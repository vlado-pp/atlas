using Atlas.BuildingBlocks.Security;

namespace Atlas.BuildingBlocks.Markets;

/// <summary>
/// Scoped holder for the market partition and the actor of the current unit of work. Everything that touches
/// a market database or customer data resolves it, so a unit of work can never span two markets.
/// </summary>
public sealed class MarketScope
{
    private string? _market;
    private Actor? _actor;

    public string Market => _market ?? throw new InvalidOperationException("No market scope has been entered for this unit of work.");

    public Actor Actor => _actor ?? throw new InvalidOperationException("No market scope has been entered for this unit of work.");

    internal void Enter(string market, Actor actor)
    {
        if (_market is not null)
        {
            throw new InvalidOperationException($"Market scope is already set to {_market}.");
        }

        _market = market;
        _actor = actor;
    }
}
