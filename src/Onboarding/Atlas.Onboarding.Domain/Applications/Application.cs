using Atlas.Onboarding.Domain.Common;
using Atlas.Onboarding.Domain.Markets;
using Atlas.Onboarding.Domain.Verification;

namespace Atlas.Onboarding.Domain.Applications;

/// <summary>
/// The onboarding application: an explicit state machine and the process manager of the workflow (ADR-0004).
/// It holds no personal data (that lives in <see cref="Applicants.Applicant"/>), so it can be retained, audited
/// and reasoned about after the customer's personal data has been erased (ADR-0011).
///
/// <code>
/// Submitted ─► IdentityVerified ─┬─► (activation gate) ─► AccountOpeningRequested ─► Approved
///     │                          └─► PendingComplianceReview ─► (cleared) ─► activation gate
///     └─► Rejected                          └─► (confirmed) ─► Rejected
/// activation gate (InPersonBranch) ─► AwaitingBranchVisit ─► (staff) ─► AccountOpeningRequested
///                                               └─► (deadline) ─► Expired
/// </code>
/// </summary>
public sealed class Application
{
    private const string WorkflowActor = "workflow";

    private readonly List<StatusChange> _history = [];
    private readonly List<IDomainEvent> _events = [];

    private Application()
    {
    }

    public ApplicationId Id { get; private set; }

    public string Market { get; private set; } = string.Empty;

    /// <summary>The authenticated customer (device/session) that submitted the application.</summary>
    public string SubmittedBy { get; private set; } = string.Empty;

    public DateTimeOffset SubmittedAt { get; private set; }

    /// <summary>Ten years from application (GC-2026-0814 §4).</summary>
    public DateTimeOffset RetainUntil { get; private set; }

    public ApplicationStatus Status { get; private set; }

    public RejectionReason? RejectionReason { get; private set; }

    /// <summary>When the workflow runner should next look at this application; null when it waits for someone else.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>Failed technical attempts at the current step.</summary>
    public int AttemptCount { get; private set; }

    public string? IdentityVerificationReference { get; private set; }

    public string? ScreeningCaseId { get; private set; }

    public DateTimeOffset? ComplianceReviewDueAt { get; private set; }

    public bool ComplianceReviewOverdue { get; private set; }

    public string? ReviewedBy { get; private set; }

    public DateTimeOffset? BranchVisitDeadline { get; private set; }

    public BranchActivation? BranchActivation { get; private set; }

    public Guid? AccountId { get; private set; }

