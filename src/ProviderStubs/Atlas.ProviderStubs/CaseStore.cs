using System.Collections.Concurrent;

namespace Atlas.ProviderStubs;

/// <summary>A World-Check case created by a possible match, resolved later by a (simulated) compliance officer.</summary>
public sealed class StubCase
{
    public required string CaseId { get; init; }

    public required string Name { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string Status { get; set; } = "OPEN";

    public string? Resolution { get; set; }

    public string? ResolvedBy { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset? AutoResolveAt { get; init; }

    public string? AutoResolution { get; init; }
}

public sealed class CaseStore(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, StubCase> _cases = new(StringComparer.Ordinal);

    public IReadOnlyCollection<StubCase> All => _cases.Values.OrderByDescending(c => c.CreatedAt).ToList();

    public void Add(StubCase stubCase) => _cases[stubCase.CaseId] = stubCase;

    public StubCase? Get(string caseId)
    {
        if (!_cases.TryGetValue(caseId, out var stubCase))
        {
            return null;
        }

        if (stubCase is { Status: "OPEN", AutoResolveAt: { } due } && clock.GetUtcNow() >= due)
        {
            Resolve(stubCase, stubCase.AutoResolution ?? "FALSE_POSITIVE", "stub-officer-auto");
        }

        return stubCase;
    }

    public bool TryResolve(string caseId, string resolution, string officerId)
    {
        if (!_cases.TryGetValue(caseId, out var stubCase) || stubCase.Status != "OPEN")
        {
            return false;
        }

        Resolve(stubCase, resolution, officerId);
        return true;
    }

    private void Resolve(StubCase stubCase, string resolution, string officerId)
    {
        stubCase.Status = "RESOLVED";
        stubCase.Resolution = resolution;
        stubCase.ResolvedBy = officerId;
        stubCase.ResolvedAt = clock.GetUtcNow();
    }
}
