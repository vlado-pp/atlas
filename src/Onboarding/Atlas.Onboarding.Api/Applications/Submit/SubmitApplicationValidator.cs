using System.Globalization;
using Atlas.Onboarding.Api.Documents;
using Atlas.Onboarding.Api.Markets;
using Atlas.Onboarding.Domain.Applicants;
using Atlas.Onboarding.Domain.Markets;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Api.Applications.Submit;

public sealed record DecodedDocument(DocumentType Type, byte[] Content);

/// <summary>A request that passed validation, in domain types.</summary>
public sealed record ValidSubmission(
    string Market,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    CustomerIdentifier Identifier,
    string Nationality,
    string Email,
    string Phone,
    IReadOnlyList<DecodedDocument> Documents);

public sealed class SubmissionValidation
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public ValidSubmission? Submission { get; internal set; }

    public bool IsValid => _errors.Count == 0 && Submission is not null;

    public IDictionary<string, string[]> Errors => _errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal);

    internal void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            _errors[field] = messages = [];
        }

        messages.Add(message);
    }
}

/// <summary>
/// Validates a submission at the edge. Format rules are here; market rules are asked of the market policy, so the
/// same rules apply wherever the policy is used.
/// </summary>
public sealed class SubmitApplicationValidator(MarketPolicies policies, TimeProvider clock, IOptions<DocumentStorageOptions> documentOptions)
{
    private const int MaxNameLength = 100;

    public SubmissionValidation Validate(SubmitApplicationRequest request)
    {
        var result = new SubmissionValidation();

        if (!policies.TryGet(request.Country, out var policy))
        {
            result.Add("country", "Unknown or missing market code.");
            return result;
        }

        var firstName = Required(result, "firstName", request.FirstName, MaxNameLength);
        var lastName = Required(result, "lastName", request.LastName, MaxNameLength);
        var email = Required(result, "email", request.Email, 254);
        var phone = Required(result, "phone", request.Phone, 30);
        if (email is not null && !email.Contains('@', StringComparison.Ordinal))
        {
            result.Add("email", "Must be an email address.");
        }

        var dateOfBirth = ValidateDateOfBirth(result, request.DateOfBirth, policy);

        var identifier = policy.ResolveIdentifier(request.NationalId, request.PassportNumber);
        if (!identifier.IsSuccess)
        {
            result.Add(identifier.Error!.Field ?? "nationalId", identifier.Error.Message);
        }

        var nationality = request.Nationality?.Trim().ToUpperInvariant();
        if (nationality is not null && (nationality.Length is < 2 or > 3 || !nationality.All(char.IsAsciiLetterUpper)))
        {
            result.Add("nationality", "Must be a 2 or 3 letter country code.");
        }

        var usesPassportInLieu = identifier.IsSuccess && !string.IsNullOrWhiteSpace(request.PassportNumber);
        if (usesPassportInLieu && nationality is null)
        {
            // Q8: the market is not a safe default for someone without a national identifier (e.g. refugees in MF).
            result.Add("nationality", "Required when onboarding with a passport number.");
        }

        var documents = ValidateDocuments(result, request.Documents, usesPassportInLieu);

        if (!request.TermsAccepted)
        {
            result.Add("termsAccepted", "Terms must be accepted.");
        }

        if (result.Errors.Count == 0)
        {
            result.Submission = new ValidSubmission(
                policy.Market,
                firstName!,
                lastName!,
                dateOfBirth!.Value,
                identifier.Value,
                nationality ?? policy.DefaultNationality,
                email!,
                phone!,
                documents);
        }

        return result;
    }

    private static string? Required(SubmissionValidation result, string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result.Add(field, "Required.");
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            result.Add(field, $"Must be at most {maxLength} characters.");
            return null;
        }

        return trimmed;
    }

    private DateOnly? ValidateDateOfBirth(SubmissionValidation result, string? value, MarketPolicy policy)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOfBirth))
        {
            result.Add("dateOfBirth", "Required, in the format yyyy-MM-dd.");
            return null;
        }

        if (!policy.IsOldEnough(dateOfBirth, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)))
        {
            result.Add("dateOfBirth", $"Applicant must be at least {policy.MinimumAge} years old.");
            return null;
        }

        return dateOfBirth;
    }

    private List<DecodedDocument> ValidateDocuments(SubmissionValidation result, IReadOnlyList<DocumentUpload>? uploads, bool usesPassportInLieu)
    {
        var documents = new List<DecodedDocument>();
        if (uploads is null || uploads.Count == 0)
        {
            result.Add("documents", "An identity document (PASSPORT or ID_CARD) and a SELFIE are required.");
            return documents;
        }

        for (var i = 0; i < uploads.Count; i++)
        {
            var field = $"documents[{i}]";
            var type = uploads[i].Type switch
            {
                "PASSPORT" => DocumentType.Passport,
                "ID_CARD" => DocumentType.IdCard,
                "SELFIE" => DocumentType.Selfie,
                _ => (DocumentType?)null,
            };

            if (type is null)
            {
                result.Add($"{field}.type", "Must be PASSPORT, ID_CARD or SELFIE.");
                continue;
            }

            if (Decode(result, $"{field}.image", uploads[i].Image) is { } content)
            {
                documents.Add(new DecodedDocument(type.Value, content));
            }
        }

        var identityDocuments = documents.Count(d => d.Type != DocumentType.Selfie);
        var selfies = documents.Count(d => d.Type == DocumentType.Selfie);
        if (identityDocuments != 1 || selfies != 1)
        {
            result.Add("documents", "Exactly one identity document (PASSPORT or ID_CARD) and one SELFIE are required.");
        }
        else if (usesPassportInLieu && documents.All(d => d.Type != DocumentType.Passport))
        {
            result.Add("documents", "A PASSPORT image is required when onboarding with a passport number.");
        }

        return documents;
    }

    private byte[]? Decode(SubmissionValidation result, string field, string? base64)
    {
        var maxBytes = documentOptions.Value.MaxDocumentBytes;
        if (string.IsNullOrWhiteSpace(base64))
        {
            result.Add(field, "Required (base64).");
            return null;
        }

        if ((long)base64.Length * 3 / 4 > maxBytes)
        {
            result.Add(field, $"Must be at most {maxBytes / (1024 * 1024)} MB.");
            return null;
        }

        try
        {
            var content = Convert.FromBase64String(base64);
            if (content.Length == 0)
            {
                result.Add(field, "Must not be empty.");
                return null;
            }

            return content;
        }
        catch (FormatException)
        {
            result.Add(field, "Must be valid base64.");
            return null;
        }
    }
}
