using Atlas.Onboarding.Api.Providers;
using Atlas.Onboarding.Api.Providers.IdNow;
using Atlas.Onboarding.Api.Providers.WorldCheck;
using Atlas.Onboarding.Domain.Verification;

namespace Atlas.Onboarding.UnitTests.Providers;

/// <summary>The anti-corruption layer: provider vocabulary in, domain vocabulary out, unknowns never treated as a pass.</summary>
public sealed class ProviderMapperTests
{
    [Theory]
    [InlineData("VALID", DocumentAuthenticity.Valid)]
    [InlineData("INVALID", DocumentAuthenticity.Invalid)]
    [InlineData("INCONCLUSIVE", DocumentAuthenticity.Inconclusive)]
    [InlineData("SOMETHING_NEW", DocumentAuthenticity.Inconclusive)]
    [InlineData(null, DocumentAuthenticity.Inconclusive)]
    public void IdNow_DocumentResult_MapsToDomain(string? documentResult, DocumentAuthenticity expected)
    {
        var report = IdNowMapper.ToReport(new IdNowIdentificationResponse("idn_1", documentResult, true, 0.97m));

        report.Document.ShouldBe(expected);
        report.ProviderReference.ShouldBe("idn_1");
    }

    [Theory]
    [InlineData(null, true, 0.9)]
    [InlineData("idn_1", null, 0.9)]
    [InlineData("idn_1", true, null)]
    public void IdNow_MissingContractFields_IsAContractViolation(string? id, bool? faceMatch, double? confidence)
    {
        Should.Throw<ProviderContractException>(() =>
            IdNowMapper.ToReport(new IdNowIdentificationResponse(id, "VALID", faceMatch, (decimal?)confidence)));
    }

    [Theory]
    [InlineData("CLEAR", ScreeningOutcome.Clear)]
    [InlineData("POSSIBLE_MATCH", ScreeningOutcome.PossibleMatch)]
    [InlineData("SOMETHING_NEW", ScreeningOutcome.PossibleMatch)]
    [InlineData(null, ScreeningOutcome.PossibleMatch)]
    public void WorldCheck_UnknownStatus_GoesToAHumanNeverClear(string? status, ScreeningOutcome expected)
    {
        var result = WorldCheckMapper.ToResult(new WorldCheckScreenResponse("wc_1", status, []));

        result.Outcome.ShouldBe(expected);
    }

    [Fact]
    public void WorldCheck_OpenCase_HasNoResolution()
    {
        WorldCheckMapper.ToResolution(new WorldCheckCaseResponse("wc_1", "OPEN", null, null, null)).ShouldBeNull();
    }

    [Theory]
    [InlineData("FALSE_POSITIVE", ComplianceReviewDecision.Cleared)]
    [InlineData("TRUE_MATCH", ComplianceReviewDecision.Confirmed)]
    public void WorldCheck_ResolvedCase_MapsTheOfficersDecision(string resolution, ComplianceReviewDecision expected)
    {
        var resolvedAt = TestData.Now;

        var result = WorldCheckMapper.ToResolution(new WorldCheckCaseResponse("wc_1", "RESOLVED", resolution, "officer-7", resolvedAt));

        result.ShouldNotBeNull();
        result.Decision.ShouldBe(expected);
        result.OfficerId.ShouldBe("officer-7");
    }

    [Fact]
    public void WorldCheck_ResolvedCaseWithoutOfficer_IsNotADecision()
    {
        Should.Throw<ProviderContractException>(() =>
            WorldCheckMapper.ToResolution(new WorldCheckCaseResponse("wc_1", "RESOLVED", "FALSE_POSITIVE", null, TestData.Now)));
    }

    [Fact]
    public void WorldCheck_UnknownResolution_IsNotADecision()
    {
        Should.Throw<ProviderContractException>(() =>
            WorldCheckMapper.ToResolution(new WorldCheckCaseResponse("wc_1", "RESOLVED", "MAYBE", "officer-7", TestData.Now)));
    }
}
