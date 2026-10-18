using Atlas.BuildingBlocks.Markets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.BuildingBlocks.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddMarketPartitioning(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PersistenceOptions>()
            .Bind(configuration.GetSection(PersistenceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<MarketConnectionStrings>();
        services.AddSingleton<IMarketScopeFactory, MarketScopeFactory>();
        services.AddScoped<MarketScope>();
        return services;
    }

    /// <summary>
    /// Registers a DbContext whose connection is chosen by the current <see cref="MarketScope"/>: one database per
    /// market, one schema per service inside it (ADR-0009).
    /// </summary>
    public static IServiceCollection AddMarketDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            var market = sp.GetRequiredService<MarketScope>().Market;
            var connectionString = sp.GetRequiredService<MarketConnectionStrings>().For(market);
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", schema));
        });

        services.AddHostedService<MarketDatabaseMigrator<TContext>>();
        return services;
    }
}
