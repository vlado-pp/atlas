namespace Atlas.Onboarding.Api.Workflow;

/// <summary>Capped exponential backoff with ±20% jitter, so applications parked by an outage don't all retry at once.</summary>
public static class RetryBackoff
{
    public static TimeSpan For(int attempt, TimeSpan baseDelay, TimeSpan maxDelay, Random? random = null)
    {
        var exponent = Math.Min(Math.Max(attempt, 0), 20);
        var delay = Math.Min(baseDelay.TotalMilliseconds * Math.Pow(2, exponent), maxDelay.TotalMilliseconds);
#pragma warning disable CA5394 // Jitter, not security.
        var jitter = 0.8 + ((random ?? Random.Shared).NextDouble() * 0.4);
#pragma warning restore CA5394
        return TimeSpan.FromMilliseconds(delay * jitter);
    }
}
