using Atlas.Onboarding.Api.CustomerRecords;
using Atlas.Onboarding.Api.Markets;
using Atlas.Onboarding.Api.Persistence;
using Atlas.Onboarding.Api.Providers;
using Atlas.Onboarding.Domain.Applicants;
using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Domain.Common;
using Atlas.Onboarding.Domain.Markets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Api.Workflow;

/// <summary>
/// Runs the next step for one application, based on its current state. The aggregate decides; this class only
/// fetches what the decision needs (provider answers, customer data) and persists the result in one transaction.
/// </summary>
public sealed class ApplicationWorkflow(
    OnboardingDbContext db,
    ICustomerRecords customerRecords,
    IIdentityVerificationProvider identityVerification,
    ISanctionsScreeningProvider screening,
    MarketPolicies policies,
    OnboardingMetrics metrics,
    TimeProvider clock,
    IOptions<WorkflowOptions> options,
    ILogger<ApplicationWorkflow> logger)
{
    public async Task AdvanceAsync(ApplicationId applicationId, CancellationToken cancellationToken)
    {
        var application = await db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        if (application is null)
        {
            return;
        }

        var policy = policies.Get(application.Market);
        var statusBefore = application.CustomerFacingStatus;

        try
        {
            await RunStepAsync(application, policy, cancellationToken);
        }
        catch (ProviderUnavailableException ex)
        {
            ScheduleRetry(application, ex);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (application.CustomerFacingStatus != statusBefore)
        {
            metrics.StatusReached(application, clock.GetUtcNow());
        }
    }

    private Task RunStepAsync(Application application, MarketPolicy policy, CancellationToken cancellationToken) => application.Status switch
    {
        ApplicationStatus.Submitted => VerifyIdentityAsync(application, policy, cancellationToken),
        ApplicationStatus.IdentityVerified => ScreenAsync(application, policy, cancellationToken),
        ApplicationStatus.PendingComplianceReview => CheckComplianceReviewAsync(application, policy, cancellationToken),
        ApplicationStatus.AwaitingBranchVisit => Apply(application.ExpireBranchVisit(clock.GetUtcNow())),
        _ => Unexpected(application),
    };

    private async Task VerifyIdentityAsync(Application application, MarketPolicy policy, CancellationToken cancellationToken)
    {
        var applicant = await LoadApplicantAsync(application, AccessPurpose.IdentityVerification, cancellationToken);
        var document = applicant.IdentityDocument ?? throw new InvalidOperationException($"Applicant {application.Id} has no identity document.");
        var selfie = applicant.Selfie ?? throw new InvalidOperationException($"Applicant {application.Id} has no selfie.");

        var request = new IdentityVerificationRequest(
            document.Type,
            await customerRecords.ReadDocumentAsync(applicant, document, AccessPurpose.IdentityVerification, cancellationToken),
            await customerRecords.ReadDocumentAsync(applicant, selfie, AccessPurpose.IdentityVerification, cancellationToken));

        var report = await identityVerification.VerifyAsync(request, cancellationToken);
        await Apply(application.RecordIdentityVerification(report, policy, clock.GetUtcNow()));
    }

    private async Task ScreenAsync(Application application, MarketPolicy policy, CancellationToken cancellationToken)
    {
        var applicant = await LoadApplicantAsync(application, AccessPurpose.SanctionsScreening, cancellationToken);
        var result = await screening.ScreenAsync(new ScreeningRequest(applicant.FullName, applicant.DateOfBirth, applicant.Nationality), cancellationToken);
        await Apply(application.RecordScreening(result, policy, clock.GetUtcNow()));
    }

    private async Task CheckComplianceReviewAsync(Application application, MarketPolicy policy, CancellationToken cancellationToken)
    {
        var resolution = await screening.GetCaseResolutionAsync(application.ScreeningCaseId!, cancellationToken);
        if (resolution is not null)
        {
            await Apply(application.RecordComplianceReview(resolution, policy, clock.GetUtcNow()));
            return;
        }

        if (application.ComplianceReviewStillPending(policy, clock.GetUtcNow()))
        {
            logger.LogWarning(
                "Compliance review for {ApplicationId} in {Market} is overdue (due {DueAt}); waiting for the officer, no automatic decision",
                application.Id,
                application.Market,
                application.ComplianceReviewDueAt);
        }
    }

    private async Task<Applicant> LoadApplicantAsync(Application application, AccessPurpose purpose, CancellationToken cancellationToken) =>
        await customerRecords.FindAsync(application.Id, purpose, cancellationToken)
            ?? throw new InvalidOperationException($"Applicant record for {application.Id} is missing.");

    private void ScheduleRetry(Application application, ProviderUnavailableException ex)
    {
        var settings = options.Value;
        var delay = RetryBackoff.For(application.AttemptCount, settings.RetryBaseDelay, settings.RetryMaxDelay);
        application.ScheduleRetry(clock.GetUtcNow() + delay);
        metrics.ProviderFailed(ex.Provider);

        if (application.AttemptCount >= settings.StalledAfterAttempts)
        {
            logger.LogError(ex, "Application {ApplicationId} is stalled at {Status} after {Attempts} attempts; still retrying", application.Id, application.Status, application.AttemptCount);
        }
        else
        {
            logger.LogWarning("Provider unavailable for {ApplicationId} at {Status} ({Reason}); retrying in {RetryDelay}", application.Id, application.Status, ex.Message, delay);
        }
    }

    private Task Unexpected(Application application)
    {
        logger.LogWarning("Application {ApplicationId} was claimed in status {Status}, which has no workflow step", application.Id, application.Status);
        return Task.CompletedTask;
    }

    /// <summary>The workflow only calls transitions valid for the state it dispatched on; a failure here is a bug.</summary>
    private static Task Apply(Result result) =>
        result.IsSuccess ? Task.CompletedTask : throw new InvalidOperationException(result.Error!.Message);
}
