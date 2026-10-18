using System.Diagnostics.Metrics;
using Atlas.Onboarding.Api.Applications;
using Atlas.Onboarding.Domain.Applications;
using Microsoft.Extensions.Logging;

namespace Atlas.Onboarding.Api.Workflow;

/// <summary>
/// Measures the promise that matters (AC2): time from submit to a final answer, by market and outcome.
/// Also logged as a structured event so it can be charted in Seq without a metrics backend.
/// </summary>
public sealed class OnboardingMetrics : IDisposable
{
    public const string MeterName = "Atlas.Onboarding";

    private readonly Meter _meter;
    private readonly Histogram<double> _timeToDecision;
    private readonly Counter<long> _providerFailures;
    private readonly ILogger<OnboardingMetrics> _logger;

    public OnboardingMetrics(IMeterFactory meterFactory, ILogger<OnboardingMetrics> logger)
    {
        _meter = meterFactory.Create(MeterName);
        _timeToDecision = _meter.CreateHistogram<double>("atlas.onboarding.time_to_decision", unit: "s", description: "Time from submit to a final or customer-actionable status.");
        _providerFailures = _meter.CreateCounter<long>("atlas.onboarding.provider_failures");
        _logger = logger;
    }

    public void StatusReached(Application application, DateTimeOffset now)
    {
        var status = PublicStatus.Of(application.CustomerFacingStatus);
        var seconds = (now - application.SubmittedAt).TotalSeconds;
        _timeToDecision.Record(seconds, new("market", application.Market), new("status", status));
        _logger.LogInformation(
            "Application {ApplicationId} reached {PublicStatus} after {ElapsedSeconds:F1}s",
            application.Id,
            status,
            seconds);
    }

    public void ProviderFailed(string provider) => _providerFailures.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public void Dispose() => _meter.Dispose();
}
