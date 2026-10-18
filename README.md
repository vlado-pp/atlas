# Atlas: digital onboarding backend

One coherent onboarding flow, end to end, including the awkward parts:
- providers that are slow, flaky or down
- possible sanctions matches that need a human
- MD's in-person activation
- MF residents without a national ID
- data that must stay in its country
- every access to personal data attributed and logged

**Start with [WHERE-TO-LOOK.md](WHERE-TO-LOOK.md).** The reasoning is in [docs/decisions/](docs/decisions/) (one ADR
per decision, each with the alternatives I rejected).

## Run it

**Prerequisites:** .NET 10 SDK, Docker.

```powershell
./scripts/run.ps1          # Windows. If scripts are blocked: powershell -ExecutionPolicy Bypass -File scripts/run.ps1
./scripts/run.sh           # macOS / Linux / Git Bash
```

The script does three things:
1. Starts the infrastructure: `docker compose -f infra/docker-compose.yml up -d --wait`.
2. Builds the solution.
3. Runs the three services on the host. Each service creates and migrates its schema in all six market databases on first start.

Ctrl+C stops the services.

| | URL |
|---|---|
| Onboarding API (mobile-facing) | http://127.0.0.1:5100 (Swagger UI: http://127.0.0.1:5100/swagger, OpenAPI: `/openapi/v1.json`) |
| Accounts service | http://127.0.0.1:5200 (health only; works off the broker) |
| Provider stubs (IDNow, World-Check) | http://127.0.0.1:5300 |
| Seq (logs) | http://127.0.0.1:5341 |
| RabbitMQ management | http://127.0.0.1:15672 (guest / guest) |

**Try it:** open [requests/onboarding.http](requests/onboarding.http) (VS Code REST Client or Visual Studio) and run the
scenarios: happy path, possible match, confirmed match, MD branch visit, MF passport, IDNow outage, forged
document, validation, idempotent replay. Use [requests/stubs.http](requests/stubs.http) to play the compliance
officer or to break or recover a provider. Follow any application in Seq by filtering on
`ApplicationId = 'mb-…'`.

**Authentication (development only):** use `Authorization: Bearer dev:{customer|staff|service}:{id}`, e.g.
`dev:customer:device-1` or `dev:staff:teller-12`. Every request carries an individual identity into the access
log (ADR-0010). Outside Development the services refuse to start until a real OIDC scheme is configured.

### Tests

```powershell
dotnet test tests/Atlas.Onboarding.UnitTests    # 96 tests, no infrastructure needed, ~1s
dotnet test tests/Atlas.IntegrationTests        # 18 end-to-end tests, needs the Docker infrastructure up, ~20s
```

The integration tests host all three services in-process, against the real SQL Server, RabbitMQ and Azurite.
Each run creates its own databases, queues and containers, and deletes them afterwards.

### Ports already taken?

Compose ports are overridable (`SQL_PORT`, `RABBITMQ_PORT`, `AZURITE_PORT`, `SEQ_PORT`). If you change one, tell the
services via environment variables, for example:

```powershell
$env:SQL_PORT=14330; docker compose -f infra/docker-compose.yml up -d --wait
$env:Persistence__SqlServer="Server=127.0.0.1,14330;User Id=sa;Password=Local_Dev_Password_1;TrustServerCertificate=True;Encrypt=False"
$env:RabbitMq__Port=5673   # if RABBITMQ_PORT was changed
./scripts/run.ps1 -NoInfra
```

All addresses use `127.0.0.1` rather than `localhost` on purpose: on some Windows machines another process
listens on `[::1]` for the same ports.

## What I built

```
 mobile ──HTTP──► Onboarding service ─────────HTTP (typed clients + Polly)──────► IDNow / World-Check
                  │  public API (202 + status)                                    (stub service, port 5300)
                  │  Application state machine
                  │  workflow runner (DB-driven, leases)
                  │  audited customer-record store ◄── HTTP, own service identity ──┐
                  │  outbox ─► RabbitMQ ─► Accounts service ── core banking / card  │ (fakes behind seams)
                  │         ◄─ RabbitMQ ◄─ outbox                                    │
                  ▼                                                                   │
        SQL: one database per market (ATLAS_MA … ATLAS_MF), one schema per service ──┘
        Blob: one container per market (Azurite)
```

