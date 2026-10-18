using Atlas.Onboarding.Domain.Applicants;
using Atlas.Onboarding.Domain.Verification;

namespace Atlas.Onboarding.Api.Providers.IdNow;

/// <summary>Wire contract of <c>POST /v1/identifications</c>, exactly as documented in ATLAS-1. Lives only here.</summary>
public sealed record IdNowIdentificationRequest(string DocumentType, string DocumentImage, string SelfieImage);

public sealed record IdNowIdentificationResponse(string? IdentificationId, string? DocumentResult, bool? FaceMatch, decimal? Confidence);

/// <summary>Adapter for IDNow (document authenticity + face match).</summary>
public sealed class IdNowClient(HttpClient http) : IIdentityVerificationProvider
{
    private const string Provider = "IDNow";

    public Task<IdentityCheckReport> VerifyAsync(IdentityVerificationRequest request, CancellationToken cancellationToken)
    {
        var body = new IdNowIdentificationRequest(
            IdNowMapper.ToDocumentType(request.DocumentType),
            Convert.ToBase64String(request.DocumentImage),
            Convert.ToBase64String(request.SelfieImage));

        return ProviderHttp.SendAsync<IdNowIdentificationResponse, IdentityCheckReport>(
            Provider,
            ct => ProviderHttp.PostJsonAsync(http, "v1/identifications", body, ct),
            IdNowMapper.ToReport,
            cancellationToken);
    }
}

/// <summary>
/// Anti-corruption layer: IDNow vocabulary in, domain vocabulary out. An unknown document result is treated as
/// inconclusive, never as valid.
/// </summary>
public static class IdNowMapper
{
    public static string ToDocumentType(DocumentType type) => type switch
    {
        DocumentType.Passport => "PASSPORT",
        DocumentType.IdCard => "ID_CARD",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Only identity documents are sent to IDNow."),
    };

    public static IdentityCheckReport ToReport(IdNowIdentificationResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.IdentificationId) || response.FaceMatch is null || response.Confidence is null)
        {
            throw new ProviderContractException("IDNow response is missing identificationId, faceMatch or confidence.");
        }

        var document = response.DocumentResult switch
        {
            "VALID" => DocumentAuthenticity.Valid,
            "INVALID" => DocumentAuthenticity.Invalid,
            _ => DocumentAuthenticity.Inconclusive, // "INCONCLUSIVE", or a value added by the provider later.
        };

        return new IdentityCheckReport(response.IdentificationId, document, response.FaceMatch.Value, response.Confidence.Value);
    }
}
