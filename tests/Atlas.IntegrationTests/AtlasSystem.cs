using Atlas.Accounts.Api;
using Atlas.Accounts.Api.Provisioning;
using Atlas.BuildingBlocks.Markets;
using Atlas.BuildingBlocks.Messaging;
using Atlas.BuildingBlocks.Persistence;
using Atlas.BuildingBlocks.Security;
using Atlas.Onboarding.Api;
using Atlas.Onboarding.Api.Documents;
using Atlas.Onboarding.Api.Providers;
using Atlas.Onboarding.Api.Providers.IdNow;
using Atlas.Onboarding.Api.Providers.WorldCheck;
using Atlas.ProviderStubs;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atlas.IntegrationTests;

/// <summary>
/// The whole system in one process: provider stubs, Onboarding and Accounts, wired over in-memory HTTP, against the
/// real SQL Server, RabbitMQ and Azurite from <c>infra/docker-compose.yml</c>. Each run gets its own databases, broker topology and
/// blob containers (unique run id) and removes them afterwards.
/// </summary>
public sealed class AtlasSystem : IAsyncLifetime
{
    private static readonly string[] Markets = ["MA", "MB", "MC", "MD", "ME", "MF"];
    private static readonly string[] Providers = ["IdNow", "WorldCheck"];

    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];

    public WebApplicationFactory<ProviderStubsEntryPoint> Stubs { get; private set; } = null!;

    public WebApplicationFactory<OnboardingApiEntryPoint> Onboarding { get; private set; } = null!;

    public WebApplicationFactory<AccountsApiEntryPoint> Accounts { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Stubs = new WebApplicationFactory<ProviderStubsEntryPoint>();

        Onboarding = new WebApplicationFactory<OnboardingApiEntryPoint>().WithWebHostBuilder(builder =>
        {
            ApplySharedSettings(builder);
            builder.UseSetting("Documents:ContainerPrefix", $"it{_runId}-");
            builder.UseSetting("Workflow:PollInterval", "00:00:00.200");
            builder.UseSetting("Workflow:RetryBaseDelay", "00:00:00.500");
            builder.UseSetting("Workflow:RetryMaxDelay", "00:00:02");
            foreach (var provider in Providers)
            {
                // One test's simulated outage must not open the circuit for every other test running alongside it.
                builder.UseSetting($"Providers:{provider}:CircuitBreakerMinimumThroughput", "100000");
            }

            builder.UseSetting("Providers:IdNow:BaseUrl", "http://stubs/idnow/");
            builder.UseSetting("Providers:WorldCheck:BaseUrl", "http://stubs/refinitiv/");
            foreach (var market in Markets)
            {
                builder.UseSetting($"Markets:{market}:ComplianceReview:CaseStatusPollInterval", "00:00:00.300");
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<IIdentityVerificationProvider, IdNowClient>().ConfigurePrimaryHttpMessageHandler(() => Stubs.Server.CreateHandler());
                services.AddHttpClient<ISanctionsScreeningProvider, WorldCheckClient>().ConfigurePrimaryHttpMessageHandler(() => Stubs.Server.CreateHandler());
            });
        });

        Accounts = new WebApplicationFactory<AccountsApiEntryPoint>().WithWebHostBuilder(builder =>
        {
            ApplySharedSettings(builder);
            builder.UseSetting("Onboarding:BaseUrl", "http://onboarding/");
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<OnboardingClient>().ConfigurePrimaryHttpMessageHandler(() => Onboarding.Server.CreateHandler()));
        });

        // Start in dependency order; each host migrates its schema in every market database on startup.
        _ = Stubs.Server;
        _ = Onboarding.Server;
        _ = Accounts.Server;
        return Task.CompletedTask;
    }

    public HttpClient Customer(string deviceId) => Client(Onboarding, $"dev:customer:{deviceId}");

    public HttpClient Staff(string staffId) => Client(Onboarding, $"dev:staff:{staffId}");

    public HttpClient StubControl() => Stubs.CreateClient();

    /// <summary>A unit of work against a market database of the given service, for asserting on stored state.</summary>
    public static MarketScopeHandle MarketScope<TEntryPoint>(WebApplicationFactory<TEntryPoint> service, string market)
        where TEntryPoint : class =>
        service.Services.GetRequiredService<IMarketScopeFactory>().Begin(market, Actor.System("integration-test"));

    public async Task DisposeAsync()
    {
        var connectionStrings = Onboarding.Services.GetRequiredService<MarketConnectionStrings>();
        var topology = Onboarding.Services.GetRequiredService<MessagingTopology>();
        var connections = Onboarding.Services.GetRequiredService<RabbitMqConnectionProvider>();
        var blobs = Onboarding.Services.GetRequiredService<BlobServiceClient>();
        var containerPrefix = Onboarding.Services.GetRequiredService<IOptions<DocumentStorageOptions>>().Value.ContainerPrefix;

        await using (var channel = await (await connections.GetAsync(CancellationToken.None)).CreateChannelAsync())
        {
            await topology.DeleteAsync(channel, CancellationToken.None);
        }

        await foreach (var container in blobs.GetBlobContainersAsync(prefix: containerPrefix))
        {
            await blobs.DeleteBlobContainerAsync(container.Name);
        }

        var databases = Markets.Select(m => new SqlConnectionStringBuilder(connectionStrings.For(m)).InitialCatalog).ToList();
        var master = new SqlConnectionStringBuilder(connectionStrings.For(Markets[0])) { InitialCatalog = "master" }.ConnectionString;

        await Accounts.DisposeAsync();
        await Onboarding.DisposeAsync();
        await Stubs.DisposeAsync();

        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        foreach (var database in databases)
        {
            await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Database names are generated by this fixture, not user input.
            command.CommandText = $"IF DB_ID('{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END";
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync();
        }
    }

    private static HttpClient Client<TEntryPoint>(WebApplicationFactory<TEntryPoint> service, string token)
        where TEntryPoint : class
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private void ApplySharedSettings(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Persistence:DatabaseNameFormat", $"atlas_it_{_runId}_{{market}}");
        builder.UseSetting("Persistence:MigrateOnStartup", "true");
        builder.UseSetting("RabbitMq:Prefix", $"it-{_runId}.");
        builder.UseSetting("Outbox:PollInterval", "00:00:00.200");
    }
}

[CollectionDefinition(Name)]
public sealed class AtlasSystemCollection : ICollectionFixture<AtlasSystem>
{
    public const string Name = "Atlas system";
}
