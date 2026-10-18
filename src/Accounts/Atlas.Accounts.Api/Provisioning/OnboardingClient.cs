using System.Net.Http.Headers;
using System.Net.Http.Json;
using Atlas.BuildingBlocks;

namespace Atlas.Accounts.Api.Provisioning;

public sealed class OnboardingClientOptions
{
    public const string SectionName = "Onboarding";

    public Uri? BaseUrl { get; set; }

    /// <summary>This service's own identity. Development token locally; client credentials in production (ADR-0010).</summary>
    public string ServiceToken { get; set; } = "dev:service:accounts";
}

/// <summary>Accounts' own view of the profile; services do not share DTO libraries.</summary>
public sealed record CustomerProfile(
    string ApplicationId,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string Email,
    string Phone,
    string IdentifierScheme,
    string IdentifierValue);

/// <summary>Reads the customer profile from Onboarding under this service's identity; Onboarding audits the read.</summary>
public sealed class OnboardingClient(HttpClient http)
{
    public async Task<CustomerProfile> GetCustomerProfileAsync(string applicationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"internal/applications/{Uri.EscapeDataString(applicationId)}/customer-profile", UriKind.Relative));
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CustomerProfile>(AtlasJson.Options, cancellationToken)
            ?? throw new InvalidOperationException("Onboarding returned an empty customer profile.");
    }

    internal static void Configure(HttpClient http, OnboardingClientOptions options)
    {
        http.BaseAddress = options.BaseUrl ?? throw new InvalidOperationException("Onboarding:BaseUrl is not configured.");
        var token = options.ServiceToken;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
