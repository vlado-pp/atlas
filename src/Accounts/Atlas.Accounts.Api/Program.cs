using Atlas.Accounts.Api.Gateways;
using Atlas.Accounts.Api.Persistence;
using Atlas.Accounts.Api.Provisioning;
using Atlas.BuildingBlocks.Health;
using Atlas.BuildingBlocks.Messaging;
using Atlas.BuildingBlocks.Observability;
using Atlas.BuildingBlocks.Persistence;
using Atlas.BuildingBlocks.Security;
using Atlas.Contracts;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddAtlasLogging("accounts");
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddMarketPartitioning(builder.Configuration);
builder.Services.AddMarketDbContext<AccountsDbContext>(AccountsDbContext.Schema);
builder.Services.AddRabbitMqMessaging(builder.Configuration);
builder.Services.AddOutboxDispatcher<AccountsDbContext>(builder.Configuration);
builder.Services.AddMessageConsumer<AccountOpeningRequested, AccountOpeningRequestedHandler>(
    MessagingTopology.AccountOpeningQueue,
    Actor.Service("onboarding"));

builder.Services.AddOptions<FakeGatewayOptions>().Bind(builder.Configuration.GetSection(FakeGatewayOptions.SectionName));
builder.Services.AddSingleton<ICoreBankingGateway, FakeCoreBankingGateway>();
builder.Services.AddSingleton<ICardProcessorGateway, FakeCardProcessorGateway>();

builder.Services.AddOptions<OnboardingClientOptions>().Bind(builder.Configuration.GetSection(OnboardingClientOptions.SectionName));
builder.Services.AddHttpClient<OnboardingClient>((sp, http) =>
        OnboardingClient.Configure(http, sp.GetRequiredService<IOptions<OnboardingClientOptions>>().Value))
    .AddStandardResilienceHandler();

builder.Services.AddAtlasHealthChecks<AccountsDbContext>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.MapAtlasHealthChecks();

await app.RunAsync();
