using Atlas.BuildingBlocks.Persistence;
using Atlas.BuildingBlocks.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atlas.BuildingBlocks.Markets;

public interface IMarketScopeFactory
{
    IReadOnlyCollection<string> Markets { get; }

    bool IsKnownMarket(string market);

    /// <summary>Starts a DI scope bound to one market partition and one actor.</summary>
    MarketScopeHandle Begin(string market, Actor actor);
}

public sealed class MarketScopeFactory(IServiceScopeFactory scopeFactory, IOptions<PersistenceOptions> options) : IMarketScopeFactory
{
    public IReadOnlyCollection<string> Markets => options.Value.Markets.AsReadOnly();

    public bool IsKnownMarket(string market) => options.Value.Markets.Contains(market, StringComparer.Ordinal);

    public MarketScopeHandle Begin(string market, Actor actor)
    {
        if (!IsKnownMarket(market))
        {
            throw new InvalidOperationException($"Market '{market}' is not configured.");
        }

        var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<MarketScope>().Enter(market, actor);
        return new MarketScopeHandle(scope);
    }
}

public sealed class MarketScopeHandle(AsyncServiceScope scope) : IAsyncDisposable
{
    public IServiceProvider Services => scope.ServiceProvider;

    public T Get<T>() where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
