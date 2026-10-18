namespace Atlas.Onboarding.Api.Workflow;

public sealed class WorkflowOptions
{
    public const string SectionName = "Workflow";

    /// <summary>How often each replica looks for due work when the previous pass found none.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// How long a claimed application is hidden from other replicas. Must exceed the slowest step (the provider
    /// total timeout); if a replica dies mid-step, another picks the application up after the lease.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>After this many failed attempts at one step the application is reported as stalled (it still retries).</summary>
    public int StalledAfterAttempts { get; set; } = 10;
}
