using Atlas.BuildingBlocks.Markets;
using Atlas.BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlas.BuildingBlocks.Persistence;

/// <summary>
/// Applies EF migrations to every market database at startup when <see cref="PersistenceOptions.MigrateOnStartup"/>
/// is on (local development and tests). Retries because SQL Server may still be starting and two services may
/// race to create the same market database.
/// </summary>
public sealed class MarketDatabaseMigrator<TContext>(
    IMarketScopeFactory scopes,
    IOptions<PersistenceOptions> options,
    ILogger<MarketDatabaseMigrator<TContext>> logger) : IHostedService
    where TContext : DbContext
{
    private const int MaxAttempts = 10;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MigrateOnStartup)
        {
            return;
        }

        foreach (var market in options.Value.Markets)
        {
            await MigrateAsync(market, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task MigrateAsync(string market, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopes.Begin(market, Actor.System("migrator"));
                await scope.Get<TContext>().Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Migrated {Context} for market {Market}", typeof(TContext).Name, market);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Migrating {Context} for market {Market} failed (attempt {Attempt}); retrying", typeof(TContext).Name, market, attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }
}
