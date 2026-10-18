using Atlas.Onboarding.Domain.Markets;
using Atlas.Onboarding.Domain.Verification;

namespace Atlas.Onboarding.UnitTests.Markets;

/// <summary>Annex B identifier rules, tested against the configuration production uses.</summary>
public sealed class MarketPolicyTests
{
    private readonly Atlas.Onboarding.Api.Markets.MarketPolicies _markets = TestData.ConfiguredMarkets();

    [Theory]
    [InlineData("MA", "0403991450016", "PersonalNumber")]
    [InlineData("MB", "0403991450016", "PersonalNumber")]
    [InlineData("MC", "12345678A", "UnifiedCitizenId")]
    [InlineData("MC", "AB34567CZ", "UnifiedCitizenId")]
    [InlineData("MD", "1234567890", "CitizenNumber")]
    [InlineData("ME", "1234567890", "CivilNumber")]
    [InlineData("MF", "0403991450016", "PersonalNumber")]
    public void ResolveIdentifier_ValidNationalIdentifier_IsAccepted(string market, string nationalId, string expectedScheme)
    {
        var result = _markets.Get(market).ResolveIdentifier(nationalId, passportNumber: null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Scheme.ShouldBe(expectedScheme);
        result.Value.Value.ShouldBe(nationalId);
    }

    [Theory]
    [InlineData("MA", "040399145001")]
    [InlineData("MB", "04039914500161")]
    [InlineData("MB", "04039914500AB")]
    [InlineData("MC", "123456789")]
    [InlineData("MC", "1234567A1")]
    [InlineData("MC", "12345678a")]
    [InlineData("MD", "0403991450016")]
    [InlineData("ME", "123456789")]
    public void ResolveIdentifier_WrongFormatForMarket_IsRejectedOnNationalId(string market, string nationalId)
    {
        var result = _markets.Get(market).ResolveIdentifier(nationalId, passportNumber: null);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Field.ShouldBe("nationalId");
        result.Error.Code.ShouldBe("identifier.format");
    }

    [Fact]
    public void ResolveIdentifier_PassportInLieuInMF_IsAccepted()
    {
        var result = _markets.Get("MF").ResolveIdentifier(nationalId: null, passportNumber: "AB1234567");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Scheme.ShouldBe("Passport");
    }

    [Theory]
    [InlineData("MA")]
    [InlineData("MB")]
    [InlineData("MC")]
    [InlineData("MD")]
    [InlineData("ME")]
    public void ResolveIdentifier_PassportInLieuOutsideMF_IsRejected(string market)
    {
        var result = _markets.Get(market).ResolveIdentifier(nationalId: null, passportNumber: "AB1234567");

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("identifier.passport_not_accepted");
    }

    [Fact]
    public void ResolveIdentifier_NeitherIdentifier_IsRequired()
    {
        var result = _markets.Get("MB").ResolveIdentifier(null, null);

        result.Error!.Code.ShouldBe("identifier.required");
    }

    [Fact]
    public void ResolveIdentifier_BothIdentifiers_IsAmbiguous()
    {
        var result = _markets.Get("MF").ResolveIdentifier("0403991450016", "AB1234567");

        result.Error!.Code.ShouldBe("identifier.ambiguous");
    }

    [Fact]
    public void ConfiguredMarkets_OnlyMDRequiresInPersonActivation()
    {
        string[] markets = ["MA", "MB", "MC", "MD", "ME", "MF"];

        markets.Where(m => _markets.Get(m).Activation.Mode == ActivationMode.InPersonBranch).ShouldBe(["MD"]);
    }

    [Theory]
    [InlineData(DocumentAuthenticity.Valid, true, 0.90, IdentityVerificationOutcome.Verified)]
    [InlineData(DocumentAuthenticity.Valid, true, 0.8999, IdentityVerificationOutcome.Inconclusive)]
    [InlineData(DocumentAuthenticity.Valid, false, 0.99, IdentityVerificationOutcome.Failed)]
    [InlineData(DocumentAuthenticity.Invalid, true, 0.99, IdentityVerificationOutcome.Failed)]
    [InlineData(DocumentAuthenticity.Inconclusive, true, 0.99, IdentityVerificationOutcome.Inconclusive)]
    [InlineData(DocumentAuthenticity.Inconclusive, false, 0.99, IdentityVerificationOutcome.Failed)]
    public void AssessIdentity_OnlyAClearPassIsVerified(DocumentAuthenticity document, bool faceMatch, double confidence, IdentityVerificationOutcome expected)
    {
        var outcome = _markets.Get("MB").AssessIdentity(new IdentityCheckReport("idn_1", document, faceMatch, (decimal)confidence));

        outcome.ShouldBe(expected);
    }

    [Theory]
    [InlineData("2008-10-01", true)]
    [InlineData("2008-10-02", false)]
    [InlineData("1950-01-01", true)]
    public void IsOldEnough_EighteenthBirthday_IsTheBoundary(string dateOfBirth, bool expected)
    {
        var today = new DateOnly(2026, 10, 1);

        _markets.Get("MB").IsOldEnough(DateOnly.Parse(dateOfBirth, System.Globalization.CultureInfo.InvariantCulture), today).ShouldBe(expected);
    }
}
