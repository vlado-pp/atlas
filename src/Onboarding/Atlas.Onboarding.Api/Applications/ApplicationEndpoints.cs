using System.Security.Claims;
using Atlas.BuildingBlocks.Markets;
using Atlas.BuildingBlocks.Security;
using Atlas.Onboarding.Api.Applications.Submit;
using Atlas.Onboarding.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.Api.Applications;

/// <summary>The mobile-facing API (ADR-0003): submit returns 202 and a status resource; the outcome follows asynchronously.</summary>
public static class ApplicationEndpoints
{
    /// <summary>Two base64 images of up to 8 MB each, plus JSON overhead.</summary>
    private const long MaxRequestBytes = 25 * 1024 * 1024;

    private const int MaxIdempotencyKeyLength = 100;

    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/applications")
            .RequireAuthorization(AtlasAuthentication.CustomerPolicy)
            .WithTags("Applications");

        group.MapPost("/", SubmitAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .Produces<ApplicationStatusResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{applicationId}", GetAsync)
            .Produces<ApplicationStatusResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> SubmitAsync(
        SubmitApplicationRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ClaimsPrincipal user,
        SubmitApplicationValidator validator,
        IMarketScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Idempotency-Key"] = [$"Header is required (at most {MaxIdempotencyKeyLength} characters); reuse it when retrying the same submission."],
            });
        }

        var validation = validator.Validate(request);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.Errors);
        }

        var submission = validation.Submission!;
        await using var scope = scopes.Begin(submission.Market, user.ToActor());
        var outcome = await scope.Get<SubmitApplicationHandler>().HandleAsync(request, submission, idempotencyKey, cancellationToken);

        return outcome switch
        {
            SubmitAccepted accepted => Results.Accepted($"/applications/{accepted.Application.ApplicationId}", accepted.Application),
            IdempotencyKeyReused => Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Idempotency-Key was already used for a different request.",
                detail: "Use a new key for a new submission."),
            _ => throw new InvalidOperationException($"Unhandled outcome {outcome.GetType().Name}."),
        };
    }

    private static async Task<IResult> GetAsync(
        string applicationId,
        ClaimsPrincipal user,
        IMarketScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        if (!ApplicationId.TryParse(applicationId, out var id) || !scopes.IsKnownMarket(id.Market))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Application not found.");
        }

        var actor = user.ToActor();
        await using var scope = scopes.Begin(id.Market, actor);
        var application = await scope.Get<OnboardingDbContext>().Applications.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

        // Customers only see their own applications; someone else's is indistinguishable from a missing one.
        return application is null || application.SubmittedBy != actor.Id
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Application not found.")
            : Results.Ok(ApplicationStatusResponse.From(application));
    }
}
