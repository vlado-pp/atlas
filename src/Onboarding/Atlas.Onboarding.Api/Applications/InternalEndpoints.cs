using System.Security.Claims;
using Atlas.BuildingBlocks.Markets;
using Atlas.BuildingBlocks.Security;
using Atlas.Onboarding.Api.CustomerRecords;
using Atlas.Onboarding.Api.Persistence;
using Atlas.Onboarding.Domain.Applications;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.Api.Applications;

public sealed record CustomerProfileResponse(
    string ApplicationId,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string Email,
    string Phone,
    string IdentifierScheme,
    string IdentifierValue);

/// <summary>
/// Service-to-service API. Accounts reads the customer profile it needs to open the account here, under its own
/// service identity, rather than receiving personal data in a message (ADR-0009/0010).
/// </summary>
public static class InternalEndpoints
{
    public static IEndpointRouteBuilder MapInternalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/internal/applications/{applicationId}/customer-profile", GetCustomerProfileAsync)
            .RequireAuthorization(AtlasAuthentication.ServicePolicy)
            .WithTags("Internal");
        return endpoints;
    }

    private static async Task<IResult> GetCustomerProfileAsync(string applicationId, ClaimsPrincipal user, IMarketScopeFactory scopes, CancellationToken cancellationToken)
    {
        if (!ApplicationId.TryParse(applicationId, out var id) || !scopes.IsKnownMarket(id.Market))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Application not found.");
        }

        await using var scope = scopes.Begin(id.Market, user.ToActor());
        var application = await scope.Get<OnboardingDbContext>().Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Application not found.");
        }

        // Least privilege: the profile is only released once the application has passed all checks.
        if (application.Status is not (ApplicationStatus.AccountOpeningRequested or ApplicationStatus.Approved))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Application has not passed its checks.");
        }

        var applicant = await scope.Get<ICustomerRecords>().FindAsync(id, AccessPurpose.AccountOpening, cancellationToken);
        return applicant is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Application not found.")
            : Results.Ok(new CustomerProfileResponse(
                id.Value,
                applicant.FirstName,
                applicant.LastName,
                applicant.DateOfBirth,
                applicant.Email,
                applicant.Phone,
                applicant.Identifier.Scheme,
                applicant.Identifier.Value));
    }
}
