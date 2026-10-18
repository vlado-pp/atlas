using System.ComponentModel.DataAnnotations;

namespace Atlas.BuildingBlocks.Persistence;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    /// <summary>Base SQL Server connection string; the database name is set per market.</summary>
    [Required]
    public string SqlServer { get; set; } = string.Empty;

    /// <summary>Database name per market, e.g. <c>atlas_{market}</c> becomes <c>ATLAS_MB</c>.</summary>
    [Required]
    public string DatabaseNameFormat { get; set; } = "atlas_{market}";

    [MinLength(1)]
    public IList<string> Markets { get; set; } = [];

    /// <summary>
    /// Full connection string per market. This is how a market's data moves to an in-country server
    /// without a code change (ADR-0009).
    /// </summary>
    public IDictionary<string, string> ConnectionStringOverrides { get; set; } = new Dictionary<string, string>();

    public bool MigrateOnStartup { get; set; }
}
