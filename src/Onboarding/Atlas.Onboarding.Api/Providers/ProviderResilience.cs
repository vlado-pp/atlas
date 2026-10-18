using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Atlas.Onboarding.Api.Providers;

/// <summary>
/// In-call resilience for a provider: short, bounded retries for blips. Longer outages are handled one level up by
/// the workflow runner, which reschedules the application with backoff instead of holding a request open (ADR-0004).
///
/// Order, outermost first: total timeout → retry → circuit breaker → per-attempt timeout.
/// </summary>
internal static class ProviderResilience
{
    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> builder, ProviderEndpointOptions options)
    {
        builder
            .AddTimeout(options.TotalTimeout)
            .AddRetry(new HttpRetryStrategyOptions
            {
                // Retries timeouts, 408, 429 (honouring Retry-After), 5xx and connection failures. Never 4xx validation errors.
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(500),
                ShouldRetryAfterHeader = true,
            })
            .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                // Stop hammering a provider that is down; the workflow parks applications until it recovers.
                FailureRatio = 0.5,
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(15),
            })
            .AddTimeout(options.AttemptTimeout);
    }
}
