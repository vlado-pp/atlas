using Atlas.BuildingBlocks.Markets;
using Atlas.BuildingBlocks.Messaging;
using Atlas.BuildingBlocks.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Atlas.BuildingBlocks.Health;

public static class HealthCheckExtensions
{
    private const string ReadyTag = "ready";

    public static IHealthChecksBuilder AddAtlasHealthChecks<TContext>(this IServiceCollection services)
        where TContext : DbContext =>
        services.AddHealthChecks()
            .AddCheck<MarketDatabasesHealthCheck<TContext>>("market-databases", tags: [ReadyTag])
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: [ReadyTag]);

    public static IEndpointRouteBuilder MapAtlasHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) }).AllowAnonymous();
        return endpoints;
    }
}

internal sealed class MarketDatabasesHealthCheck<TContext>(IMarketScopeFactory scopes) : IHealthCheck
    where TContext : DbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var unreachable = new List<string>();
        foreach (var market in scopes.Markets)
        {
            await using var scope = scopes.Begin(market, Actor.System("health-check"));
            if (!await scope.Get<TContext>().Database.CanConnectAsync(cancellationToken))
            {
                unreachable.Add(market);
            }
        }

        return unreachable.Count == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"Unreachable market databases: {string.Join(", ", unreachable)}");
    }
}

internal sealed class RabbitMqHealthCheck(RabbitMqConnectionProvider connections) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await connections.GetAsync(cancellationToken);
            return connection.IsOpen ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded("Connection is recovering.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ unreachable.", ex);
        }
    }
}
