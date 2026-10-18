---
name: dotnet-clean-code
description: How to write production C#/.NET code for the Atlas services: solution layout, layering, design patterns (state machine, strategy, adapter/anti-corruption layer, options, result, outbox, decorator, value objects), clean-code rules, C# conventions, dependency choices and licensing pitfalls. Use whenever creating or modifying any .cs, .csproj, Directory.*.props, Program.cs or appsettings file.
---

# .NET clean code for Atlas

Senior-level, production-grade, and **explainable**: the author must be able to defend every line in an
interview. Prefer boring, explicit code over clever code. If a pattern can't be justified in one sentence
for *this* problem, don't use it.

Always check `atlas-requirements-guardrails` first for domain invariants.

## Platform baseline

- **Target framework:** `net10.0` (ADR-0001), set once in `Directory.Build.props`.
- **Service boundaries and workflow** are fixed by ADR-0002 (Onboarding, Accounts, ProviderStubs) and
  ADR-0004 (DB-driven workflow runner inside Onboarding, own outbox/inbox on RabbitMQ.Client between
  services, no MassTransit). Read `docs/decisions/` before adding projects.
- `Directory.Build.props`: `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`,
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<AnalysisLevel>latest-recommended</AnalysisLevel>`,
  `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`.
- `Directory.Packages.props` with **Central Package Management**. Pin exact versions.
- `.editorconfig` at the root. Code must build with zero warnings.
- `global.json` pins the SDK so reviewers get a reproducible build.

## Dependency choices (and why)

| Need | Use | Avoid / note |
|---|---|---|
| In-process dispatch | Plain interfaces / handlers invoked directly | **MediatR**: commercial licence since v13, and adds indirection you'd have to defend |
| Mapping | Hand-written `ToDomain()` / `ToResponse()` methods | **AutoMapper**: commercial licence, hides mapping bugs |
| Validation | FluentValidation (Apache-2), or hand-rolled for value objects | Validating inside controllers |
| Resilience | `Microsoft.Extensions.Http.Resilience` (Polly v8) | Hand-written retry loops |
| Persistence | EF Core (SQL Server) | Generic `IRepository<T>` over EF |
| Messaging | Decide in an ADR: RabbitMQ.Client directly, MassTransit **v8** (Apache-2; v9 is commercial), or Wolverine | Picking a library you can't explain |
| Logging | Serilog → Seq, or `ILogger` + OpenTelemetry | `Console.WriteLine`, string interpolation in log calls |
| Time | `TimeProvider` (injected) | `DateTime.Now` / `DateTime.UtcNow` in logic |

## Layout (per service)

Choose **one** of the following per service and stay consistent:

- **Layered (Clean/Onion):** `Atlas.<Service>.Api` → `.Application` → `.Domain` ← `.Infrastructure`.
  The domain has no dependency on EF, HTTP or messaging.
- **Vertical slices:** `Features/<UseCase>/` holding endpoint + handler + validator, plus a `Domain/` folder.
  Less ceremony for small services.

Don't do both in the same service. Service boundaries themselves are an architectural decision for an ADR.
Don't create a project per pattern.

Shared code: only truly cross-cutting contracts (`Atlas.Contracts` for message/integration contracts,
maybe `Atlas.BuildingBlocks` for outbox/observability wiring). **No shared domain library across services.**

## Design patterns: where they fit in Atlas

Use a pattern because it solves a problem listed here, not to show it off.

- **Aggregate + explicit state machine**: the onboarding `Application` lifecycle
  (`Draft → Submitted → IdentityVerified → Screened → PendingComplianceReview → Approved/Rejected →
  AccountOpened → PendingBranchActivation → Active`, or whatever the ADR settles on). Transitions are methods
  on the aggregate (`application.RecordScreeningResult(result, clock)`). Illegal transitions fail
  explicitly. State is never set from outside. Raise domain events from transitions.
- **Strategy (keyed by market)**: `IMarketPolicy` / `IIdentifierValidator` resolved by `MarketCode`, built
  from configuration (`IOptions<MarketsOptions>`). Use keyed services (`AddKeyedSingleton`) or a small
  registry. This is how "market differences are configuration, not code" (AC4) is achieved.
- **Adapter / Anti-Corruption Layer**: `IIdentityVerificationProvider` and `ISanctionsScreeningProvider`
  defined in Application/Domain terms. The IDNow/World-Check DTOs live only in Infrastructure and are mapped
  to domain outcomes (`IdentityCheckOutcome.Passed/Failed/Inconclusive`). Provider enums never leak.
- **Options pattern** with `ValidateDataAnnotations()`/custom `IValidateOptions<T>` + `ValidateOnStart()`.
  Misconfiguration fails at boot, not at the first request from MD.
- **Result pattern** for *expected* business outcomes (validation failure, rejected application). Use
  exceptions for *exceptional* situations (bugs, infrastructure down). Don't use exceptions for control flow.
- **Value objects** (`readonly record struct` / `sealed record`): `MarketCode`, `NationalIdentifier
  { Scheme, Value }`, `ApplicationId`, `Email`. Validate in the factory (`Create(...)` returning a Result).
  Make invalid states unrepresentable.
- **Transactional Outbox**: state change + outgoing message are committed in one DB transaction. A
  dispatcher publishes later. Required wherever "save then publish" would otherwise be two writes.
- **Idempotent consumer / Inbox**: every message handler is safe to run twice.
- **Decorator**: cross-cutting behaviour around an interface, e.g. an access-auditing decorator around the
  PII reader (GC §2), or logging around a provider adapter.
- **Process manager / Saga**: if onboarding is orchestrated across services and time (48h review), one
  component owns the workflow state. See `atlas-messaging-workflow`.

Anti-patterns to reject: anemic domain + god "Service" classes, service locator (`IServiceProvider` in
business code), static mutable state, `async void`, `.Result`/`.Wait()`, catching `Exception` and
swallowing it, primitive obsession (`string country`), boolean parameters that switch behaviour, and
`if (market == "MD")` anywhere outside the policy configuration.

## Clean code rules

- **Names** reveal intent and use the domain language from the requirements (`Screening`, `PossibleMatch`,
  `ComplianceReview`, `BranchActivation`). No `Manager`, `Helper`, `Util`, `Data`, `Info` suffixes.
- **Small units:** a method does one thing at one level of abstraction. Roughly 20 lines is a smell
  threshold, not a rule. A class has one reason to change.
- **Guard clauses** over nested `if`s. Return early.
- **Immutability by default:** `sealed` classes, `init`/`readonly`, records for DTOs and messages,
  `IReadOnlyCollection<T>` on public surfaces.
- **Async all the way** with a `CancellationToken` parameter on every async method that does I/O, passed
  through. No `ConfigureAwait(false)` noise in ASP.NET Core app code.
- **No magic values:** market rules, timeouts, retry counts and thresholds (e.g. IDNow `confidence`) come
  from options.
- **Comments explain *why*** (often a reference like `// GC-2026-0814 §3: possible matches must be reviewed
  by a human`). Code explains *what*. Delete commented-out code.
- **Errors:** never put PII in exception messages or logs. Log identifiers (`ApplicationId`), not people.
- **DI lifetimes:** singletons must not capture scoped services (`DbContext`). Enable
  `ValidateScopes`/`ValidateOnBuild` in Development.
- **Primary constructors**: fine for DI-only classes. Avoid them where you need validation or readonly
  guarantees on captured parameters.
- **Minimal APIs vs controllers**: pick one per solution and say why in an ADR. With Minimal APIs, group
  endpoints with `MapGroup` and keep handlers thin. They call the application layer.

## Definition of done for a code change

- [ ] Builds with zero warnings. `dotnet format --verify-no-changes` passes.
- [ ] Guardrail invariants still hold.
- [ ] Unit tests for domain logic/policies (`dotnet-unit-testing`). Integration tests if wiring, persistence or
      messaging changed (`dotnet-integration-testing`).
- [ ] No PII in logs/exceptions. No secrets committed.
- [ ] Any non-obvious decision has an ADR entry or a `why` comment.
- [ ] README run instructions still accurate.
- [ ] You can explain the change and the alternative you didn't take.
