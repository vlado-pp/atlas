---
name: atlas-api-design
description: How to design and implement the Atlas public HTTP API consumed by the mobile app: resource and status design given the ticket's single-POST contract, honest non-final states, idempotency keys, ProblemDetails errors, validation per market, large document uploads, versioning, OpenAPI and .http samples. Use when adding or changing endpoints, request/response DTOs, status codes, validation or API documentation.
---

# API design for Atlas

The ticket says: *"the mobile team is waiting on this so please don't redesign it without talking to
them."* Treat the existing contract with respect. Where the requirements make it impossible (C1, C2, C4,
C10, C11 in `atlas-requirements-guardrails`), write the change up as a **proposal to mobile** in the docs and
keep it as close to their shape as possible. Possible approaches (decide in an ADR):

- **Compatible extension:** keep `POST /applications` with the same body. Return `201` with
  `status: APPROVED | REJECTED` when known within budget, and a new non-final status (e.g. `PENDING_REVIEW`,
  `PENDING_BRANCH_ACTIVATION`) otherwise, plus `GET /applications/{id}` and a `Location` header. Mobile
  learns new enum values and gets a status endpoint (and/or push notification, which is not polling a socket).
- **Resumable flow:** `POST /applications` (details) → `PUT /applications/{id}/documents/{type}` → `POST
  /applications/{id}/submit`. This solves save/resume and payload size, but it is a bigger change for mobile.

## Rules

- **Status codes are honest.** `201 Created` when the application resource is created (with `Location`).
  `202 Accepted` only if nothing durable exists yet. Never return `APPROVED` before the account exists (AC6)
  or for a possible match.
- **Idempotency:** accept an `Idempotency-Key` header on `POST`. Store key → response per client/market, and
  return the original response on replay. Mobile on a train in a tunnel *will* retry.
- **Errors:** RFC 9457 **ProblemDetails** (`AddProblemDetails()`, `IExceptionHandler`). Validation errors
  are `400` with field-keyed `errors` (`nationalId`: "MB requires 13 digits"). Return `413` for oversize
  bodies, `409` for state conflicts, `422` only if you deliberately distinguish semantic errors. Never leak
  stack traces, provider payloads or PII.
- **Validation layers:** shape/format at the edge (DTO validator), per-market rules via the market policy,
  invariants in the domain. Don't duplicate rules. The edge delegates to the same policy.
- **DTOs** are separate from domain types. Use `sealed record`s, enums serialized as strings
  (`JsonStringEnumConverter`), dates as `yyyy-MM-dd` (`DateOnly`) for date of birth, and camelCase JSON matching
  the ticket.
- **Market** comes from the request (`country`) and is validated against configured markets. Unknown market →
  `400`, not a crash.
- **Payload size:** set Kestrel/endpoint `MaxRequestBodySize` explicitly and document it. Prefer streaming or
  multipart for documents if the resumable flow is chosen. Don't buffer base64 into memory more than once.
- **Time budget:** the request carries a deadline (e.g. a configurable 2.5 min, or much less). When it
  elapses, return the current non-final state. Never time out silently at the load balancer.
- **Security:** assume mobile calls with a bearer token (stub auth locally, documented). Internal and
  back-office endpoints (compliance review resolution, record read/erase) require an individually
  attributable identity (GC §2), so even a stub must carry a user/service ID into the audit log.
- **Versioning:** URL segment or header, decided once. Additive changes only within a version.
- **Docs:** OpenAPI (Swashbuckle on .NET 8, `Microsoft.AspNetCore.OpenApi` on .NET 9+) with examples, plus
  a `requests/*.http` file covering every scenario the stubs support.
- **Health:** `/health/live` and `/health/ready` (DB, broker, provider stub reachability).

## Tests

Contract-level integration tests for every status code above. Include a test that replays the same
`Idempotency-Key` and gets an identical response with a single application created.
