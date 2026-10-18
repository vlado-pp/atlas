using System.Globalization;
using Atlas.Onboarding.Domain.Verification;

namespace Atlas.Onboarding.Api.Providers.WorldCheck;

/// <summary>Wire contract of <c>POST /worldcheck/v2/screen</c>, as documented in ATLAS-1.</summary>
public sealed record WorldCheckScreenRequest(string Name, string DateOfBirth, string Nationality);

public sealed record WorldCheckScreenResponse(string? CaseId, string? Status, IReadOnlyList<WorldCheckMatch>? Matches);

public sealed record WorldCheckMatch(string? ListType, decimal? Score, string? Subject);

/// <summary>
/// <c>GET /worldcheck/v2/cases/{caseId}</c>. Not in the trimmed documentation; assumed.
/// Possible matches are reviewed by the market's compliance officer in World-Check's case tooling.
/// </summary>
public sealed record WorldCheckCaseResponse(string? CaseId, string? Status, string? Resolution, string? ResolvedBy, DateTimeOffset? ResolvedAt);

/// <summary>Adapter for Refinitiv World-Check (sanctions and PEP screening).</summary>
public sealed class WorldCheckClient(HttpClient http) : ISanctionsScreeningProvider
{
    private const string Provider = "World-Check";

    public Task<ScreeningResult> ScreenAsync(ScreeningRequest request, CancellationToken cancellationToken)
    {
        var body = new WorldCheckScreenRequest(
            request.FullName,
            request.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            request.Nationality);

        return ProviderHttp.SendAsync<WorldCheckScreenResponse, ScreeningResult>(
            Provider,
            ct => ProviderHttp.PostJsonAsync(http, "worldcheck/v2/screen", body, ct),
            WorldCheckMapper.ToResult,
            cancellationToken);
    }

    public Task<ComplianceReviewResolution?> GetCaseResolutionAsync(string caseId, CancellationToken cancellationToken) =>
        ProviderHttp.SendAsync<WorldCheckCaseResponse, ComplianceReviewResolution?>(
            Provider,
            ct => http.GetAsync(new Uri($"worldcheck/v2/cases/{Uri.EscapeDataString(caseId)}", UriKind.Relative), ct),
            WorldCheckMapper.ToResolution,
            cancellationToken);
}

/// <summary>
/// Anti-corruption layer for World-Check. The safety rule is asymmetric: anything we do not recognise is treated
/// as a possible match (goes to a human), never as clear.
/// </summary>
public static class WorldCheckMapper
{
    public static ScreeningResult ToResult(WorldCheckScreenResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.CaseId))
        {
            throw new ProviderContractException("World-Check response has no caseId.");
        }

        var outcome = response.Status == "CLEAR" ? ScreeningOutcome.Clear : ScreeningOutcome.PossibleMatch;
        return new ScreeningResult(response.CaseId, outcome);
    }

    public static ComplianceReviewResolution? ToResolution(WorldCheckCaseResponse response)
    {
        if (response.Status != "RESOLVED")
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(response.ResolvedBy) || response.ResolvedAt is null)
        {
            // GC-2026-0814 §2/§3: a decision we cannot attribute to an officer is not a decision.
            throw new ProviderContractException("Resolved World-Check case has no resolving officer or time.");
        }

        var decision = response.Resolution switch
        {
            "FALSE_POSITIVE" => ComplianceReviewDecision.Cleared,
            "TRUE_MATCH" => ComplianceReviewDecision.Confirmed,
            var other => throw new ProviderContractException($"Unknown World-Check resolution '{other}'."),
        };

        return new ComplianceReviewResolution(decision, response.ResolvedBy, response.ResolvedAt.Value);
    }
}
