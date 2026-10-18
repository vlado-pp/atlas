# 0007. In-person activation as a market policy (MD)

Status: Accepted · Date: 2026-10-18 · Deviates from: ATLAS-1 AC6 for MD

## Context
Annex B: in MD, remote identification is permitted for the application stage only. The applicant must
attend a branch and give a wet signature before the account may be activated. The exemption requested in
2024 is still outstanding and "no relief should be assumed" (Annex B note 1, Teams 22 Jul). Product wants
one codebase with market differences as configuration (AC4).

## Decision
- Each market has an `Activation` policy in configuration: `Remote` (MA, MB, MC, ME, MF) or `InPersonBranch`
  (MD).
- For `InPersonBranch`, after identity and screening have passed, the application moves to
  `AwaitingBranchVisit` (public `AWAITING_BRANCH_VISIT`) **instead of** requesting account opening.
- Branch staff complete it through `POST /internal/applications/{id}/branch-activation` with their
  individual identity, the branch code and a reference to the signed form. That request is audited (GC §2),
  and the workflow then continues with account opening + card ordering, exactly as for remote markets.
- We **do not open the account until the branch visit**. The flow after the gate is identical for every
  market, so there is one code path with a configurable gate in front of it.
- An unvisited application expires after `Markets:MD:Activation:VisitDeadlineDays` (default 30, an
  assumption) → `Expired`. The record is retained (GC §4).
- When the exemption arrives, MD becomes a configuration change: `Activation: Remote`.

## Alternatives considered
- **Open the account inactive at approval, activate at the branch**: gives the branch a ready account
  number, but leaves dormant accounts for everyone who never visits and needs a cleanup process in core
  banking. It also creates a second account-opening path that only MD uses.
- **Exclude MD from v1 ("phase two", Teams 22 Jul)**: AC1 and AC4 want all six markets, and supporting MD
  costs one state and one endpoint.
- **Market-specific code branch (`if market == MD`)**: the card-portal mistake the ticket says it won't repeat.

## In 30 seconds
MD's rule is a gate, not a different process. The gate is configuration, a named branch employee opens it,
and the moment the regulator grants the exemption we flip a setting.
