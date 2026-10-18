using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Atlas.Onboarding.Domain.Applications;

/// <summary>
/// Surrogate identifier that embeds the market (<c>mb-0199f6…</c>), so any request or message can be routed to the
/// right market partition without a cross-border lookup table (ADR-0009). Time-ordered (GUID v7) for index locality.
/// </summary>
public readonly partial record struct ApplicationId
{
    private ApplicationId(string value) => Value = value;

    public string Value { get; }

    public string Market => Value[..2].ToUpperInvariant();

    public static ApplicationId New(string market, DateTimeOffset now) =>
        new($"{market.ToLowerInvariant()}-{Guid.CreateVersion7(now):N}");

    public static ApplicationId FromTrusted(string value) =>
        TryParse(value, out var id) ? id : throw new ArgumentException($"'{value}' is not a valid application id.", nameof(value));

    public static bool TryParse([NotNullWhen(true)] string? value, out ApplicationId id)
    {
        if (value is not null && Format().IsMatch(value))
        {
            id = new ApplicationId(value);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z]{2}-[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex Format();
}
