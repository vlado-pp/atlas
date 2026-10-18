# 0003. Asynchronous application API

Status: Accepted · Date: 2026-10-18 · Deviates from: ATLAS-1 "API" section

## Context
The ticket asks for one POST returning `201 { status: APPROVED | REJECTED }` within 3 minutes, with no
polling and no websocket. That cannot be honest for every applicant:
- a possible sanctions/PEP match requires human review, up to 48h (GC §3, Teams 19 Aug);
- MD requires a branch visit and wet signature before activation (Annex B);
- providers can be slow or down, and the ticket itself asks for retries.

A synchronous request would also hold a connection (and several MB of base64) open while retrying
providers, and a mobile network drop mid-request ("train in a tunnel", Teams 6 Aug) would lose the answer.

## Decision
- `POST /applications` keeps the ticket's request body (with the additions in ADR-0008). It validates,
  stores the documents, persists the application and returns **`202 Accepted`** with
  `{ applicationId, status }` and a `Location: /applications/{id}` header. Processing continues in the
  background (ADR-0004).
- `GET /applications/{id}` returns the current public status.
- An **`Idempotency-Key`** header is required on POST. A retry after a dropped connection returns the original
  response and never creates a second application.
- Public statuses: `IN_PROGRESS`, `UNDER_REVIEW`, `AWAITING_BRANCH_VISIT`, `APPROVED`, `REJECTED`.
  - `APPROVED` keeps the ticket's meaning (AC6): the account is open and the card ordered.
  - `UNDER_REVIEW` deliberately does not say "sanctions" (to avoid tipping off a possibly sanctioned
    person), and `REJECTED` carries only a customer-safe reason code.
- A notification seam (`ICustomerNotifier`, stubbed) is called on every terminal or customer-actionable
  status change, so mobile can rely on a push notification rather than tight polling. Polling `GET` is the
  fallback (for example when the app comes back to the foreground).
- The "under 3 minutes" promise becomes an **SLO on the automated path**: time from submit to a final
  status for applications that need no human step, measured as a histogram (see the observability skill).

## Alternatives considered
- **Ticket as written (sync 201)**: impossible to keep honest for possible matches and MD. It would force
  auto-rejecting possible matches, which GC §3 forbids.
- **Hybrid: hold the POST open for N seconds and return the final answer if ready, else 202**: keeps the
  happy path at one call. Rejected for v1: with three replicas the step may finish on another node, so the
  request would have to poll the DB anyway, and two response shapes for one endpoint make the mobile code
  harder. This is easy to add later as a compatible enhancement if mobile wants it.
- **Websocket / SSE**: mobile explicitly declined these.
- **Fully resumable multi-step API (create → upload documents → submit)**: the best answer to "save and
  resume". It is a larger contract change, so it is left as a proposal for the mobile team instead of
  being imposed.

## Consequences
- This is a contract change for the mobile team. It is written up as a proposal for them, not delivered
  silently.
- Product must reword "under 3 minutes" for the board demo (AC2 holds for the automated path only).
- Every step after the POST must be durable and retryable (ADR-0004).

## In 30 seconds
Some customers legally cannot get an answer in 3 minutes, so an API that promises one forces us to lie or
break compliance. 202 plus a status resource, idempotent submit and a push notification keeps the mobile
experience close to "one call" without lying.
