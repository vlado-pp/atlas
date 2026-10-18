---
name: atlas-messaging-workflow
description: How to implement the long-running onboarding workflow across services and time in Atlas: orchestration vs choreography, process manager/saga, transactional outbox and inbox, idempotent consumers, retries and dead-lettering, scheduled timeouts (48h compliance review), and message contract design without PII. Use when adding a broker (RabbitMQ), background workers, message contracts, consumers, the outbox dispatcher or workflow/saga state.
---

# Messaging and workflow for Atlas

Onboarding is **not** a single request: screening can take 48h (GC §3), MD activation needs a branch
visit, and providers can be down. The workflow has to survive restarts, retries and duplicate delivery.

## Decide first (ADR)

- **Do you need a broker at all?** A single service with a DB-backed job/outbox table and a
  `BackgroundService` may be enough for the scope. A broker (`rabbitmq:3-management`, allowed) is justified
  once there are multiple services reacting to the same events. Write down why.
- **Orchestration vs choreography.** In a regulated flow, a single **process manager** that owns the
  onboarding state is easier to audit and explain ("where is application X and why?"). Choreography spreads
  that state across services. Pick one and record the rejected alternative.
- **Library:** RabbitMQ.Client directly (most to explain, least magic), MassTransit **v8** (Apache-2; v9 is
  commercial), or Wolverine. Whatever you choose, you must be able to explain the outbox and retry behaviour
  it gives you.

## Non-negotiable mechanics

- **Transactional outbox:** a state change and the messages it causes are written in the **same DB
  transaction**. A dispatcher publishes them afterwards and marks them sent. Delivery is at-least-once.
- **Idempotent consumers / inbox:** record processed `MessageId`s (or use natural idempotency on state
  transitions) so redelivery is a no-op. Every handler must be safe to run twice.
- **Retries:** immediate retries for transient faults, then delayed redelivery, then **dead-letter** with
  enough context (message type, application ID, error) to replay. No PII in the dead-letter reason.
- **Poison messages** must not block the queue.
- **Ordering:** don't assume it. The aggregate's state machine rejects out-of-order transitions, and the
  consumer decides whether to drop, defer or fail.
- **Timeouts as data:** "review due by" / "retry provider at" are persisted deadlines processed by a
  scheduler or delayed message, and are testable with `FakeTimeProvider`. Don't use `Task.Delay` in a
  request path.
- **Correlation:** every message carries `ApplicationId`, `CorrelationId`/`traceparent` and `Market`.

## Message contracts

- Live in a small shared `Atlas.Contracts` project. Use immutable `record`s with past-tense names for events
  (`ApplicationSubmitted`, `ScreeningCompleted`, `ComplianceReviewResolved`) and imperative names for
  commands (`OpenAccount`, `OrderCard`).
- **No PII or images in messages.** Carry IDs and outcomes, and let the consumer read details from the market
  partition it is authorised for. This supports data residency (GC §1) and erasure (GC §5).
- Version additively: add optional fields and never repurpose or remove them. Put a `SchemaVersion` on the
  envelope if needed.
- Messages that leave a market partition carry only non-personal data.

## Tests

- Unit: process manager transitions per incoming event, including duplicates and out-of-order events.
- Integration: outbox commit/rollback, consumer idempotency (deliver twice → one effect), dead-letter after
  max retries, 48h timeout fires via fake clock, end-to-end via real broker for at least the happy path and
  the possible-match path.
