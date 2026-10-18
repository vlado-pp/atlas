using System.Security.Claims;
using Atlas.BuildingBlocks.Markets;
using Atlas.BuildingBlocks.Security;
using Atlas.Onboarding.Api.CustomerRecords;
using Atlas.Onboarding.Api.Persistence;
using Atlas.Onboarding.Domain.Applications;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.Api.Applications;

public sealed record BranchActivationRequest(string? BranchCode, string? SignedFormReference);

public sealed record BranchApplicantResponse(
    string ApplicationId,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string Nationality,
    string IdentifierScheme,
    string IdentifierValue);

/// <summary>
/// Branch staff complete in-person activation (ADR-0007, Annex B MD). Every call carries the individual staff
/// identity: the read is in the access log, the activation is in the status history.
/// </summary>
public static class BranchEndpoints
{
    public static IEndpointRouteBuilder MapBranchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/branch/applications/{applicationId}")
            .RequireAuthorization(AtlasAuthentication.StaffPolicy)
            .WithTags("Branch");

        group.MapGet("/applicant", GetApplicantAsync);
        group.MapPost("/activation", ActivateAsync);
        return endpoints;
    }

    /// <summary>Lets the branch check the person in front of them against the application. Only while a visit is expected.</summary>
    private static async Task<IResult> GetApplicantAsync(string applicationId, ClaimsPrincipal user, IMarketScopeFactory scopes, CancellationToken cancellationToken)
    {
        if (!ApplicationId.TryParse(applicationId, out var id) || !scopes.IsKnownMarket(id.Market))
        {
            return NotFound();
        }

        await using var scope = scopes.Begin(id.Market, user.ToActor());
        var application = await scope.Get<OnboardingDbContext>().Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        if (application.Status != ApplicationStatus.AwaitingBranchVisit)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Application is not awaiting a branch visit.");
        }

        var applicant = await scope.Get<ICustomerRecords>().FindAsync(id, AccessPurpose.BranchIdentityCheck, cancellationToken);
        return applicant is null
            ? NotFound()
            : Results.Ok(new BranchApplicantResponse(
                id.Value,
                applicant.FirstName,
                applicant.LastName,
                applicant.DateOfBirth,
                applicant.Nationality,
                applicant.Identifier.Scheme,
                applicant.Identifier.Value));
    }

    private static async Task<IResult> ActivateAsync(
        string applicationId,
        BranchActivationRequest request,
        ClaimsPrincipal user,
        IMarketScopeFactory scopes,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BranchCode) || string.IsNullOrWhiteSpace(request.SignedFormReference))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["branchCode"] = ["Required."],
                ["signedFormReference"] = ["Required: reference of the wet-signed form."],
            });
        }

        if (!ApplicationId.TryParse(applicationId, out var id) || !scopes.IsKnownMarket(id.Market))
        {
            return NotFound();
        }

        var staff = user.ToActor();
        await using var scope = scopes.Begin(id.Market, staff);
        var db = scope.Get<OnboardingDbContext>();
        var application = await db.Applications.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        var result = application.RecordBranchActivation(
            new BranchActivation(staff.Id, request.BranchCode.Trim(), request.SignedFormReference.Trim(), clock.GetUtcNow()));
        if (!result.IsSuccess)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: result.Error!.Message, type: result.Error.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ApplicationStatusResponse.From(application));
    }

    private static IResult NotFound() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Application not found.");
}
