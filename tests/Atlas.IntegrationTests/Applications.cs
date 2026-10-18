using System.Net.Http.Json;
using System.Text;
using Atlas.Onboarding.Api.Applications;
using Atlas.Onboarding.Api.Applications.Submit;

namespace Atlas.IntegrationTests;

/// <summary>Request builders and polling helpers. All personal data here is synthetic.</summary>
internal static class Applications
{
    public static SubmitApplicationRequest Request(
        string country = "MB",
        string lastName = "Applicant",
        string? nationalId = "0403991450016",
        string documentMarker = "",
        string? passportNumber = null,
        string? nationality = null) =>
        new(
            FirstName: "Test",
            LastName: lastName,
            DateOfBirth: "1991-03-04",
            Country: country,
            NationalId: nationalId,
            PassportNumber: passportNumber,
            Nationality: nationality,
            Email: "applicant@example.test",
            Phone: "+000 000 000",
            Documents:
            [
                // Unique per request, so the stub's per-request failure counters never leak between tests.
                new DocumentUpload("PASSPORT", Base64($"synthetic-passport {Guid.NewGuid()} {documentMarker}")),
                new DocumentUpload("SELFIE", Base64("synthetic-selfie")),
            ],
            TermsAccepted: true);

    /// <summary>A last name that triggers a stub rule and is unique to the calling test.</summary>
    public static string Name(string stubRule) => $"{stubRule}{Guid.NewGuid().ToString("N")[..6]}";

    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, SubmitApplicationRequest request, string? idempotencyKey = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/applications") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return await client.SendAsync(message);
    }

    public static async Task<ApplicationStatusResponse> SubmitAsync(HttpClient client, SubmitApplicationRequest request)
    {
        using var response = await PostAsync(client, request);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApplicationStatusResponse>())!;
    }

    public static async Task<ApplicationStatusResponse> GetAsync(HttpClient client, string applicationId) =>
        (await client.GetFromJsonAsync<ApplicationStatusResponse>($"/applications/{applicationId}"))!;

    /// <summary>Polls the public status until it equals <paramref name="expected"/>, failing after the timeout. No sleeps in tests.</summary>
    public static async Task<ApplicationStatusResponse> WaitForStatusAsync(HttpClient client, string applicationId, string expected, int timeoutSeconds = 30)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var ticks = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        var last = await GetAsync(client, applicationId);
        try
        {
            while (last.Status != expected && await ticks.WaitForNextTickAsync(timeout.Token))
            {
                last = await GetAsync(client, applicationId);
            }
        }
        catch (OperationCanceledException)
        {
        }

        last.Status.ShouldBe(expected, $"Application {applicationId} did not reach {expected} within {timeoutSeconds}s.");
        return last;
    }

    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
}
