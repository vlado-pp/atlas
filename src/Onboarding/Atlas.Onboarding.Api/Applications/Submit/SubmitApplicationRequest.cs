namespace Atlas.Onboarding.Api.Applications.Submit;

/// <summary>
/// The ATLAS-1 request body, unchanged, plus two optional fields (ADR-0008): <c>passportNumber</c> for residents
/// without a national identifier (MF) and <c>nationality</c> for screening. Everything is a nullable string so
/// that malformed input is reported per field instead of failing deserialization.
/// </summary>
public sealed record SubmitApplicationRequest(
    string? FirstName,
    string? LastName,
    string? DateOfBirth,
    string? Country,
    string? NationalId,
    string? PassportNumber,
    string? Nationality,
    string? Email,
    string? Phone,
    IReadOnlyList<DocumentUpload>? Documents,
    bool TermsAccepted);

public sealed record DocumentUpload(string? Type, string? Image);
