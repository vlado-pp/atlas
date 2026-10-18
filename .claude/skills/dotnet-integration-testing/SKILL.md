---
name: dotnet-integration-testing
description: How to write integration and end-to-end tests for the Atlas services: WebApplicationFactory, real SQL Server from infra/docker-compose.yml, database isolation, provider stubs driven by scenario, messaging/outbox tests, and the end-to-end onboarding scenarios that prove the flow works including the awkward paths. Use when creating *.IntegrationTests projects or testing API endpoints, EF Core persistence, migrations, message consumers, resilience behaviour or the full onboarding flow.
---

# Integration and end-to-end testing for Atlas

The brief values "one coherent flow that genuinely works, end to end, including the parts that are
awkward". Integration tests are the proof. They should exercise real wiring: real HTTP pipeline, real SQL
Server, real serialization, and stubbed external providers.

## Stack

- `Microsoft.AspNetCore.Mvc.Testing` → `WebApplicationFactory<Program>` (add `public partial class Program;`).
- **SQL Server:** the instance from `infra/docker-compose.yml`. Each test run creates its own uniquely
  named database (e.g. `atlas_it_{guid}`), applies migrations, and drops it on dispose. Alternatively use
  Testcontainers with the allowed `mcr.microsoft.com/mssql/server:2022-latest` image. Record the choice;
  both need Docker.
- **Reset between tests:** Respawn, or a fresh DB per test class. Never depend on test order.
- **Providers:** the same stub used for local runs (see `atlas-provider-integration`), hosted in-process,
  or **WireMock.Net** for HTTP-level adapter tests (timeouts, 500s, malformed JSON, slow responses).
- **Messaging:** if a broker is used, run against the RabbitMQ from compose with per-run vhost/queue
  names, or use the library's test harness for consumer-level tests. Still cover at least one real-broker
  path.
- Mark with `[Trait("Category", "Integration")]` so unit tests can run without Docker.

## Must-have scenarios (end to end through the public API)

Each one drives the provider stub into a scenario **without editing production code**:

| Scenario | Expected |
|---|---|
| Identity VALID + face match, screening CLEAR, remote market | Approved, account opened, card ordered (or the documented equivalent) |
| Screening POSSIBLE_MATCH | Non-final status (`PendingComplianceReview`). Not rejected, no account |
| Compliance officer resolves review (clear / confirmed match) | Application continues to approve / reject |
| Identity INVALID or face mismatch | Rejected, with reason recorded, record retained |
| Identity INCONCLUSIVE | Whatever the ADR decided |
| Provider times out / 503 then recovers | Retries within budget, then success. No duplicate side effects |
| Provider down beyond the time budget | Request returns a non-final status in time. The application completes later |
| Same submission sent twice (same idempotency key) | One application, same response |
| MD application | Approved, account pending branch activation, not active |
| MF applicant with passport in lieu of national ID | Accepted |
| Invalid identifier for market (MB 12 digits, MC no letter at pos 9) | 400 ProblemDetails with field error |
| Oversized payload | 413 / documented limit |
| Reading a customer record | Access audit entry written with caller identity |

## Persistence tests

- Migrations apply cleanly to an empty database (catches drift between model and migrations).
- Concurrency: two simultaneous transitions on the same application → one wins, the other gets a
  concurrency conflict that is handled.
- Outbox: state change and outbox row commit together. If the transaction rolls back, no message is
  published.
- Market partitioning: an MB application is written only to the MB store.

## Conventions

- Arrange via the **public API or domain**, not by inserting rows directly, unless testing a read model.
- Assert on HTTP status + body contract, then on durable state (DB) where it matters.
- **No sleeps.** For async completion, poll with a bounded timeout helper
  (`await Eventually(() => ..., timeout: 10s)`) or use the test harness's "consumed" signal.
- Use `FakeTimeProvider` registered via `ConfigureTestServices` to fast-forward the 48h review deadline or
  retry backoff.
- Keep the test host's configuration explicit (`UseSetting` / in-memory config). Don't depend on the
  developer's `appsettings.Development.json`.
- The full integration suite should run with one command documented in the README, after
  `docker compose -f infra/docker-compose.yml up -d --wait`.

## Before finishing

- [ ] Suite passes twice in a row (proves isolation/cleanup).
- [ ] Suite passes with `SQL_PORT` overridden (no hard-coded `1433`).
- [ ] Each awkward path above that is in scope has a test. Those not in scope are listed in Known
      limitations.
