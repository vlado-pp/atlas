using Atlas.BuildingBlocks.Health;
using Atlas.BuildingBlocks.Messaging;
using Atlas.BuildingBlocks.Observability;
using Atlas.BuildingBlocks.Persistence;
using Atlas.BuildingBlocks.Security;
using Atlas.Contracts;
using Atlas.Onboarding.Api;
using Atlas.Onboarding.Api.Applications;
using Atlas.Onboarding.Api.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddAtlasLogging("onboarding");
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddAtlasAuthentication(builder.Environment);
builder.Services.AddSingleton(TimeProvider.System);

// Order matters: the migrator (registered with the DbContext) must start before the workers that use the databases.
builder.Services.AddMarketPartitioning(builder.Configuration);
builder.Services.AddMarketDbContext<OnboardingDbContext>(OnboardingDbContext.Schema);
builder.Services.AddRabbitMqMessaging(builder.Configuration);
builder.Services.AddOutboxDispatcher<OnboardingDbContext>(builder.Configuration);
builder.Services.AddMessageConsumer<CustomerAccountProvisioned, CustomerAccountProvisionedHandler>(
    MessagingTopology.AccountProvisionedQueue,
    Actor.Service("accounts"));
builder.Services.AddOnboarding(builder.Configuration);
builder.Services.AddAtlasHealthChecks<OnboardingDbContext>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi().AllowAnonymous();
app.MapAtlasHealthChecks();
app.MapApplicationEndpoints();
app.MapBranchEndpoints();
app.MapInternalEndpoints();

await app.RunAsync();
