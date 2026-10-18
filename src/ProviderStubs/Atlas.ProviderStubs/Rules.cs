namespace Atlas.ProviderStubs;

/// <summary>
/// A scenario rule. The first rule whose <see cref="Match"/> occurs in the request (case-insensitive) decides the
/// response; with no match the provider answers the happy path. For IDNow the match is against the decoded
/// document and selfie bytes as text, for World-Check against the screened name.
/// </summary>
public abstract class StubRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string Match { get; set; } = string.Empty;

    /// <summary>Delay before answering; above the client's attempt timeout this simulates a hanging provider.</summary>
    public int LatencyMs { get; set; }

    /// <summary>Fail this many calls per distinct request before answering normally; -1 fails forever.</summary>
    public int FailTimes { get; set; }

    public int FailStatusCode { get; set; } = StatusCodes.Status503ServiceUnavailable;
}

public sealed class IdNowRule : StubRule
{
    public string DocumentResult { get; set; } = "VALID";

    public bool FaceMatch { get; set; } = true;

    public decimal Confidence { get; set; } = 0.97m;
}

public sealed class WorldCheckRule : StubRule
{
    public string Status { get; set; } = "CLEAR";

    public string ListType { get; set; } = "SANCTIONS";

    public decimal Score { get; set; } = 0.82m;

    /// <summary>When set, a possible-match case resolves by itself after this many seconds (an "officer" in the stub).</summary>
    public int? AutoResolveAfterSeconds { get; set; }

    /// <summary><c>FALSE_POSITIVE</c> or <c>TRUE_MATCH</c>.</summary>
    public string AutoResolution { get; set; } = "FALSE_POSITIVE";
}

public sealed class StubRulesOptions
{
    public const string SectionName = "StubRules";

    public List<IdNowRule> IdNow { get; set; } = [];

    public List<WorldCheckRule> WorldCheck { get; set; } = [];
}
