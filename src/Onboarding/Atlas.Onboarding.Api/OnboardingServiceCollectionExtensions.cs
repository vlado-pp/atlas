using Atlas.Onboarding.Api.Applications.Submit;
using Atlas.Onboarding.Api.CustomerRecords;
using Atlas.Onboarding.Api.Documents;
using Atlas.Onboarding.Api.Markets;
using Atlas.Onboarding.Api.Persistence;
using Atlas.Onboarding.Api.Providers;
using Atlas.Onboarding.Api.Providers.IdNow;
using Atlas.Onboarding.Api.Providers.WorldCheck;
using Atlas.Onboarding.Api.Workflow;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Api;

public static class OnboardingServiceCollectionExtensions
{
    public static IServiceCollection AddOnboarding(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMarkets(configuration);
        services.AddDocuments(configuration);
        services.AddProviders(configuration);

        // Customer data: the store, wrapped by the audit decorator. Nothing resolves the undecorated store directly.
        services.AddScoped<CustomerRecords.CustomerRecords>();
        services.AddScoped<ICustomerRecords>(sp => new AuditingCustomerRecords(
            sp.GetRequiredService<CustomerRecords.CustomerRecords>(),
            sp.GetRequiredService<OnboardingDbContext>(),
            sp.GetRequiredService<Atlas.BuildingBlocks.Markets.MarketScope>(),
            sp.GetRequiredService<TimeProvider>()));

        services.AddSingleton<SubmitApplicationValidator>();
        services.AddScoped<SubmitApplicationHandler>();

        services.AddOptions<WorkflowOptions>().Bind(configuration.GetSection(WorkflowOptions.SectionName));
        services.AddSingleton<OnboardingMetrics>();
        services.AddScoped<WorkClaims>();
        services.AddScoped<ApplicationWorkflow>();
        services.AddHostedService<WorkflowRunner>();

        return services;
    }

    private static void AddMarkets(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MarketsOptions>()
            .Configure(o => configuration.GetSection(MarketsOptions.SectionName).Bind(o.Markets))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MarketsOptions>, MarketsOptionsValidator>();
        services.AddSingleton<MarketPolicies>();
    }

    private static void AddDocuments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DocumentStorageOptions>().Bind(configuration.GetSection(DocumentStorageOptions.SectionName));
        services.AddSingleton(sp => new BlobServiceClient(sp.GetRequiredService<IOptions<DocumentStorageOptions>>().Value.ConnectionString));
        services.AddSingleton<IDocumentStore, BlobDocumentStore>();
    }

    private static void AddProviders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ProvidersOptions>()
            .Bind(configuration.GetSection(ProvidersOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IIdentityVerificationProvider, IdNowClient>((sp, http) =>
                http.BaseAddress = sp.GetRequiredService<IOptions<ProvidersOptions>>().Value.IdNow.BaseUrl)
            .AddResilienceHandler("idnow", (pipeline, context) =>
                ProviderResilience.Configure(pipeline, context.ServiceProvider.GetRequiredService<IOptions<ProvidersOptions>>().Value.IdNow));

        services.AddHttpClient<ISanctionsScreeningProvider, WorldCheckClient>((sp, http) =>
                http.BaseAddress = sp.GetRequiredService<IOptions<ProvidersOptions>>().Value.WorldCheck.BaseUrl)
            .AddResilienceHandler("worldcheck", (pipeline, context) =>
                ProviderResilience.Configure(pipeline, context.ServiceProvider.GetRequiredService<IOptions<ProvidersOptions>>().Value.WorldCheck));
    }
}
