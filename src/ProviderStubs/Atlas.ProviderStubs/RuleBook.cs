using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Atlas.ProviderStubs;

/// <summary>Configured rules plus rules added at runtime through the control API (runtime rules win).</summary>
public sealed class RuleBook(IOptions<StubRulesOptions> configured)
{
    private readonly Lock _lock = new();
    private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);
    private readonly List<IdNowRule> _runtimeIdNow = [];
    private readonly List<WorldCheckRule> _runtimeWorldCheck = [];

    public IdNowRule? MatchIdNow(string requestText) => Match(_runtimeIdNow, configured.Value.IdNow, requestText);

    public WorldCheckRule? MatchWorldCheck(string name) => Match(_runtimeWorldCheck, configured.Value.WorldCheck, name);

    public void Add(IdNowRule rule)
    {
        lock (_lock)
        {
            _runtimeIdNow.Insert(0, rule);
        }
    }

    public void Add(WorldCheckRule rule)
    {
        lock (_lock)
        {
            _runtimeWorldCheck.Insert(0, rule);
        }
    }

    public void ResetRuntime()
    {
        lock (_lock)
        {
            _runtimeIdNow.Clear();
            _runtimeWorldCheck.Clear();
        }

        _calls.Clear();
    }

    public object Describe()
    {
        lock (_lock)
        {
            return new
            {
                runtime = new { idNow = _runtimeIdNow.ToList(), worldCheck = _runtimeWorldCheck.ToList() },
                configured = configured.Value,
            };
        }
    }

    /// <summary>Counts calls per rule and request fingerprint; true while the rule should still fail this request.</summary>
    public bool ShouldFail(StubRule rule, string fingerprint)
    {
        if (rule.FailTimes == 0)
        {
            return false;
        }

        var attempt = _calls.AddOrUpdate($"{rule.Id}|{fingerprint}", 1, (_, n) => n + 1);
        return rule.FailTimes < 0 || attempt <= rule.FailTimes;
    }

    private TRule? Match<TRule>(List<TRule> runtime, List<TRule> fromConfig, string text)
        where TRule : StubRule
    {
        lock (_lock)
        {
            return runtime.Concat(fromConfig).FirstOrDefault(r => r.Match.Length > 0 && text.Contains(r.Match, StringComparison.OrdinalIgnoreCase));
        }
    }
}
