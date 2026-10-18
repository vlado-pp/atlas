using Atlas.Onboarding.Domain.Applicants;
using Atlas.Onboarding.Domain.Common;
using Atlas.Onboarding.Domain.Verification;

namespace Atlas.Onboarding.Domain.Markets;

public enum ActivationMode
{
    /// <summary>Account is opened and the card ordered as soon as checks pass.</summary>
    Remote,

    /// <summary>Applicant must attend a branch and sign before the account may be activated (Annex B, MD).</summary>
    InPersonBranch,
}

public sealed record ActivationPolicy(ActivationMode Mode, TimeSpan BranchVisitDeadline);

public sealed record ComplianceReviewPolicy(TimeSpan ReviewSla, TimeSpan CaseStatusPollInterval);

/// <summary>
/// Everything that differs between markets, built from configuration. This object is how "market differences
/// are configuration, not code" (AC4) is met: business logic asks the policy and never tests a market code.
/// </summary>
public sealed class MarketPolicy(
    string market,
    IReadOnlyList<IdentifierRule> identifierRules,
    ActivationPolicy activation,
    ComplianceReviewPolicy complianceReview,
    decimal minimumIdentityConfidence,
    int minimumAge,
    int retentionYears,
    string defaultNationality)
{
    public string Market { get; } = market;

    public IReadOnlyList<IdentifierRule> IdentifierRules { get; } = identifierRules;

    public ActivationPolicy Activation { get; } = activation;

    public ComplianceReviewPolicy ComplianceReview { get; } = complianceReview;

    public decimal MinimumIdentityConfidence { get; } = minimumIdentityConfidence;

    public int MinimumAge { get; } = minimumAge;

    public int RetentionYears { get; } = retentionYears;

    public string DefaultNationality { get; } = defaultNationality;

    private IdentifierRule? NationalIdRule => IdentifierRules.FirstOrDefault(r => !r.IsPassport);

    private IdentifierRule? PassportRule => IdentifierRules.FirstOrDefault(r => r.IsPassport);

    /// <summary>
    /// "Required" means "one acceptable identifier for this market" (ADR-0008): the market's national identifier,
    /// or a passport number where the market accepts one in lieu (MF).
    /// </summary>
    public Result<CustomerIdentifier> ResolveIdentifier(string? nationalId, string? passportNumber)
    {
        var hasNationalId = !string.IsNullOrWhiteSpace(nationalId);
        var hasPassport = !string.IsNullOrWhiteSpace(passportNumber);

        if (hasNationalId && hasPassport)
        {
            return Error.Validation("nationalId", "identifier.ambiguous", "Provide either nationalId or passportNumber, not both.");
        }

        if (hasPassport)
        {
            return PassportRule is { } passportRule
                ? passportRule.Validate("passportNumber", passportNumber!.Trim())
                : Error.Validation("passportNumber", "identifier.passport_not_accepted", $"Market {Market} does not accept a passport number in lieu of a national identifier; nationalId is required.");
        }

        if (hasNationalId)
        {
            return NationalIdRule is { } nationalRule
                ? nationalRule.Validate("nationalId", nationalId!.Trim())
                : Error.Validation("nationalId", "identifier.not_configured", $"Market {Market} has no national identifier configured.");
        }

        return PassportRule is null
            ? Error.Validation("nationalId", "identifier.required", "nationalId is required.")
            : Error.Validation("nationalId", "identifier.required", "nationalId or passportNumber is required.");
    }

    public bool IsOldEnough(DateOnly dateOfBirth, DateOnly today)
    {
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth > today.AddYears(-age))
        {
            age--;
        }

        return age >= MinimumAge;
    }

    /// <summary>Turns what the identity provider reported into a decision. Anything that is not a clear pass is not a pass (ADR-0006).</summary>
    public IdentityVerificationOutcome AssessIdentity(IdentityCheckReport report) => report switch
    {
        { Document: DocumentAuthenticity.Invalid } or { FaceMatch: false } => IdentityVerificationOutcome.Failed,
        { Document: DocumentAuthenticity.Valid } when report.Confidence >= MinimumIdentityConfidence => IdentityVerificationOutcome.Verified,
        _ => IdentityVerificationOutcome.Inconclusive,
    };
}