    public string? CardOrderReference { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public IReadOnlyList<StatusChange> History => _history.AsReadOnly();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events.AsReadOnly();

    public CustomerFacingStatus CustomerFacingStatus => Status switch
    {
        ApplicationStatus.Submitted or ApplicationStatus.IdentityVerified or ApplicationStatus.AccountOpeningRequested => CustomerFacingStatus.InProgress,
        ApplicationStatus.PendingComplianceReview => CustomerFacingStatus.UnderReview,
        ApplicationStatus.AwaitingBranchVisit => CustomerFacingStatus.AwaitingBranchVisit,
        ApplicationStatus.Approved => CustomerFacingStatus.Approved,
        ApplicationStatus.Rejected or ApplicationStatus.Expired => CustomerFacingStatus.Rejected,
        _ => throw new InvalidOperationException($"Unmapped status {Status}."),
    };

    public static Application Submit(ApplicationId id, string submittedBy, MarketPolicy policy, DateTimeOffset now)
    {
        var application = new Application
        {
            Id = id,
            Market = policy.Market,
            SubmittedBy = submittedBy,
            SubmittedAt = now,
            RetainUntil = now.AddYears(policy.RetentionYears),
            Status = ApplicationStatus.Submitted,
            NextAttemptAt = now,
        };
        application._history.Add(new StatusChange(null, ApplicationStatus.Submitted, now, $"Customer:{submittedBy}", "Application submitted"));
        return application;
    }

    public Result RecordIdentityVerification(IdentityCheckReport report, MarketPolicy policy, DateTimeOffset now)
    {
        if (EnsureStatus(ApplicationStatus.Submitted, "record identity verification") is { } error)
        {
            return error;
        }

        IdentityVerificationReference = report.ProviderReference;

        switch (policy.AssessIdentity(report))
        {
            case IdentityVerificationOutcome.Verified:
                TransitionTo(ApplicationStatus.IdentityVerified, now, WorkflowActor, "Identity verified");
                NextAttemptAt = now;
                break;
            case IdentityVerificationOutcome.Failed:
                Reject(Applications.RejectionReason.IdentityNotVerified, now, WorkflowActor, "Document invalid or face mismatch");
                break;
            default:
                Reject(Applications.RejectionReason.RecaptureRequired, now, WorkflowActor, "Identity check inconclusive");
                break;
        }

        return Result.Success();
    }

    public Result RecordScreening(ScreeningResult result, MarketPolicy policy, DateTimeOffset now)
    {
        if (EnsureStatus(ApplicationStatus.IdentityVerified, "record screening") is { } error)
        {
            return error;
        }

        ScreeningCaseId = result.CaseId;

        if (result.Outcome == ScreeningOutcome.Clear)
        {
            PassChecks(policy, now, WorkflowActor);
            return Result.Success();
        }

        // GC-2026-0814 §3: a possible match is referred to a compliance officer. Never decided automatically.
        TransitionTo(ApplicationStatus.PendingComplianceReview, now, WorkflowActor, "Possible sanctions/PEP match referred for compliance review");
        ComplianceReviewDueAt = now + policy.ComplianceReview.ReviewSla;
        NextAttemptAt = now + policy.ComplianceReview.CaseStatusPollInterval;
        return Result.Success();
    }

    /// <summary>
    /// The case is still open. Schedules the next check. Past the review SLA the application is flagged overdue,
    /// and nothing else happens: the clock never decides for the officer (ADR-0005).
    /// </summary>
    /// <returns>True if the application became overdue with this call.</returns>
    public bool ComplianceReviewStillPending(MarketPolicy policy, DateTimeOffset now)
    {
        if (Status != ApplicationStatus.PendingComplianceReview)
        {
            throw new InvalidOperationException($"Application {Id} is not awaiting compliance review.");
        }

        NextAttemptAt = now + policy.ComplianceReview.CaseStatusPollInterval;
        if (ComplianceReviewOverdue || now < ComplianceReviewDueAt)
        {
            return false;
        }

        ComplianceReviewOverdue = true;
        return true;
    }

    public Result RecordComplianceReview(ComplianceReviewResolution resolution, MarketPolicy policy, DateTimeOffset now)
    {
        if (EnsureStatus(ApplicationStatus.PendingComplianceReview, "record a compliance review") is { } error)
        {
            return error;
        }

        ReviewedBy = resolution.OfficerId;
        var officer = $"ComplianceOfficer:{resolution.OfficerId}";

        if (resolution.Decision == ComplianceReviewDecision.Cleared)
        {
            PassChecks(policy, now, officer);
        }
        else
        {
            Reject(Applications.RejectionReason.NotEligible, now, officer, "Compliance review confirmed the match");
        }

        return Result.Success();
    }

    public Result RecordBranchActivation(BranchActivation activation)
    {
        if (EnsureStatus(ApplicationStatus.AwaitingBranchVisit, "record a branch activation") is { } error)
        {
            return error;
        }

        if (activation.ActivatedAt >= BranchVisitDeadline)
        {
            return Error.Conflict("application.branch_visit_expired", "The deadline for the branch visit has passed.");
        }

        BranchActivation = activation;
        RequestAccountOpening(activation.ActivatedAt, $"Staff:{activation.StaffId}", $"Identity confirmed and form signed at branch {activation.BranchCode}");
        return Result.Success();
    }

    public Result ExpireBranchVisit(DateTimeOffset now)
    {
        if (EnsureStatus(ApplicationStatus.AwaitingBranchVisit, "expire") is { } error)
        {
            return error;
        }

        if (now < BranchVisitDeadline)
        {
            NextAttemptAt = BranchVisitDeadline;
            return Result.Success();
        }

        RejectionReason = Applications.RejectionReason.BranchVisitExpired;
        TransitionTo(ApplicationStatus.Expired, now, WorkflowActor, "No branch visit before the deadline");
        NextAttemptAt = null;
        DecidedAt = now;
        return Result.Success();
    }

    public Result RecordAccountProvisioned(Guid accountId, string cardOrderReference, DateTimeOffset now)
    {
        if (Status == ApplicationStatus.Approved && AccountId == accountId)
        {
            return Result.Success(); // Redelivered message: already applied.
        }

        if (EnsureStatus(ApplicationStatus.AccountOpeningRequested, "record account provisioning") is { } error)
        {
            return error;
        }

        AccountId = accountId;
        CardOrderReference = cardOrderReference;
        DecidedAt = now;
        TransitionTo(ApplicationStatus.Approved, now, "accounts", "Account opened and card ordered");
        return Result.Success();
    }

    /// <summary>A provider was unavailable. Technical failure is never a decision about the customer (ADR-0006).</summary>
    public void ScheduleRetry(DateTimeOffset nextAttemptAt)
    {
        AttemptCount++;
        NextAttemptAt = nextAttemptAt;
    }

    public void ClearDomainEvents() => _events.Clear();

    private void PassChecks(MarketPolicy policy, DateTimeOffset now, string actor)
    {
        if (policy.Activation.Mode == ActivationMode.InPersonBranch)
        {
            TransitionTo(ApplicationStatus.AwaitingBranchVisit, now, actor, "Checks passed; in-person activation required by market policy");
            BranchVisitDeadline = now + policy.Activation.BranchVisitDeadline;
            NextAttemptAt = BranchVisitDeadline;
            return;
        }

        RequestAccountOpening(now, actor, "Checks passed");
    }

    private void RequestAccountOpening(DateTimeOffset now, string actor, string note)
    {
        TransitionTo(ApplicationStatus.AccountOpeningRequested, now, actor, note);
        NextAttemptAt = null;
        _events.Add(new AccountOpeningRequestedEvent(Id, now));
    }

    private void Reject(RejectionReason reason, DateTimeOffset now, string actor, string note)
    {
        RejectionReason = reason;
        DecidedAt = now;
        TransitionTo(ApplicationStatus.Rejected, now, actor, note);
        NextAttemptAt = null;
    }

    private void TransitionTo(ApplicationStatus to, DateTimeOffset now, string actor, string note)
    {
        var customerFacingBefore = CustomerFacingStatus;
        _history.Add(new StatusChange(Status, to, now, actor, note));
        Status = to;
        AttemptCount = 0;

        if (CustomerFacingStatus != customerFacingBefore)
        {
            _events.Add(new CustomerFacingStatusChangedEvent(Id, CustomerFacingStatus, RejectionReason, now));
        }
    }

    private Error? EnsureStatus(ApplicationStatus expected, string action) =>
        Status == expected
            ? null
            : Error.Conflict("application.invalid_transition", $"Cannot {action} while the application is {Status}.");
}
