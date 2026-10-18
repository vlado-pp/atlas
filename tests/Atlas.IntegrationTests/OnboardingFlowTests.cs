using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Accounts.Api.Persistence;
using Atlas.BuildingBlocks.Security;
using Atlas.Onboarding.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.IntegrationTests;

/// <summary>End-to-end through the public API, driving the provider stand-ins into each scenario without code changes.</summary>
[Collection(AtlasSystemCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OnboardingFlowTests(AtlasSystem system)
{
    private readonly HttpClient _customer = system.Customer($"device-{Guid.NewGuid():N}");

    [Fact]
    public async Task RemoteMarket_AllChecksClear_IsApprovedWithAnOpenAccountAndOrderedCard()
    {
        var submitted = await Applications.SubmitAsync(_customer, Applications.Request(country: "MB"));
        submitted.Status.ShouldBe("IN_PROGRESS");

        await Applications.WaitForStatusAsync(_customer, submitted.ApplicationId, "APPROVED");

        await using var accounts = AtlasSystem.MarketScope(system.Accounts, "MB");
        var account = await accounts.Get<AccountsDbContext>().Accounts.SingleAsync(a => a.ApplicationId == submitted.ApplicationId);
        account.AccountNumber.ShouldNotBeNullOrEmpty();
        account.CardOrderReference.ShouldNotBeNull();
    }

    [Fact]
    public async Task RemoteMarket_AccessToPersonalData_IsAttributedInTheAccessLog()
    {
        var submitted = await Applications.SubmitAsync(_customer, Applications.Request(country: "MA"));
        await Applications.WaitForStatusAsync(_customer, submitted.ApplicationId, "APPROVED");

        await using var onboarding = AtlasSystem.MarketScope(system.Onboarding, "MA");
        var entries = await onboarding.Get<OnboardingDbContext>().AccessLog
            .Where(e => e.RecordId.StartsWith(submitted.ApplicationId))
            .ToListAsync();

        entries.ShouldContain(e => e.ActorType == ActorType.Customer && e.Action == AccessAction.Create);
        entries.ShouldContain(e => e.ActorType == ActorType.Service && e.ActorId == "onboarding-workflow" && e.Purpose == "IdentityVerification");
        entries.ShouldContain(e => e.ActorType == ActorType.Service && e.ActorId == "accounts" && e.Purpose == "AccountOpening");
        entries.ShouldAllBe(e => e.RetainUntil > DateTimeOffset.UtcNow.AddYears(9));
    }

    [Fact]
    public async Task PossibleMatch_StaysUnderReviewUntilAnOfficerClearsIt_ThenIsApproved()
    {
        var lastName = Applications.Name("PossibleMatch");
        var submitted = await Applications.SubmitAsync(_customer, Applications.Request(lastName: lastName));

        await Applications.WaitForStatusAsync(_customer, submitted.ApplicationId, "UNDER_REVIEW");
        await Task.Delay(TimeSpan.FromSeconds(2)); // Several case polls later, nothing has been decided automatically.
        (await Applications.GetAsync(_customer, submitted.ApplicationId)).Status.ShouldBe("UNDER_REVIEW");

        await ResolveCaseAsync(lastName, "CLEARED");

        await Applications.WaitForStatusAsync(_customer, submitted.ApplicationId, "APPROVED");
    }

    [Fact]
    public async Task PossibleMatch_ConfirmedByAnOfficer_IsRejectedWithoutSayingWhy()
    {
        var lastName = Applications.Name("PossibleMatch");
        var submitted = await Applications.SubmitAsync(_customer, Applications.Request(lastName: lastName));
        await Applications.WaitForStatusAsync(_customer, submitted.ApplicationId, "UNDER_REVIEW");

        await ResolveCaseAsync(lastName, "CONFIRMED");

        var final = await Applications.WaitForStatusAsync(_customer, submitted.ApplicationId, "REJECTED");
        final.Reason.ShouldBe("NOT_ELIGIBLE");
    }

    [Theory]
    [InlineData("idnow:INVALID", "IDENTITY_NOT_VERIFIED")]
    [InlineData("idnow:FACE_MISMATCH", "IDENTITY_NOT_VERIFIED")]
    [InlineData("idnow:INCONCLUSIVE", "RECAPTURE_REQUIRED")]
    [InlineData("idnow:LOW_CONFIDENCE", "RECAPTURE_REQUIRED")]
    public async Task IdentityNotClearlyVerified_IsRejectedWithAReason(string documentMarker, string expectedReason)
    {
        var submitted = await Applications.SubmitAsync(_customer, Applications.Request(documentMarker: documentMarker));

        var final = await Applications.WaitForStatusAsync(_customer, submitted.ApplicationId, "REJECTED");

        final.Reason.ShouldBe(expectedReason);
    }

    [Fact]
    public async Task IdentityProviderFlaky_RecoversWithinTheCallsRetries_IsApproved()
    {
        var submitted = await Applications.SubmitAsync(_customer, Applications.Request(documentMarker: "idnow:FLAKY"));

        await Applications.WaitForStatusAsync(_customer, submitted.ApplicationId, "APPROVED");
    }

    [Fact]
    public async Task ScreeningProviderFlaky_RecoversWithinTheCallsRetries_IsApproved()
    {
        var submitted = await Applications.SubmitAsync(_customer, Applications.Request(lastName: Applications.Name("Flaky")));

        await Applications.WaitForStatusAsync(_customer, submitted.ApplicationId, "APPROVED");
    }

    [Fact]
    public async Task IdentityProviderDown_ApplicationWaitsAndRetries_ButIsNeverRejected()
    {
        var submitted = await Applications.SubmitAsync(_customer, Applications.Request(country: "ME", nationalId: "1234567890", documentMarker: "idnow:DOWN"));
        var id = ApplicationId.FromTrusted(submitted.ApplicationId);

        await using (var scope = AtlasSystem.MarketScope(system.Onboarding, "ME"))
        {
            var db = scope.Get<OnboardingDbContext>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while ((await db.Applications.AsNoTracking().SingleAsync(a => a.Id == id, timeout.Token)).AttemptCount < 2)
            {
                await Task.Delay(200, timeout.Token);
            }
        }

        (await Applications.GetAsync(_customer, submitted.ApplicationId)).Status.ShouldBe("IN_PROGRESS");
    }

    [Fact]
    public async Task InPersonActivationMarket_AwaitsBranchVisit_StaffActivates_IsApproved()
    {
        var submitted = await Applications.SubmitAsync(_customer, Applications.Request(country: "MD", nationalId: "1234567890"));
        await Applications.WaitForStatusAsync(_customer, submitted.ApplicationId, "AWAITING_BRANCH_VISIT");

        var activation = new { branchCode = "MD-001", signedFormReference = "form-2026-0001" };
        using (var byCustomer = await _customer.PostAsJsonAsync($"/branch/applications/{submitted.ApplicationId}/activation", activation))
        {
            byCustomer.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        var staff = system.Staff("teller-12");
        using (var applicant = await staff.GetAsync($"/branch/applications/{submitted.ApplicationId}/applicant"))
        {
            applicant.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var byStaff = await staff.PostAsJsonAsync($"/branch/applications/{submitted.ApplicationId}/activation", activation))
        {
            byStaff.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await Applications.WaitForStatusAsync(_customer, submitted.ApplicationId, "APPROVED");
    }

    [Fact]
    public async Task ResidentWithoutNationalId_OnboardsWithPassportInLieu()
    {
        var request = Applications.Request(country: "MF", nationalId: null, passportNumber: "AB1234567", nationality: "SY");

        var submitted = await Applications.SubmitAsync(_customer, request);

        await Applications.WaitForStatusAsync(_customer, submitted.ApplicationId, "APPROVED");
    }

    [Fact]
    public async Task Resubmit_WithTheSameIdempotencyKey_ReturnsTheSameApplication()
    {
        var request = Applications.Request();
        var key = Guid.NewGuid().ToString();

        using var first = await Applications.PostAsync(_customer, request, key);
        using var retry = await Applications.PostAsync(_customer, request, key);
        using var differentBody = await Applications.PostAsync(_customer, request with { LastName = "Someone Else" }, key);

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        retry.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await IdOf(retry)).ShouldBe(await IdOf(first));
        differentBody.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Submit_IdentifierInTheWrongFormatForTheMarket_IsAFieldValidationError()
    {
        using var response = await Applications.PostAsync(_customer, Applications.Request(country: "MB", nationalId: "12345"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").TryGetProperty("nationalId", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Submit_WithoutIdempotencyKey_IsRejected()
    {
        using var response = await _customer.PostAsJsonAsync("/applications", Applications.Request());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Submit_Unauthenticated_IsRejected()
    {
        using var anonymous = system.Onboarding.CreateClient();

        using var response = await Applications.PostAsync(anonymous, Applications.Request());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetApplication_OfAnotherCustomer_IsNotFound()
    {
        var submitted = await Applications.SubmitAsync(_customer, Applications.Request());
        var someoneElse = system.Customer("another-device");

        using var response = await someoneElse.GetAsync($"/applications/{submitted.ApplicationId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<string> IdOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applicationId").GetString()!;

    private async Task ResolveCaseAsync(string lastName, string outcome)
    {
        using var control = system.StubControl();
        var cases = await control.GetFromJsonAsync<JsonElement>("/_control/cases");
        var caseId = cases.EnumerateArray()
            .Single(c => c.GetProperty("name").GetString()!.EndsWith(lastName, StringComparison.Ordinal))
            .GetProperty("caseId").GetString();

        using var resolved = await control.PostAsJsonAsync($"/_control/cases/{caseId}/resolve", new { outcome, officerId = "officer-7" });
        resolved.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
