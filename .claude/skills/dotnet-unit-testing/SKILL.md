---
name: dotnet-unit-testing
description: How to write fast, deterministic unit tests for the Atlas .NET services (xUnit, NSubstitute, test data builders, FakeTimeProvider, naming, AAA, table-driven theories) and what to prioritise testing in the onboarding domain. Use when creating or changing tests in *.UnitTests projects, or when adding domain logic, market policies, state transitions, provider-result mapping or validators that need coverage.
---

# Unit testing for Atlas

The brief asks for "automated tests around whatever you judge to be the parts most likely to break". Coverage
percentage is not the goal. **Risk coverage** is. Every test should protect a rule you could point to in the
requirements.

## Stack

- **xUnit** (v2 or v3, pick one solution-wide), `Microsoft.NET.Test.Sdk`, `coverlet.collector`.
- **Assertions:** xUnit `Assert` or **Shouldly** / **AwesomeAssertions** (Apache-2 forks).
  Avoid FluentAssertions v8+ (commercial licence). Only use v7 if you note the reason.
- **Test doubles:** NSubstitute. Prefer hand-written fakes for things with behaviour (e.g. an in-memory
  `FakeSanctionsProvider` with scripted responses).
- **Time:** `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) for deadlines, 48h review
  timeouts and retention dates.
- Project naming: `Atlas.<Service>.UnitTests`, mirroring the production folder structure.

## What to test first (highest risk in this domain)

1. **Application state machine**: every legal transition, and that illegal ones fail. In particular:
   - `POSSIBLE_MATCH` → `PendingComplianceReview`, never `Rejected`/`Approved` (GC §3).
   - No account opening before screening `CLEAR` and identity passed (AC5).
   - MD-type policy → approved but `PendingBranchActivation`, not `Active` (Annex B).
2. **Per-market identifier validation**: table-driven `[Theory]` per market: valid, wrong length, wrong
   character class, MC letter-in-position-9, MF passport-in-lieu, missing identifier where allowed/not
   allowed. Driven by the *same configuration* production uses.
3. **Provider result → domain outcome mapping** (anti-corruption layer): every enum value of
   `documentResult`, `faceMatch=false`, confidence below/at/above threshold, `INCONCLUSIVE`, unknown/new enum
   value from provider (must not crash or silently approve).
4. **Decision logic**: the combination matrix of identity × screening outcomes.
5. **Idempotency**: handling the same command/message twice produces one effect.
6. **Retention / erasure rules**: erasure during the retention period vs after it, with `FakeTimeProvider`.
7. **Options validation**: misconfigured market (unknown code, missing identifier rule) fails `ValidateOnStart`.

Don't unit-test: EF mappings, ASP.NET plumbing, DI registration, or third-party libraries. Those belong
in integration tests.

## Conventions

- **Name:** `Method_or_Behaviour_Scenario_ExpectedOutcome`, e.g.
  `RecordScreeningResult_PossibleMatch_MovesToPendingComplianceReview`. The test name should read like a
  requirement.
- **Arrange / Act / Assert**, separated by blank lines (no comments needed if obvious). One *act* per test.
- **One behaviour per test.** Multiple asserts are fine if they describe one outcome.
- **No logic in tests:** no `if`, loops or try/catch. Use `[Theory]` + `[InlineData]` / `[MemberData]` /
  `TheoryData<>` for tables.
- **Test data builders** (`ApplicationBuilder.ForMarket("MF").WithPassportInLieu().Build()`) with sensible
  defaults, so each test states only what matters to it. Never use real-looking personal data. Use
  obviously synthetic values.
- **Mock roles, not objects:** substitute interfaces you own (`ISanctionsScreeningProvider`), never
  `HttpClient`, `DbContext` or types you don't own. To test an HTTP adapter, use a stub
  `HttpMessageHandler` or WireMock in integration tests.
- **Verify outcomes over interactions.** Assert on state/returned results. Use `Received()` only when the
  interaction *is* the behaviour (e.g. "card is ordered exactly once").
- **Deterministic:** no `Thread.Sleep`, no real clock, no randomness without a fixed seed, no ordering
  dependence, no shared mutable static state.
- **Fast:** the whole unit suite should run in seconds. Anything touching I/O belongs elsewhere.
- **Failure messages** should tell the reader what rule broke without opening the test.

## Template

```csharp
public sealed class ApplicationScreeningTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void RecordScreeningResult_PossibleMatch_MovesToPendingComplianceReview()
    {
        var application = ApplicationBuilder.ForMarket("MB").IdentityVerified().Build();

        var result = application.RecordScreeningResult(ScreeningOutcome.PossibleMatch, _clock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.PendingComplianceReview);
    }
}
```

## Before finishing

- [ ] Each new rule in production code has a test that fails if the rule is removed. Check this by
      temporarily breaking the code (manual mutation test).
- [ ] `dotnet test --filter Category!=Integration` (or the unit test project alone) passes quickly.
- [ ] No PII-looking data and no real provider payloads with personal data in fixtures.
