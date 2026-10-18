using System.Text;
using Atlas.Onboarding.Api.Applications.Submit;
using Atlas.Onboarding.Api.Documents;
using Atlas.Onboarding.Domain.Applicants;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Atlas.Onboarding.UnitTests.Applications;

public sealed class SubmitApplicationValidatorTests
{
    private static readonly string Image = Convert.ToBase64String(Encoding.UTF8.GetBytes("synthetic-image"));

    private readonly SubmitApplicationValidator _validator = new(
        TestData.ConfiguredMarkets(),
        new FakeTimeProvider(TestData.Now),
        Options.Create(new DocumentStorageOptions { MaxDocumentBytes = 1024 }));

    [Fact]
    public void Validate_TicketShapedRequest_IsValid()
    {
        var result = _validator.Validate(Request());

        result.IsValid.ShouldBeTrue();
        result.Submission!.Market.ShouldBe("MB");
        result.Submission.Identifier.ShouldBe(new CustomerIdentifier("PersonalNumber", "0403991450016"));
        result.Submission.Nationality.ShouldBe("MB");
        result.Submission.Documents.Select(d => d.Type).ShouldBe([DocumentType.Passport, DocumentType.Selfie]);
    }

    [Fact]
    public void Validate_UnknownMarket_ReportsCountry()
    {
        var result = _validator.Validate(Request() with { Country = "XX" });

        result.Errors.Keys.ShouldBe(["country"]);
    }

    [Fact]
    public void Validate_PassportInLieuInMF_RequiresNationality()
    {
        var result = _validator.Validate(Request() with { Country = "MF", NationalId = null, PassportNumber = "AB1234567" });

        result.Errors.ShouldContainKey("nationality");
    }

    [Fact]
    public void Validate_PassportInLieuWithNationality_UsesTheGivenNationality()
    {
        var result = _validator.Validate(Request() with { Country = "MF", NationalId = null, PassportNumber = "AB1234567", Nationality = "sy" });

        result.IsValid.ShouldBeTrue();
        result.Submission!.Nationality.ShouldBe("SY");
        result.Submission.Identifier.Scheme.ShouldBe("Passport");
    }

    [Fact]
    public void Validate_PassportInLieuWithIdCardImage_RequiresAPassportImage()
    {
        var result = _validator.Validate(Request(identityDocumentType: "ID_CARD") with { Country = "MF", NationalId = null, PassportNumber = "AB1234567", Nationality = "SY" });

        result.Errors.ShouldContainKey("documents");
    }

    [Fact]
    public void Validate_MissingSelfie_IsRejected()
    {
        var result = _validator.Validate(Request() with { Documents = [new DocumentUpload("PASSPORT", Image)] });

        result.Errors.ShouldContainKey("documents");
    }

    [Theory]
    [InlineData("not base64 !!")]
    [InlineData("")]
    public void Validate_BadImage_IsReportedOnTheDocument(string image)
    {
        var result = _validator.Validate(Request() with { Documents = [new DocumentUpload("PASSPORT", image), new DocumentUpload("SELFIE", Image)] });

        result.Errors.ShouldContainKey("documents[0].image");
    }

    [Fact]
    public void Validate_ImageOverTheLimit_IsRejected()
    {
        var tooBig = Convert.ToBase64String(new byte[2048]);

        var result = _validator.Validate(Request() with { Documents = [new DocumentUpload("PASSPORT", tooBig), new DocumentUpload("SELFIE", Image)] });

        result.Errors.ShouldContainKey("documents[0].image");
    }

    [Fact]
    public void Validate_UnknownDocumentType_IsRejected()
    {
        var result = _validator.Validate(Request() with { Documents = [new DocumentUpload("DRIVING_LICENCE", Image), new DocumentUpload("SELFIE", Image)] });

        result.Errors.ShouldContainKey("documents[0].type");
    }

    [Fact]
    public void Validate_TermsNotAccepted_IsRejected()
    {
        var result = _validator.Validate(Request() with { TermsAccepted = false });

        result.Errors.Keys.ShouldBe(["termsAccepted"]);
    }

    [Fact]
    public void Validate_Underage_IsRejected()
    {
        var result = _validator.Validate(Request() with { DateOfBirth = "2010-01-01" });

        result.Errors.ShouldContainKey("dateOfBirth");
    }

    [Fact]
    public void Validate_WrongIdentifierForMarket_IsReportedOnNationalId()
    {
        var result = _validator.Validate(Request() with { Country = "MD" });

        result.Errors.ShouldContainKey("nationalId");
    }

    private static SubmitApplicationRequest Request(string identityDocumentType = "PASSPORT") =>
        new(
            FirstName: "Test",
            LastName: "Applicant",
            DateOfBirth: "1991-03-04",
            Country: "MB",
            NationalId: "0403991450016",
            PassportNumber: null,
            Nationality: null,
            Email: "applicant@example.test",
            Phone: "+000 000 000",
            Documents: [new DocumentUpload(identityDocumentType, Image), new DocumentUpload("SELFIE", Image)],
            TermsAccepted: true);
}