**Services (ADR-0002):**
- **Onboarding** owns the application and the whole decision: identity verification, then screening, then review or branch gate, then account opening. One decision lives in one place.
- **Accounts** is the seam to core banking and the card processor.
- **Provider stubs** speak the documented IDNow and World-Check wire contracts. They are not part of the product.

**The flow:**
1. `POST /applications` validates per market, stores the images in the market's blob container, and writes the application, the personal data, an access-log entry and an idempotency record in one transaction. It returns `202` with a `Location`.
2. The workflow runner claims due applications from every market database and advances each one a step:
   - IDNow, then World-Check.
   - A possible match goes to `UNDER_REVIEW`, and the case is polled until a named officer decides.
   - In MD, the application moves to `AWAITING_BRANCH_VISIT` until branch staff record the signature.
   - Otherwise it moves to account opening.
3. Account opening is a message (outbox → RabbitMQ). Accounts reads the profile from Onboarding under its own identity (audited), opens the account, orders the card, and replies with a message. The application becomes `APPROVED`.

**Where I deliberately departed from the ticket.** Each has an ADR:

| Ticket says | What Atlas does | Why |
|---|---|---|
| One POST, `APPROVED`/`REJECTED` in < 3 min, no polling | `202` + `GET /applications/{id}` + status events for push ([0003](docs/decisions/0003-asynchronous-application-api.md)) | Some answers legally take up to 48h |
| No human intervention (AC3); Product: "just reject" possible matches | Possible matches wait for a compliance officer and are never decided automatically, not even after 48h ([0005](docs/decisions/0005-possible-matches-go-to-compliance-review.md)) | GC-2026-0814 §3 |
| Account open and card ordered when the call returns (AC6) | MD waits for a branch visit, configured per market ([0007](docs/decisions/0007-market-activation-policy-md-branch-visit.md)) | Annex B, no exemption |
| `nationalId` is a required string | Per-market identifier rules; `passportNumber` + `nationality` for MF ([0008](docs/decisions/0008-market-configuration-and-identifiers.md)) | Annex B, Teams 29 Jul |
| "Build in retries" | Short in-call retries plus durable backoff in the workflow; a provider outage never rejects a customer ([0004](docs/decisions/0004-workflow-execution-and-messaging.md), [0006](docs/decisions/0006-identity-verification-outcomes.md)) | Retries must not break the time budget or lose applications |
| One region, one SQL, shared bus (Platform) | Every store partitioned by market and routable by the application ID ([0009](docs/decisions/0009-data-residency-partition-per-market.md)) | GC §1 data residency |

The other decisions:
- [Attributable access logging](docs/decisions/0010-access-audit-and-identities.md)
- [Retention vs erasure](docs/decisions/0011-retention-and-erasure.md)
- [Provider stand-ins](docs/decisions/0012-provider-stand-ins.md)
- [.NET 10](docs/decisions/0001-target-net-10.md)

## What I left out

The main gaps:
- The erasure endpoint and purge job (the data model is ready for them).
- Real authentication.
- A push-notification consumer.
- Check-digit algorithms (not supplied).
- OpenTelemetry export.
- The resumable upload API proposed to mobile.

## Layout

```
src/BuildingBlocks/  Atlas.Contracts (messages), Atlas.BuildingBlocks (market scope, outbox/inbox, RabbitMQ, dev auth, logging)
src/Onboarding/      Atlas.Onboarding.Domain (no dependencies), Atlas.Onboarding.Api
src/Accounts/        Atlas.Accounts.Api
src/ProviderStubs/   Atlas.ProviderStubs
tests/               Atlas.Onboarding.UnitTests, Atlas.IntegrationTests
docs/                decisions (ADRs)
requests/            .http scenarios
infra/               docker-compose: SQL Server, Seq, RabbitMQ, Azurite (extends the supplied /platform)
```
