using Atlas.Onboarding.Api.Applications;
using Atlas.Onboarding.Domain.Applications;

namespace Atlas.Onboarding.UnitTests.Applications;

public sealed class PublicStatusTests
{
    [Fact]
    public void UnderReview_DoesNotRevealWhy()
    {
        PublicStatus.Of(CustomerFacingStatus.UnderReview).ShouldBe("UNDER_REVIEW");
        PublicStatus.ReasonOf(CustomerFacingStatus.UnderReview, null).ShouldBeNull();
    }

    [Theory]
    [InlineData(RejectionReason.IdentityNotVerified, "IDENTITY_NOT_VERIFIED")]
    [InlineData(RejectionReason.RecaptureRequired, "RECAPTURE_REQUIRED")]
    [InlineData(RejectionReason.NotEligible, "NOT_ELIGIBLE")]
    [InlineData(RejectionReason.BranchVisitExpired, "EXPIRED")]
    public void Rejected_CarriesOnlyACustomerSafeReason(RejectionReason reason, string expected)
    {
        PublicStatus.ReasonOf(CustomerFacingStatus.Rejected, reason).ShouldBe(expected);
    }
}
