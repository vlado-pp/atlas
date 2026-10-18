using System.Diagnostics.CodeAnalysis;
using Atlas.BuildingBlocks.Persistence;
using Atlas.Onboarding.Domain.Markets;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Api.Markets;

/// <summary>The configured markets, as domain policies. Built once; misconfiguration fails at startup.</summary>
public sealed class MarketPolicies(IOptions<MarketsOptions> options)
{
    private readonly Dictionary<string, MarketPolicy> _policies =
        options.Value.Markets.ToDictionary(m => m.Key, m => Create(m.Key, m.Value), StringComparer.Ordinal);

    public bool TryGet([NotNullWhen(true)] string? market, [NotNullWhen(true)] out MarketPolicy? policy)
    {
        policy = null;
        return market is not null && _policies.TryGetValue(market, out policy);
    }

    public MarketPolicy Get(string market) =>
        _policies.TryGetValue(market, out var policy) ? policy : throw new InvalidOperationException($"Market '{market}' is not configured.");

    internal static MarketPolicy Create(string market, MarketOptions options) =>
        new(
            market,
            options.Identifiers.Select(i => new IdentifierRule(i.Scheme, i.Pattern, i.CheckDigit, i.IsPassport)).ToList(),
            new ActivationPolicy(options.Activation.Mode, TimeSpan.FromDays(options.Activation.BranchVisitDeadlineDays)),
            new ComplianceReviewPolicy(TimeSpan.FromHours(options.ComplianceReview.ReviewSlaHours), options.ComplianceReview.CaseStatusPollInterval),
            options.MinimumIdentityConfidence,
            options.MinimumAge,
            options.RetentionYears,
            options.DefaultNationality ?? market);
}

/// <summary>Fails startup when a market is misconfigured or the policy markets and the database partitions disagree.</summary>
public sealed class MarketsOptionsValidator(IOptions<PersistenceOptions> persistence) : IValidateOptions<MarketsOptions>
{
    public ValidateOptionsResult Validate(string? name, MarketsOptions options)
    {
        var failures = new List<string>();

        foreach (var (market, settings) in options.Markets)
        {
            if (settings.Identifiers.Count == 0)
            {
                failures.Add($"Market {market} has no identifier rules.");
            }

            try
            {
                _ = MarketPolicies.Create(market, settings);
            }
            catch (ArgumentException ex)
            {
                failures.Add($"Market {market}: {ex.Message}");
            }
        }

        var partitions = persistence.Value.Markets.ToHashSet(StringComparer.Ordinal);
        if (!partitions.SetEquals(options.Markets.Keys))
        {
            failures.Add($"Markets [{string.Join(", ", options.Markets.Keys)}] do not match persistence partitions [{string.Join(", ", partitions)}].");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
