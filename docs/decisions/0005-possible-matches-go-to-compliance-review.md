# 0005. Possible sanctions/PEP matches go to compliance review, never to an automatic decision

Status: Accepted · Date: 2026-10-18 · Deviates from: ATLAS-1 AC3 and Product's "those just get rejected" (Teams 19 Aug)

## Context
- GC §3: a possible match *must* be referred to a compliance officer in the relevant market. It may take up
  to 48h and must never be automated or bypassed.
- Teams 19 Aug: World-Check puts possible matches into a manual review queue "on their side", and the
  review takes up to 48h. Product suggested auto-rejecting. Platform pointed out that is not allowed.
- AC3 ("no application requires human intervention") is therefore unachievable for a small but non-zero
  share of applicants.

## Decision
- World-Check `CLEAR` → continue. `POSSIBLE_MATCH` → the application moves to `PendingComplianceReview`
  (public: `UNDER_REVIEW`). We store only the World-Check `caseId`. The match details stay in World-Check,
  where the officer reviews them, so we don't keep another copy of sensitive data (data minimisation).
- The review is done by the market's compliance officer **in World-Check's case tooling**. The workflow
  runner polls the case (`GET` on the case, an assumed endpoint) at a configured interval
  until it is resolved:
  - `CLEARED` (false positive) → continue to account opening;
  - `CONFIRMED` → `Rejected` with the customer-safe reason `NOT_ELIGIBLE`.
  The resolution records the reviewing officer's identity and time (GC §2 attribution).
- After 48h unresolved, nothing is decided automatically. The application raises an `Overdue` flag
  (metric + warning log) for the market compliance team, and keeps waiting.
- Screening runs **after** identity verification succeeds (see ADR-0006), so officers only review cases for
  verified identities.
- The customer never learns that the reason was sanctions screening (tipping-off).

## Alternatives considered
- **Auto-reject possible matches (Product's suggestion)**: explicitly forbidden by GC §3. It would also reject
  the false positives, which are most possible matches, and damage the customer experience.
- **Auto-approve below a score threshold**: still automating the review. Forbidden.
- **Our own back-office review endpoint/UI**: duplicates World-Check's case manager, which compliance
  already uses according to Platform. A small internal `POST /internal/screening-cases/{id}/resolution`
  remains a plausible seam if Compliance prefers to decide in our system, and is listed as an open
  question.
- **Webhook instead of polling**: better latency and load. The trimmed provider docs don't show one, so
  polling is the assumption. The adapter hides the difference.

## Consequences
- AC3 is reported to Product as unachievable as written. AC2 (3 minutes) excludes these applications.
- The provider stub must be able to create a `POSSIBLE_MATCH` case and resolve it later via a control
  endpoint (ADR-0012).

## In 30 seconds
Compliance's notice outranks the ticket. A possible match parks the application, a named officer decides,
we record who and when, and the clock never decides for them, not even after 48h.
