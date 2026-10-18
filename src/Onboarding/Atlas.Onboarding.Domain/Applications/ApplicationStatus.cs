namespace Atlas.Onboarding.Domain.Applications;

/// <summary>Internal lifecycle. Customers see the coarser <see cref="CustomerFacingStatus"/>.</summary>
public enum ApplicationStatus
{
    Submitted,
    IdentityVerified,
    PendingComplianceReview,
    AwaitingBranchVisit,
    AccountOpeningRequested,
    Approved,
    Rejected,
    Expired,
}

/// <summary>
/// What the mobile app is told (ADR-0003). <see cref="UnderReview"/> deliberately does not say why, so a possibly
/// sanctioned person is not tipped off.
/// </summary>
public enum CustomerFacingStatus
{
    InProgress,
    UnderReview,
    AwaitingBranchVisit,
    Approved,
    Rejected,
}

public enum RejectionReason
{
    IdentityNotVerified,
    RecaptureRequired,
    NotEligible,
    BranchVisitExpired,
}

public sealed record StatusChange(ApplicationStatus? From, ApplicationStatus To, DateTimeOffset OccurredAt, string Actor, string Note);

public sealed record BranchActivation(string StaffId, string BranchCode, string SignedFormReference, DateTimeOffset ActivatedAt);
