namespace Atlas.Onboarding.Domain.Verification;

/// <summary>Document authenticity in our terms. Provider enums are mapped to this by the anti-corruption layer.</summary>
public enum DocumentAuthenticity
{
    Valid,
    Invalid,
    Inconclusive,
}

/// <summary>What the identity provider reported, translated into domain terms but not yet judged.</summary>
public sealed record IdentityCheckReport(string ProviderReference, DocumentAuthenticity Document, bool FaceMatch, decimal Confidence);

public enum IdentityVerificationOutcome
{
    Verified,
    Failed,
    Inconclusive,
}

public enum ScreeningOutcome
{
    Clear,
    PossibleMatch,
}

public sealed record ScreeningResult(string CaseId, ScreeningOutcome Outcome);

public enum ComplianceReviewDecision
{
    /// <summary>The officer judged the possible match a false positive.</summary>
    Cleared,

    /// <summary>The officer confirmed the match.</summary>
    Confirmed,
}

public sealed record ComplianceReviewResolution(ComplianceReviewDecision Decision, string OfficerId, DateTimeOffset ResolvedAt);
