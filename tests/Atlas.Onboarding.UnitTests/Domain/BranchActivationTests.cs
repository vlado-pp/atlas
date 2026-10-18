using Atlas.Onboarding.Domain.Applications;

namespace Atlas.Onboarding.UnitTests.Domain;

/// <summary>Annex B (MD): in-person attendance and a wet signature before the account may be activated (ADR-0007).</summary>
public sealed class BranchActivationTests
{
    private readonly Atlas.Onboarding.Domain.Markets.MarketPolicy _branch = TestData.BranchMarket();

    [Fact]
    public void ChecksPassed_InPersonBranchMarket_AwaitsBranchVisitInsteadOfOpeningAnAccount()
    {
        var application = ApplicationBuilder.In(_branch).IdentityVerified().Build();

        application.RecordScreening(TestData.Clear(), _branch, TestData.Now);

        application.Status.ShouldBe(ApplicationStatus.AwaitingBranchVisit);
        application.CustomerFacingStatus.ShouldBe(CustomerFacingStatus.AwaitingBranchVisit);
        application.BranchVisitDeadline.ShouldBe(TestData.Now.AddDays(30));
        application.NextAttemptAt.ShouldBe(application.BranchVisitDeadline);
        application.DomainEvents.OfType<AccountOpeningRequestedEvent>().ShouldBeEmpty();
    }

    [Fact]
    public void RecordBranchActivation_BeforeTheDeadline_RequestsAccountOpeningAttributedToStaff()
    {
        var application = ApplicationBuilder.In(_branch).Screened(TestData.Clear()).Build();

        var result = application.RecordBranchActivation(new BranchActivation("staff-12", "MD-001", "form-889", TestData.Now.AddDays(3)));

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.AccountOpeningRequested);
        application.BranchActivation!.StaffId.ShouldBe("staff-12");
        application.History[^1].Actor.ShouldBe("Staff:staff-12");
        application.DomainEvents.OfType<AccountOpeningRequestedEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void RecordBranchActivation_AfterTheDeadline_IsRefused()
    {
        var application = ApplicationBuilder.In(_branch).Screened(TestData.Clear()).Build();

        var result = application.RecordBranchActivation(new BranchActivation("staff-12", "MD-001", "form-889", TestData.Now.AddDays(31)));

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("application.branch_visit_expired");
        application.Status.ShouldBe(ApplicationStatus.AwaitingBranchVisit);
    }

    [Fact]
    public void RecordBranchActivation_InARemoteMarketApplication_IsAnInvalidTransition()
    {
        var remote = TestData.RemoteMarket();
        var application = ApplicationBuilder.In(remote).Screened(TestData.Clear()).Build();

        var result = application.RecordBranchActivation(new BranchActivation("staff-12", "MB-001", "form-1", TestData.Now));

        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void ExpireBranchVisit_AtTheDeadline_ExpiresTheApplication()
    {
        var application = ApplicationBuilder.In(_branch).Screened(TestData.Clear()).Build();

        application.ExpireBranchVisit(TestData.Now.AddDays(30));

        application.Status.ShouldBe(ApplicationStatus.Expired);
        application.RejectionReason.ShouldBe(RejectionReason.BranchVisitExpired);
        application.CustomerFacingStatus.ShouldBe(CustomerFacingStatus.Rejected);
        application.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public void ExpireBranchVisit_BeforeTheDeadline_KeepsWaiting()
    {
        var application = ApplicationBuilder.In(_branch).Screened(TestData.Clear()).Build();

        application.ExpireBranchVisit(TestData.Now.AddDays(29));

        application.Status.ShouldBe(ApplicationStatus.AwaitingBranchVisit);
        application.NextAttemptAt.ShouldBe(TestData.Now.AddDays(30));
    }
}
