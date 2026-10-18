using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Atlas.BuildingBlocks.Persistence;

public sealed class MarketConnectionStrings(IOptions<PersistenceOptions> options)
{
    public string For(string market)
    {
        var settings = options.Value;
        if (settings.ConnectionStringOverrides.TryGetValue(market, out var dedicated))
        {
            return dedicated;
        }

        var builder = new SqlConnectionStringBuilder(settings.SqlServer)
        {
            InitialCatalog = settings.DatabaseNameFormat.Replace("{market}", market, StringComparison.Ordinal).ToUpperInvariant(),
        };
        return builder.ConnectionString;
    }
}
