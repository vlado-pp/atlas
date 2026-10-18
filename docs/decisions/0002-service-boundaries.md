# 0002. Two services plus provider stand-ins

Status: Accepted · Date: 2026-10-18

## Context
The epic lists five child features (submit, IDNow, World-Check, account opening, card ordering). The brief
asks for a service-oriented backend, but values "one coherent flow that genuinely works end to end" over
"six services sketched in outline". Boundaries should follow consistency and ownership, not the feature list.

Verification, screening and the approve/reject decision form one consistency boundary: the decision is a
function of both results, and every transition must be auditable in one place (GC §2, §4). Accounts and
cards belong to a different context. They outlive onboarding and in reality sit in core banking.

## Decision

| Component | Owns | Talks to |
|---|---|---|
| **Onboarding service** (`Atlas.Onboarding.Api`) | Public API for mobile, the `Application` aggregate and its state machine, market policies, documents, IDNow + World-Check adapters, compliance-review tracking, branch-activation endpoint, access audit | IDNow / World-Check (HTTP), RabbitMQ, SQL (per market), Blob (per market) |
| **Accounts service** (`Atlas.Accounts.Api`) | Account and card records, seam to core banking and the card processor (both stubbed), idempotent `OpenAccount` / `OrderCard` | RabbitMQ, SQL (per market), Onboarding internal API (audited read of the customer profile) |
| **Provider stubs** (`Atlas.ProviderStubs`) | Not part of the product. Stands in for IDNow and World-Check over HTTP with scenario control | (none) |

Onboarding is the **orchestrator** of the onboarding process (see ADR-0004).

## Alternatives considered
- **One service per child feature (5–6 services)**: splits a single decision across the network. The
  approve/reject decision would need data from three services, which adds distributed consistency, more
  failure modes and more audit surface for no business gain. Most of these "services" would be thin
  wrappers around a provider call.
- **A separate Screening service**: the strongest candidate for a future split, because screening has its
  own lifecycle (48h cases) and the bank will need ongoing re-screening of *existing* customers, not just
  applicants. Deferred: in v1 it only serves onboarding, and splitting it now costs a day of plumbing.
  The adapter and case tracking are kept in their own folder so the split is mechanical later.
- **Modular monolith**: defensible, but the brief explicitly asks for service orientation, and
  accounts/cards is a real boundary (different owner, different system of record).
- **Choreography between more services**: rejected in ADR-0004.

## Consequences
- Cross-service communication is asynchronous (RabbitMQ + outbox). Inside Onboarding, steps are local
  transactions.
- Accounts must be idempotent per application, because messages are at-least-once.
- The reading of customer PII by Accounts is an explicit, audited system access (GC §2).

## In 30 seconds
I split where the data and the owner change, not where the ticket has bullet points. The decision needs
verification and screening together, so they live together. Accounts are a different system of record,
so they're a separate service behind a message boundary. Screening is the next candidate to split out once
re-screening of existing customers exists.
