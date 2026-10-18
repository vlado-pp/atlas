# 0012. Provider stand-ins: an HTTP stub service steered by rules and a control API

Status: Accepted · Date: 2026-10-18

## Context
There are no credentials and no sandbox. The reviewers will check "whether we can drive their different
responses through your stand-in without editing your code".

## Decision
- `Atlas.ProviderStubs` is a separate ASP.NET Core app implementing the **documented wire contracts** of
  IDNow (`POST /v1/identifications`) and World-Check (`POST /worldcheck/v2/screen`, plus an assumed case
  status endpoint). Onboarding calls it through the **same typed `HttpClient`s and resilience pipeline**
  used in production. Only the base URL is configuration.
- Scenario selection, all without code changes:
  1. **Rules in the stub's `appsettings.json`**, matched on the request: last name `PossibleMatch*`
     → `POSSIBLE_MATCH`, `Sanctioned*` → match later confirmed, `Slow*` → latency, `Down*` → 503 N times
     then 200, a document image starting with a marker → `INVALID`/`INCONCLUSIVE`, and so on. Default is the
     happy path.
  2. **Control API** (`GET /_control/rules`, `POST /_control/rules/idnow`, `POST /_control/rules/worldcheck`,
     `DELETE /_control/rules`) to add or override rules at runtime, and `POST /_control/cases/{caseId}/resolve`
     with `{ "outcome": "CLEARED" | "CONFIRMED", "officerId": "..." }` to play the compliance officer.
     Rules support a status, latency, and "fail N times with status X" (503, 429, ...).
  3. **Core banking / card processor**: in-process fakes inside Accounts behind `ICoreBankingGateway` and
     `ICardProcessorGateway`, with failure injection via configuration. The brief says a plausible seam is
     enough.
- `requests/*.http` holds one runnable request per scenario.

## Alternatives considered
- **In-process fakes for IDNow/World-Check**: simpler, but they skip the HTTP client, serialization,
  timeouts and retry policy, which are the parts most likely to break.
- **WireMock.Net as the runtime stand-in**: excellent for tests (and used there), but the 48h case lifecycle
  is stateful behaviour that's clearer as code than as WireMock mappings.
- **Header-driven scenarios (`X-Scenario`)**: means mobile-facing requests carry test-only fields into
  production code paths.

## In 30 seconds
The stub speaks the provider's wire format, so the real client code is exercised. Reviewers pick scenarios
through test data or a control API, and nothing in Atlas knows it's talking to a fake.
