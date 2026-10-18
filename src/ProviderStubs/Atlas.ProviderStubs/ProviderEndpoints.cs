using System.Security.Cryptography;
using System.Text;

namespace Atlas.ProviderStubs;

public sealed record IdentificationRequest(string? DocumentType, string? DocumentImage, string? SelfieImage);

public sealed record ScreenRequest(string? Name, string? DateOfBirth, string? Nationality);

public sealed record ResolveCaseRequest(string? Outcome, string? OfficerId);

/// <summary>The providers' documented wire contracts (ATLAS-1), answering according to the rule book.</summary>
public static class ProviderEndpoints
{
    public static void MapIdNow(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/idnow/v1/identifications", async (IdentificationRequest request, RuleBook rules, ILogger<RuleBook> logger, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(request.DocumentImage) || string.IsNullOrEmpty(request.SelfieImage) || request.DocumentType is not ("PASSPORT" or "ID_CARD"))
            {
                return Results.BadRequest(new { error = "documentType, documentImage and selfieImage are required" });
            }

            var text = Decode(request.DocumentImage) + "\n" + Decode(request.SelfieImage);
            var rule = rules.MatchIdNow(text) ?? new IdNowRule { Id = "default" };
            logger.LogInformation("IDNow stub: rule {RuleId}", rule.Id);

            if (await Delay(rule, ct) is { } cancelled)
            {
                return cancelled;
            }

            if (rules.ShouldFail(rule, Fingerprint(request.DocumentImage)))
            {
                return Results.StatusCode(rule.FailStatusCode);
            }

            return Results.Ok(new
            {
                identificationId = $"idn_{Guid.NewGuid():N}",
                documentResult = rule.DocumentResult,
                faceMatch = rule.FaceMatch,
                confidence = rule.Confidence,
            });
        });

    public static void MapWorldCheck(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/refinitiv/worldcheck/v2/screen", async (ScreenRequest request, RuleBook rules, CaseStore cases, TimeProvider clock, ILogger<RuleBook> logger, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.DateOfBirth))
            {
                return Results.BadRequest(new { error = "name and dateOfBirth are required" });
            }

            var rule = rules.MatchWorldCheck(request.Name) ?? new WorldCheckRule { Id = "default" };
            logger.LogInformation("World-Check stub: rule {RuleId}", rule.Id);

            if (await Delay(rule, ct) is { } cancelled)
            {
                return cancelled;
            }

            if (rules.ShouldFail(rule, request.Name))
            {
                return Results.StatusCode(rule.FailStatusCode);
            }

            var caseId = $"wc_{Guid.NewGuid():N}";
            if (rule.Status != "POSSIBLE_MATCH")
            {
                return Results.Ok(new { caseId, status = rule.Status, matches = Array.Empty<object>() });
            }

            var now = clock.GetUtcNow();
            cases.Add(new StubCase
            {
                CaseId = caseId,
                Name = request.Name,
                CreatedAt = now,
                AutoResolveAt = rule.AutoResolveAfterSeconds is { } seconds ? now.AddSeconds(seconds) : null,
                AutoResolution = rule.AutoResolution,
            });

            return Results.Ok(new
            {
                caseId,
                status = "POSSIBLE_MATCH",
                matches = new[] { new { listType = rule.ListType, score = rule.Score, subject = request.Name } },
            });
        });

        // Assumed endpoint for reading a case's review outcome.
        endpoints.MapGet("/refinitiv/worldcheck/v2/cases/{caseId}", (string caseId, CaseStore cases) =>
            cases.Get(caseId) is { } c
                ? Results.Ok(new { caseId = c.CaseId, status = c.Status, resolution = c.Resolution, resolvedBy = c.ResolvedBy, resolvedAt = c.ResolvedAt })
                : Results.NotFound());
    }

    /// <summary>Lets a reviewer steer the stand-ins at runtime, without restarting anything or editing code.</summary>
    public static void MapControl(this IEndpointRouteBuilder endpoints)
    {
        var control = endpoints.MapGroup("/_control");

        control.MapGet("/rules", (RuleBook rules) => Results.Ok(rules.Describe()));
        control.MapPost("/rules/idnow", (IdNowRule rule, RuleBook rules) =>
        {
            rules.Add(rule);
            return Results.Ok(rule);
        });
        control.MapPost("/rules/worldcheck", (WorldCheckRule rule, RuleBook rules) =>
        {
            rules.Add(rule);
            return Results.Ok(rule);
        });
        control.MapDelete("/rules", (RuleBook rules) =>
        {
            rules.ResetRuntime();
            return Results.NoContent();
        });

        control.MapGet("/cases", (CaseStore cases) => Results.Ok(cases.All));
        control.MapPost("/cases/{caseId}/resolve", (string caseId, ResolveCaseRequest request, CaseStore cases) =>
        {
            var resolution = request.Outcome switch
            {
                "CLEARED" => "FALSE_POSITIVE",
                "CONFIRMED" => "TRUE_MATCH",
                _ => null,
            };

            if (resolution is null || string.IsNullOrWhiteSpace(request.OfficerId))
            {
                return Results.BadRequest(new { error = "outcome must be CLEARED or CONFIRMED, and officerId is required" });
            }

            return cases.TryResolve(caseId, resolution, request.OfficerId)
                ? Results.Ok(cases.Get(caseId))
                : Results.NotFound(new { error = "No open case with that id" });
        });
    }

    private static async Task<IResult?> Delay(StubRule rule, CancellationToken ct)
    {
        if (rule.LatencyMs <= 0)
        {
            return null;
        }

        try
        {
            await Task.Delay(rule.LatencyMs, ct);
            return null;
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    private static string Decode(string base64)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
}
