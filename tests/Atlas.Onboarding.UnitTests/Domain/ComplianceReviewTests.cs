using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Domain.Verification;

namespace Atlas.Onboarding.UnitTests.Domain;

/// <summary>GC-2026-0814 §3: a possible match is referred to a compliance officer and never decided automatically.</summary>
public sealed class ComplianceReviewTests
{
    private readonly Atlas.Onboarding.Domain.Markets.MarketPolicy _remote = TestData.RemoteMarket();

    [Fact]
    public void RecordScreening_PossibleMatch_MovesToPendingComplianceReviewWithoutDeciding()
    {
        var application = ApplicationBuilder.In(_remote).IdentityVerified().Build();

        application.RecordScreening(TestData.PossibleMatch(), _remote, TestData.Now);

        application.Status.ShouldBe(ApplicationStatus.PendingComplianceReview);
        application.CustomerFacingStatus.ShouldBe(CustomerFacingStatus.UnderReview);
        application.RejectionReason.ShouldBeNull();
        application.DecidedAt.ShouldBeNull();
        application.ScreeningCaseId.ShouldBe("wc_1");
        application.ComplianceReviewDueAt.ShouldBe(TestData.Now.AddHours(48));
        application.DomainEvents.OfType<AccountOpeningRequestedEvent>().ShouldBeEmpty();
    }

    [Fact]
    public void ComplianceReviewStillPending_PastTheSla_FlagsOverdueButNeverDecides()
    {
        var application = ApplicationBuilder.In(_remote).Screened(TestData.PossibleMatch()).Build();
        var afterSla = TestData.Now.AddHours(49);

        var becameOverdue = application.ComplianceReviewStillPending(_remote, afterSla);
        var reportedAgain = application.ComplianceReviewStillPending(_remote, afterSla.AddMinutes(1));

        becameOverdue.ShouldBeTrue();
        reportedAgain.ShouldBeFalse();
        application.ComplianceReviewOverdue.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.PendingComplianceReview);
        application.NextAttemptAt.ShouldBe(afterSla.AddMinutes(1) + _remote.ComplianceReview.CaseStatusPollInterval);
    }

    [Fact]
    public void ComplianceReviewStillPending_WithinTheSla_OnlySchedulesTheNextCheck()
    {
        var application = ApplicationBuilder.In(_remote).Screened(TestData.PossibleMatch()).Build();

        var becameOverdue = application.ComplianceReviewStillPending(_remote, TestData.Now.AddHours(1));

        becameOverdue.ShouldBeFalse();
        application.ComplianceReviewOverdue.ShouldBeFalse();
    }

    [Fact]
    public void RecordComplianceReview_Cleared_ContinuesToAccountOpeningAndRecordsTheOfficer()
    {
        var application = ApplicationBuilder.In(_remote).Screened(TestData.PossibleMatch()).Build();

        application.RecordComplianceReview(new ComplianceReviewResolution(ComplianceReviewDecision.Cleared, "officer-7", TestData.Now), _remote, TestData.Now);

        application.Status.ShouldBe(ApplicationStatus.AccountOpeningRequested);
        application.ReviewedBy.ShouldBe("officer-7");
        application.History[^1].Actor.ShouldBe("ComplianceOfficer:officer-7");
        application.DomainEvents.OfType<AccountOpeningRequestedEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void RecordComplianceReview_Confirmed_RejectsAsNotEligible()
    {
        var application = ApplicationBuilder.In(_remote).Screened(TestData.PossibleMatch()).Build();

        application.RecordComplianceReview(new ComplianceReviewResolution(ComplianceReviewDecision.Confirmed, "officer-7", TestData.Now), _remote, TestData.Now);

        application.Status.ShouldBe(ApplicationStatus.Rejected);
        application.RejectionReason.ShouldBe(RejectionReason.NotEligible);
        application.DomainEvents.OfType<AccountOpeningRequestedEvent>().ShouldBeEmpty();
    }
}
