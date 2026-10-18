using System.ComponentModel.DataAnnotations;

namespace Atlas.Onboarding.Api.Providers;

public sealed class ProvidersOptions
{
    public const string SectionName = "Providers";

    [Required]
    public ProviderEndpointOptions IdNow { get; set; } = new();

    [Required]
    public ProviderEndpointOptions WorldCheck { get; set; } = new();
}

public sealed class ProviderEndpointOptions
{
    /// <summary>The real provider in production, the stub service locally. The only thing that differs (ADR-0012).</summary>
    [Required]
    public Uri? BaseUrl { get; set; }

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>Calls within the sampling window before the circuit breaker may open.</summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 5;
}
