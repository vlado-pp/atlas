using Atlas.Onboarding.Api.Markets;
using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Domain.Markets;
using Atlas.Onboarding.Domain.Verification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    public static MarketPolicy RemoteMarket(decimal minimumConfidence = 0.90m) => Policy("MB", ActivationMode.Remote, minimumConfidence);

    public static MarketPolicy BranchMarket() => Policy("MD", ActivationMode.InPersonBranch, 0.90m);

    public static IdentityCheckReport VerifiedReport() => new("idn_1", DocumentAuthenticity.Valid, FaceMatch: true, Confidence: 0.97m);

    public static ScreeningResult Clear() => new("wc_1", ScreeningOutcome.Clear);

    public static ScreeningResult PossibleMatch() => new("wc_1", ScreeningOutcome.PossibleMatch);

    /// <summary>The market policies exactly as production configures them (the Onboarding API's appsettings.json).</summary>
    public static MarketPolicies ConfiguredMarkets()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(SolutionRoot(), "src", "Onboarding", "Atlas.Onboarding.Api", "appsettings.json"))
            .Build();

        var options = new MarketsOptions();
        configuration.GetSection(MarketsOptions.SectionName).Bind(options.Markets);
        return new MarketPolicies(Options.Create(options));
    }

    private static MarketPolicy Policy(string market, ActivationMode activation, decimal minimumConfidence) =>
        new(
            market,
            [new IdentifierRule("PersonalNumber", "^[0-9]{13}$", CheckDigitAlgorithms.None, isPassport: false)],
            new ActivationPolicy(activation, TimeSpan.FromDays(30)),
            new ComplianceReviewPolicy(TimeSpan.FromHours(48), TimeSpan.FromMinutes(1)),
            minimumConfidence,
            minimumAge: 18,
            retentionYears: 10,
            defaultNationality: market);

    private static string SolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Atlas.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Solution root not found.");
    }
}

/// <summary>Builds an application already advanced to the state a test needs, through the real transitions.</summary>
internal sealed class ApplicationBuilder
{
    private readonly MarketPolicy _policy;
    private readonly List<Action<Application>> _steps = [];

    private ApplicationBuilder(MarketPolicy policy) => _policy = policy;

    public static ApplicationBuilder In(MarketPolicy policy) => new(policy);

    public ApplicationBuilder IdentityVerified()
    {
        _steps.Add(a => a.RecordIdentityVerification(TestData.VerifiedReport(), _policy, TestData.Now).IsSuccess.ShouldBeTrue());
        return this;
    }

    public ApplicationBuilder Screened(ScreeningResult result)
    {
        IdentityVerified();
        _steps.Add(a => a.RecordScreening(result, _policy, TestData.Now).IsSuccess.ShouldBeTrue());
        return this;
    }

    public Application Build()
    {
        var application = Application.Submit(ApplicationId.New(_policy.Market, TestData.Now), "device-1", _policy, TestData.Now);
        foreach (var step in _steps)
        {
            step(application);
        }

        application.ClearDomainEvents();
        return application;
    }
}
