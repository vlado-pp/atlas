using Atlas.Onboarding.Domain.Markets;

namespace Atlas.Onboarding.Api.Markets;

/// <summary>Market configuration (the <c>Markets</c> section), keyed by internal market code (Annex B).</summary>
public sealed class MarketsOptions
{
    public const string SectionName = "Markets";

    public Dictionary<string, MarketOptions> Markets { get; } = new(StringComparer.Ordinal);
}

public sealed class MarketOptions
{
    public List<IdentifierOptions> Identifiers { get; set; } = [];

    public ActivationOptions Activation { get; set; } = new();

    public ComplianceReviewOptions ComplianceReview { get; set; } = new();

    public decimal MinimumIdentityConfidence { get; set; } = 0.90m;

    public int MinimumAge { get; set; } = 18;

    public int RetentionYears { get; set; } = 10;

    /// <summary>Nationality sent to screening when the applicant gives none; defaults to the market code.</summary>
    public string? DefaultNationality { get; set; }
}

public sealed class IdentifierOptions
{
    public string Scheme { get; set; } = string.Empty;

    public string Pattern { get; set; } = string.Empty;

    public string CheckDigit { get; set; } = CheckDigitAlgorithms.None;

    /// <summary>A passport number accepted in lieu of the national identifier (MF, Annex B).</summary>
    public bool IsPassport { get; set; }
}

public sealed class ActivationOptions
{
    public ActivationMode Mode { get; set; } = ActivationMode.Remote;

    public int BranchVisitDeadlineDays { get; set; } = 30;
}

public sealed class ComplianceReviewOptions
{
    public int ReviewSlaHours { get; set; } = 48;

    public TimeSpan CaseStatusPollInterval { get; set; } = TimeSpan.FromSeconds(30);
}
