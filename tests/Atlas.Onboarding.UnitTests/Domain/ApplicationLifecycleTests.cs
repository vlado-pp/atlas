using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Domain.Verification;

namespace Atlas.Onboarding.UnitTests.Domain;

public sealed class ApplicationLifecycleTests
{
    private readonly Atlas.Onboarding.Domain.Markets.MarketPolicy _remote = TestData.RemoteMarket();

    [Fact]
    public void Submit_NewApplication_IsDueImmediatelyAndRetainedForTenYears()
    {
        var application = ApplicationBuilder.In(_remote).Build();

        application.Status.ShouldBe(ApplicationStatus.Submitted);
        application.NextAttemptAt.ShouldBe(TestData.Now);
        application.RetainUntil.ShouldBe(TestData.Now.AddYears(10));
        application.CustomerFacingStatus.ShouldBe(CustomerFacingStatus.InProgress);
        application.Id.Market.ShouldBe("MB");
    }

    [Fact]
    public void RecordIdentityVerification_Verified_MovesToIdentityVerifiedAndIsDueForScreening()
    {
        var application = ApplicationBuilder.In(_remote).Build();

        var result = application.RecordIdentityVerification(TestData.VerifiedReport(), _remote, TestData.Now);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.IdentityVerified);
        application.IdentityVerificationReference.ShouldBe("idn_1");
        application.NextAttemptAt.ShouldBe(TestData.Now);
    }

    [Theory]
    [InlineData(DocumentAuthenticity.Invalid, true, 0.99, RejectionReason.IdentityNotVerified)]
    [InlineData(DocumentAuthenticity.Valid, false, 0.99, RejectionReason.IdentityNotVerified)]
    [InlineData(DocumentAuthenticity.Inconclusive, true, 0.99, RejectionReason.RecaptureRequired)]
    [InlineData(DocumentAuthenticity.Valid, true, 0.89, RejectionReason.RecaptureRequired)]
    public void RecordIdentityVerification_NotAClearPass_RejectsWithReason(DocumentAuthenticity document, bool faceMatch, double confidence, RejectionReason expected)
    {
        var application = ApplicationBuilder.In(_remote).Build();

        application.RecordIdentityVerification(new IdentityCheckReport("idn_1", document, faceMatch, (decimal)confidence), _remote, TestData.Now);

        application.Status.ShouldBe(ApplicationStatus.Rejected);
        application.RejectionReason.ShouldBe(expected);
        application.NextAttemptAt.ShouldBeNull();
        application.DomainEvents.OfType<AccountOpeningRequestedEvent>().ShouldBeEmpty();
    }

    [Fact]
    public void RecordScreening_BeforeIdentityIsVerified_IsRejectedAsInvalidTransition()
    {
        var application = ApplicationBuilder.In(_remote).Build();

        var result = application.RecordScreening(TestData.Clear(), _remote, TestData.Now);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("application.invalid_transition");
        application.Status.ShouldBe(ApplicationStatus.Submitted);
    }

    [Fact]
    public void RecordScreening_ClearInRemoteMarket_RequestsAccountOpening()
    {
        var application = ApplicationBuilder.In(_remote).IdentityVerified().Build();

        application.RecordScreening(TestData.Clear(), _remote, TestData.Now);

        application.Status.ShouldBe(ApplicationStatus.AccountOpeningRequested);
        application.NextAttemptAt.ShouldBeNull();
        application.DomainEvents.OfType<AccountOpeningRequestedEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void AccountProvisioned_AfterAccountOpeningRequested_Approves()
    {
        var application = ApplicationBuilder.In(_remote).Screened(TestData.Clear()).Build();
        var accountId = Guid.NewGuid();

        var result = application.RecordAccountProvisioned(accountId, "card_1", TestData.Now);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Approved);
        application.AccountId.ShouldBe(accountId);
        application.CardOrderReference.ShouldBe("card_1");
        application.DomainEvents.OfType<CustomerFacingStatusChangedEvent>().ShouldHaveSingleItem().Status.ShouldBe(CustomerFacingStatus.Approved);
    }

    [Fact]
    public void AccountProvisioned_RedeliveredForSameAccount_IsANoOp()
    {
        var application = ApplicationBuilder.In(_remote).Screened(TestData.Clear()).Build();
        var accountId = Guid.NewGuid();
        application.RecordAccountProvisioned(accountId, "card_1", TestData.Now);
        application.ClearDomainEvents();

        var result = application.RecordAccountProvisioned(accountId, "card_1", TestData.Now.AddMinutes(1));

        result.IsSuccess.ShouldBeTrue();
        application.DomainEvents.ShouldBeEmpty();
        application.History.Count(h => h.To == ApplicationStatus.Approved).ShouldBe(1);
    }

    [Fact]
    public void AccountProvisioned_BeforeChecksPassed_IsRejectedAsInvalidTransition()
    {
        var application = ApplicationBuilder.In(_remote).IdentityVerified().Build();

        var result = application.RecordAccountProvisioned(Guid.NewGuid(), "card_1", TestData.Now);

        result.IsSuccess.ShouldBeFalse();
        application.Status.ShouldBe(ApplicationStatus.IdentityVerified);
    }

    [Fact]
    public void ScheduleRetry_CountsAttempts_AndATransitionResetsThem()
    {
        var application = ApplicationBuilder.In(_remote).Build();

        application.ScheduleRetry(TestData.Now.AddSeconds(5));
        application.ScheduleRetry(TestData.Now.AddSeconds(10));

        application.AttemptCount.ShouldBe(2);
        application.NextAttemptAt.ShouldBe(TestData.Now.AddSeconds(10));
        application.Status.ShouldBe(ApplicationStatus.Submitted);

        application.RecordIdentityVerification(TestData.VerifiedReport(), _remote, TestData.Now);

        application.AttemptCount.ShouldBe(0);
    }

    [Fact]
    public void StatusChanges_PublishOnlyWhenTheCustomerFacingStatusChanges()
    {
        var application = ApplicationBuilder.In(_remote).Build();

        application.RecordIdentityVerification(TestData.VerifiedReport(), _remote, TestData.Now);
        application.RecordScreening(TestData.Clear(), _remote, TestData.Now);

        // Submitted → IdentityVerified → AccountOpeningRequested are all IN_PROGRESS for the customer.
        application.DomainEvents.OfType<CustomerFacingStatusChangedEvent>().ShouldBeEmpty();
        application.History.Select(h => h.To).ShouldBe([ApplicationStatus.Submitted, ApplicationStatus.IdentityVerified, ApplicationStatus.AccountOpeningRequested]);
    }
}
