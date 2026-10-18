# 0010. Attributable access: per-caller identity and an append-only access log

Status: Accepted · Date: 2026-10-18

## Context
GC §2: every access to customer personal data or documents must be attributable to the individual person
or system and logged, with the log retained as long as the record. No shared or generic credentials for
reading, amending or erasing a customer record.

## Decision
- **Every endpoint is authenticated** and yields an `Actor { Id, Type: Customer | Staff | Service }`.
  - Locally there is a development authentication handler (documented in the README) that reads a
    dev-only bearer token of the form `dev:<type>:<id>`. It is enabled only in Development, and a missing
    or malformed token gives `401`.
  - In production the same `Actor` comes from OIDC/JWT: customer tokens for mobile, staff SSO for
    branch/compliance, and client credentials per service.
- **Access log:** an append-only `onboarding.AccessLog` table in the market database
  `{ Id, OccurredAt, ActorType, ActorId, Action (Read|Create|Amend|Erase), RecordType, RecordId, Purpose,
  CorrelationId }`. No update or delete path exists in code. `RetainUntil` is copied from the record.
- Written by a **decorator** around `ICustomerRecordStore` (the only way to read or change applicant PII and
  documents), so a new endpoint can't forget it. The audit write is in the same transaction as the
  amend/erase. For reads, it is written before data is returned.
- System accesses count too: the workflow runner (sending data to IDNow/World-Check) and Accounts' profile
  read are logged as `Service` actors with a purpose.
- **Database credentials:** the compose `sa` login is a local convenience only. Production uses one identity
  per service (managed identity / contained users), as stated in the README.

## Alternatives considered
- **Operational logs (Seq) as the access log**: not durable or retained for 10 years, and they must not
  contain PII. The two serve different purposes.
- **SQL Server Audit / temporal tables**: they capture DB-level access by the *service's* login, not the
  end user behind the request. They could complement this design, but they don't replace it.
- **EF `SaveChanges` interceptor**: catches writes, misses reads. GC §2 says every access.

## In 30 seconds
The access log is business data, not a log line. One gate to customer data, wrapped by an audit decorator,
with a real identity on every call, including our own services.
