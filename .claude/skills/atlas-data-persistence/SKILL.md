---
name: atlas-data-persistence
description: How to model and persist Atlas onboarding data with EF Core and SQL Server under compliance constraints: surrogate keys (never national IDs), per-market data partitioning for residency, separating PII for erasure vs 10-year retention, append-only access audit log, document storage in blob not SQL, concurrency, migrations. Use when designing the data model, writing DbContexts, entity configurations, migrations, repositories or queries, or anything that stores or reads customer data or documents.
---

# Data and persistence for Atlas

Sam (Backend): *"ping me before you commit to the data model, there are a few things in here that look
simple and aren't."* He is unavailable, so this skill lists those things. Every data-model decision gets an
ADR.

## The things that look simple and aren't

1. **Identity of a customer.** National IDs are not universal (MF passport-in-lieu), not unique across
   markets, and Annex B note 2 forbids using them as keys. Use surrogate keys
   (`ApplicationId`, `PartyId`). Store identifiers as `{ Scheme, Value, IssuingCountry }`. Index for lookup
   only if needed, and never as a unique constraint across markets.
2. **Application vs person.** One person can apply more than once (rejected, then re-applies). Each
   application is retained for 10 years independently (GC §4). Decide whether `Party` is a separate
   aggregate and record why.
3. **Data residency (GC §1).** PII, documents and biometrics stay in the customer's country. Partition by
   market: a store (connection string / database / schema) **per market**, chosen by a resolver keyed on
   `MarketCode`. Locally, simulate with one database per market on the compose SQL Server (e.g.
   `atlas_ma` … `atlas_mf`). Any global/shared store holds only non-personal data (IDs, statuses, timestamps).
4. **Retention vs erasure (GC §4 vs §5).** Keep PII in its own tables (or encrypted with a per-subject key so
   crypto-shredding is possible), separate from the non-personal workflow record. An erasure request is
   recorded as data: it restricts processing now and deletes when retention ends, or immediately where no
   AML obligation applies. That legal call is an open question. Store `RetainUntil` explicitly. No cascade
   deletes on customer data.
5. **Access audit (GC §2).** Write an append-only `AccessLog`: `{ Id, OccurredAt, Actor (user or service
   identity), ActorType, Action (Read/Amend/Erase), SubjectRecordId, Purpose, CorrelationId }`. It is retained as
   long as the record it describes, lives in the same market partition, has no update/delete path in
   code, and is written by a decorator/interceptor on the PII access path so it can't be forgotten.
6. **Documents.** Base64 images are several MB. Store them in blob storage (Azurite locally) inside the
   market partition, referenced by a `DocumentId` + content hash + type. Never store them in SQL rows, logs or
   messages. Delete blobs as part of erasure.
7. **Status history.** Keep a status-transition history (who/what, when, why, provider reference) as well as
   the current status. Compliance will ask "why was this rejected?".

## EF Core rules

- One `DbContext` per service (and per market partition at runtime). Configuration via
  `IEntityTypeConfiguration<T>` classes, not attributes on domain entities.
- Map value objects with `ComplexProperty` (EF 8+) / owned types / value converters. Keep the domain free of
  EF attributes.
- **Optimistic concurrency** on the application aggregate (`rowversion`). Handle
  `DbUpdateConcurrencyException` deliberately.
- Keys: `Guid` with EF's sequential client-side generation (or `Guid.CreateVersion7()` on .NET 9+), or a
  typed ID struct with a converter. No `int` identity leaking into the API.
- Use `AsNoTracking()` for reads. Project to DTOs in queries. No lazy loading.
- Enums stored as strings for readability in a 10-year archive.
- Use `datetimeoffset` (UTC) for timestamps, taken from `TimeProvider`.
- **Migrations** are committed. Provide one documented way to apply them (startup in Development, or a
  script/`dotnet ef database update` per market DB). Production-style auto-migrate on startup must be a
  conscious, documented choice.
- No generic repository on top of EF. Use a repository per aggregate only if it buys something (e.g.
  enforcing audit), otherwise use the `DbContext` in handlers.
- The outbox table lives in the same DbContext/transaction as the aggregate.

## Local infrastructure

- Connection strings come from configuration, so `SQL_PORT` overrides work. The compose `sa` login is a
  local-dev convenience. State in the README that production uses a distinct identity per service (GC §2).
- If you add Azurite/Redis/RabbitMQ, add them to `infra/docker-compose.yml` with health checks.

## Tests

- Integration: migrations on empty DB, concurrency conflict, outbox atomicity, market routing (MB data only
  in MB store), audit entry on PII read, erasure behaviour within and after retention.
