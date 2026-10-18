# 0004. DB-driven workflow in Onboarding, outbox + RabbitMQ between services

Status: Accepted · Date: 2026-10-18

## Context
After the 202 (ADR-0003), onboarding runs for seconds (happy path), hours (provider outage) or days
(compliance review, branch visit). It must survive restarts, run safely on three replicas (Teams 18 Jul),
never repeat a side effect, and be explainable to an auditor ("where is application X and why?").

## Decision
**Inside Onboarding: orchestration by the aggregate, driven from the database.**
- The `Application` aggregate holds its state and a `NextAttemptAt` timestamp.
- A `BackgroundService` (the *workflow runner*) in each Onboarding replica claims due applications per
  market database with **one atomic statement**: `UPDATE … WITH (UPDLOCK, READPAST, ROWLOCK) SET NextAttemptAt
  = now + lease OUTPUT inserted.Id` (`WorkClaims.cs`). Rows another replica is claiming are skipped, and the
  claimed rows are hidden from everyone else for the lease (2 min). No transaction stays open while providers
  are called. If a replica dies mid-step, the lease expires and another replica picks the application up.
  The runner then runs the **next step for the current state**, records the result as a state transition
  and commits, with an optimistic concurrency check (rowversion) so a late writer cannot overwrite a newer
  state. Steps are:
  `VerifyIdentity → Screen → (await review) → (await branch visit) → RequestAccountOpening`.
- A failed provider call (after the in-call resilience pipeline) only reschedules `NextAttemptAt` with
  capped exponential backoff. Transient technical failure **never** produces a rejection. Once the
  configured threshold is exceeded, the application is flagged in metrics and logs for operations.
- Waiting for World-Check case resolution is the same mechanism: the runner re-checks the case at an
  interval (ADR-0005).
- All time comes from `TimeProvider`, so the whole workflow is testable with a fake clock.

**Between services: transactional outbox → RabbitMQ → inbox.**
- When Onboarding decides to open an account, it writes an `OpenAccountRequested` outbox row in the same
  transaction as the state change. An outbox dispatcher publishes to RabbitMQ with publisher confirms and
  marks the row sent.
- Accounts consumes with manual acks, records the `MessageId` in an inbox table in the same transaction as
  its effect (idempotent), and publishes `AccountOpened` / `CardOrdered` via its own outbox. Onboarding
  consumes those and moves the application to `Approved`.
- Retries: a bounded number of redeliveries, then a dead-letter queue (DLX) with the application ID and
  error type, and no PII.
- Messages carry IDs, market and outcomes only, never PII (ADR-0009).
- Implemented directly on `RabbitMQ.Client`: about 200 lines that I can explain completely.

## Alternatives considered
- **Broker messages for internal steps too**: needs delayed redelivery for backoff and for "check the
  case again in 30 min". `rabbitmq:3-management` has no delayed-message plugin, and delays via TTL +
  dead-letter queues are awkward. Steps that touch only our own DB don't need a broker.
- **MassTransit**: v9 is commercial and v8 support ends with 2026. It gives a saga, outbox and retries
  for free, but hides exactly the mechanics the interview will ask about. Rejected for this exercise; it
  would be a reasonable choice in a team that already runs it.
- **Hangfire/Quartz for scheduling**: an extra dependency and its own tables for what one `SELECT … WITH
  (UPDLOCK, READPAST)` loop does.
- **Choreography (each service reacts to the previous one's events)**: the onboarding state would be
  spread across services. In a regulated flow, one owner of "where is this application" is worth more.
- **In-memory `Channel<T>` / `Task.Run` after the 202**: not durable. A restart loses applications.
- **Synchronous HTTP from Onboarding to Accounts**: couples availability. An Accounts outage would stall
  approvals with no durable retry.

## Consequences
- At-least-once everywhere, so every handler and the core-banking seam must be idempotent (keyed by
  `ApplicationId`).
- Polling latency adds up to one poll interval per step. With a ~1s interval on the happy path this is
  well inside the 3-minute SLO. The interval is configuration.
- Workflow logic is plain C#, unit-testable without infrastructure.

## In 30 seconds
The aggregate is the process manager and the database is the scheduler. Row locks make three replicas
safe, and a fake clock makes it testable. The broker is used only where a message really crosses a
service boundary, and there the outbox guarantees that a state change and its message are never split.
