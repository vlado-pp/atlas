---
name: atlas-requirements-guardrails
description: Domain invariants and known requirement conflicts for the Atlas onboarding backend (ticket ATLAS-1 vs Group Compliance GC-2026-0814, Annex B, and the #atlas-onboarding Teams thread). Use before designing, coding, testing or reviewing anything that touches applications, identity verification, sanctions/PEP screening, account opening, card ordering, markets, personal data or the public API, and whenever one requirement seems to contradict another.
---

# Atlas requirements guardrails

The material in `/requirements/` contradicts itself. Working out what Atlas *should* be is most of the task.
This skill records the source hierarchy, the conflicts already found, and the invariants that code must never
violate. Re-read it before any design or implementation step.

## Source hierarchy (when sources disagree)

1. **Regulators / Group Compliance** (`compliance-notice.pdf`, `annex-b-market-requirements.pdf`). These are
   "requirements, not recommendations". They are legally binding and cannot be traded away.
2. **Facts from people who own the system** (Platform, Backend, Retail Ops MD, QA in the Teams export). These are
   observations about reality (e.g. "World-Check flags take up to 48h").
3. **Product ticket ATLAS-1**: intent and desired experience. It is authoritative on *goals*, not on facts it got
   wrong.
4. **Marketing** ("under 3 minutes" in a deck): an aspiration that has to be scoped honestly.

If a lower source conflicts with a higher one, follow the higher one and **record the deviation** in
`docs/decisions/` (see the `atlas-decision-records` skill). Never silently "fix" it.

## Known conflicts

**Decided:** these are resolved in `docs/decisions/` (ADR-0003 async API, 0005 possible matches, 0006
IDNow outcomes, 0007 MD branch activation, 0008 identifiers, 0009 residency, 0010 access audit, 0011
retention/erasure). The ADRs win over this table. If code needs to deviate from an ADR, write a superseding
ADR first.

| # | Conflict | Sources | Likely direction |
|---|---|---|---|
| C1 | "No application requires human intervention" (AC3) vs. **mandatory manual review** of any possible sanctions/PEP match, up to 48h, never automated or bypassed | AC3 vs GC §3, Teams 19 Aug | AC3 cannot hold. `POSSIBLE_MATCH` → `PendingComplianceReview`. **Never auto-reject and never auto-approve a possible match.** |
| C2 | "One call in, one answer out, no polling, no websocket", `APPROVED \| REJECTED` only, under 3 minutes | Ticket API vs C1, C4, GC §3 | A synchronous final answer is impossible for some applicants. The API needs a non-terminal status and a way to read it later (status resource and/or push notification). This is a contract change for mobile, so document it as a proposal to them. |
| C3 | "If a provider is down … build in retries" vs. the 3-minute budget | Ticket | Retries are **bounded by a time budget**. After that, the application stays durable and continues asynchronously. Don't hold an HTTP request open while you retry forever. |
| C4 | AC6 "approved customer has an open account and ordered card when the call returns" vs. **MD requires in-person branch attendance + wet signature before activation**, with no exemption | AC6 vs Annex B (MD) + note 1, Teams 22 Jul | MD gets an approved application with the account **pending branch activation**. The difference must be market *configuration/policy*, not a code fork (AC4). |
| C5 | `nationalId` is a single required string vs. different formats per market, and **MF non-citizens have no national ID** (they use a passport number) | Ticket API vs Annex B, Teams 29 Jul | Model it as an identifier value object `{ Scheme, Value }` validated by a per-market policy. "Required" means "one acceptable identifier for this market". |
| C6 | Identifier as key | Annex B note 2 | National identifiers are **never** primary keys or unique constraints. Use surrogate IDs. |
| C7 | **PII, documents and biometrics must not leave the customer's country** vs. "one region (West Europe), one SQL instance, shared Service Bus" | GC §1 vs Teams 18 Jul (Platform) | Platform's topology violates GC §1. Design data storage to be **partitioned per market** (store routed by market code) so it can be deployed in-country. Keep PII out of shared messages (pass IDs, not data). Where IDNow/World-Check process data is an open question. |
| C8 | Retain everything for 10 years (AML) vs. right to erasure on request | GC §4 vs GC §5 | AML retention normally overrides erasure while the retention period runs (a legal call, so log it as an open question). Design for both: record the erasure request, restrict processing, delete when retention expires, and keep PII separable so it can be deleted without breaking non-PII records. |
| C9 | Every access to PII must be individually attributable and logged, with the log retained as long as the record. No shared credentials for read/amend/erase | GC §2 | Write an append-only access audit (who/what system, when, which record, why). Each service gets its own identity. Don't use a shared `sa`-style login for PII paths in the design narrative (local dev compose is an accepted exception and must be stated). |
| C10 | "Save and resume, table stakes" vs. "single POST with everything in it" vs. keeping passport + selfie unencrypted on the phone for days | Teams 6 Aug | A resumable, multi-step resource (create → upload documents → submit) is the safer design. It is also a contract change for mobile, so propose it and don't impose it. |
| C11 | Base64 images "several MB each" inside a JSON POST | Teams 29 Jul | Set request size limits. Store documents in blob storage (Azurite locally) inside the market partition. Never store images in SQL rows or logs. |
| C12 | "Fully automated" vs. IDNow `INCONCLUSIVE` | Ticket provider docs | Unspecified. Decide: reject, retry/re-capture, or refer. Open question. |
| C13 | MF passport-in-lieu applicants and the sanctions check use `nationality` | World-Check request | Nationality ≠ country of residence (MF refugees). Don't fill `nationality` with the market code blindly. Open question. |

## Invariants: code must never violate these

- A sanctions/PEP `POSSIBLE_MATCH` never leads to an automatic final decision.
- No account is opened before screening has returned `CLEAR` (AC5) and identity verification has passed.
- No account is **activated** in a market whose policy requires in-person activation (MD) until that step is
  recorded.
- PII, document images and biometrics are never written to logs, exception messages, message payloads that
  leave the market partition, or test snapshots committed to the repo.
- Market differences live in configuration/policy objects. There is never a `if (market == "MD")` scattered
  through business logic.
- National identifiers are validated per market and never used as keys.
- Every read/amend/erase of a customer record goes through a path that writes an access audit entry.
- Every application, including rejected ones, is retained with its supporting documents (10y). "Delete" in
  the domain means erasure-with-retention semantics, never a cascade `DELETE`.
- External provider calls are idempotent from our side (we can safely retry without double-screening or
  double-opening an account).

## Market reference (Annex B, as at March 2026)

| Market | Remote identification | Activation | Local identifier |
|---|---|---|---|
| MA | Permitted | Remote | Personal Number, 13 digits |
| MB | Permitted | Remote | Personal Number, 13 digits |
| MC | Permitted | Remote | Unified Citizen ID, 9 alphanumeric chars, letter in position 9 |
| MD | Application stage only | **In-person branch + wet signature** | Citizen Number, 10 digits |
| ME | Permitted | Remote | Civil Number, 10 digits, check digit differs from MA/MB |
| MF | Permitted | Remote | Personal Number, **not issued to non-citizen residents → passport number accepted** |

The check-digit algorithms are **not given**. Don't invent them and present them as real. Validate length and
character class, put the check-digit algorithm behind a per-market seam, and list it as an open question.
Adding a market requires a reissued annex (note 3), so a new market should be a configuration change plus
an ADR, never a code change.

## When working on a task

1. Name which conflicts/invariants above the task touches.
2. If the task would require breaking an invariant, stop and raise it. Don't work around it.
3. If you find a new ambiguity, record it in the relevant ADR with source, impact and the assumption made.
4. If a decision is made, write or update an ADR with the alternatives you rejected.
