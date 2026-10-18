using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atlas.BuildingBlocks.Security;

public static class AtlasAuthentication
{
    public const string ActorTypeClaim = "atlas:actor_type";
    public const string CustomerPolicy = "customer";
    public const string StaffPolicy = "staff";
    public const string ServicePolicy = "service";

    /// <summary>
    /// Registers authentication. Only development tokens are implemented; outside Development/Testing the
    /// service refuses to start rather than run without real identities (ADR-0010).
    /// </summary>
    public static IServiceCollection AddAtlasAuthentication(this IServiceCollection services, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                "Only development tokens are implemented. Configure an OIDC/JWT scheme before running outside Development (ADR-0010).");
        }

        services
            .AddAuthentication(DevelopmentTokenHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, DevelopmentTokenHandler>(DevelopmentTokenHandler.SchemeName, configureOptions: null);

        services.AddAuthorizationBuilder()
            .AddPolicy(CustomerPolicy, p => p.RequireClaim(ActorTypeClaim, "customer"))
            .AddPolicy(StaffPolicy, p => p.RequireClaim(ActorTypeClaim, "staff"))
            .AddPolicy(ServicePolicy, p => p.RequireClaim(ActorTypeClaim, "service"));

        return services;
    }

    public static Actor ToActor(this ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated principal has no identifier.");

        var type = principal.FindFirstValue(ActorTypeClaim) switch
        {
            "customer" => ActorType.Customer,
            "staff" => ActorType.Staff,
            "service" => ActorType.Service,
            var other => throw new InvalidOperationException($"Unknown actor type '{other}'."),
        };

        return new Actor(type, id);
    }
}
