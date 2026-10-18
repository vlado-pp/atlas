using Atlas.Onboarding.Domain.Applications;

namespace Atlas.Onboarding.Api.Applications;

/// <summary>The wire vocabulary of the public API (ADR-0003).</summary>
public static class PublicStatus
{
    public static string Of(CustomerFacingStatus status) => status switch
    {
        CustomerFacingStatus.InProgress => "IN_PROGRESS",
        CustomerFacingStatus.UnderReview => "UNDER_REVIEW",
        CustomerFacingStatus.AwaitingBranchVisit => "AWAITING_BRANCH_VISIT",
        CustomerFacingStatus.Approved => "APPROVED",
        CustomerFacingStatus.Rejected => "REJECTED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    /// <summary>Customer-safe reason codes. A confirmed sanctions match is reported as NOT_ELIGIBLE, nothing more.</summary>
    public static string? ReasonOf(CustomerFacingStatus status, RejectionReason? reason) =>
        status != CustomerFacingStatus.Rejected
            ? null
            : reason switch
            {
                RejectionReason.IdentityNotVerified => "IDENTITY_NOT_VERIFIED",
                RejectionReason.RecaptureRequired => "RECAPTURE_REQUIRED",
                RejectionReason.NotEligible => "NOT_ELIGIBLE",
                RejectionReason.BranchVisitExpired => "EXPIRED",
                _ => null,
            };
}

public sealed record ApplicationStatusResponse(string ApplicationId, string Status, string? Reason)
{
    public static ApplicationStatusResponse From(Application application) =>
        new(application.Id.Value, PublicStatus.Of(application.CustomerFacingStatus), PublicStatus.ReasonOf(application.CustomerFacingStatus, application.RejectionReason));
}
