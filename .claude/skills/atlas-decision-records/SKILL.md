---
name: atlas-decision-records
description: How to document decisions and deliverables for the Atlas interview task: ADRs with rejected alternatives, open questions, known limitations, README run instructions, WHERE-TO-LOOK.md (half a page max), commit message conventions with real history, and interview-readiness notes. Use when making any non-trivial design decision, finishing a feature, writing or updating README/docs, preparing commits, or before submitting the task.
---

# Decisions and documentation for Atlas

The reviewers say they care about "the thinking, not the packaging", and that the interview will probe
*why* and *which alternatives were rejected*. Documentation is therefore a first-class deliverable, written
as you go and not at the end.

## Layout

```
README.md                 How to run (exact commands), what was built, what was left out
WHERE-TO-LOOK.md          ≤ half a page. Read first by reviewers
docs/decisions/NNNN-*.md  ADRs, one decision each
```

## ADR template (`docs/decisions/NNNN-short-title.md`)

```markdown
# NNNN. <Decision in a sentence>

Status: Accepted | Superseded by NNNN · Date: YYYY-MM-DD

## Context
What forced the decision. Cite sources (ATLAS-1 AC2, GC-2026-0814 §3, Annex B MD, Teams 19 Aug).

## Decision
What we do, stated plainly.

## Alternatives considered
- <Option> — why rejected (concrete, not "less clean").
- <Option> — why rejected.

## Consequences
What gets easier, what gets harder, what we now owe (follow-ups, risks, things to confirm).

## In 30 seconds
How I'd explain this out loud in the interview.
```

Write an ADR for at least: service boundaries; sync vs async response / API contract change; handling of
possible matches; MD activation; identifier model; data residency/partitioning; retention vs erasure;
broker or no broker + library; provider stand-in design; resilience/time budget; target framework.

## Open questions

Each entry: **Question**, **Who should answer** (Product/Compliance/Platform/Mobile/Legal), **Source** that
raised it, **Impact** if answered differently, **Assumption made for now**. Examples already known:
INCONCLUSIVE handling, check-digit algorithms, provider data residency, AML retention vs erasure, nationality
for MF passport holders, whether the 3-minute promise excludes manual review, Platform topology vs GC §1.

## README must have

1. Prerequisites with versions (SDK, Docker).
2. **Copy-pasteable** steps: `docker compose -f infra/docker-compose.yml up -d --wait`, migrations/DB setup if not automatic,
   start each service (or one script, with both a PowerShell and a bash variant), run tests.
3. How to drive provider scenarios (stub rules + control endpoints + `.http` files).
4. Ports and URLs (API, stubs, Seq, RabbitMQ UI).
5. What was built, what was deliberately not built and why, and links to ADRs.

Verify the README on a clean clone before submitting. The brief says: "if we have to guess at a manual step,
we will get it wrong."

## WHERE-TO-LOOK.md

Half a page maximum: the 3–4 files most worth reading (with one line each on why), **the one decision you
most want to be asked about**, and what to ignore (scaffolding, stubs, generated migrations).

## Commits

- Real, incremental history (no squashing, per the brief). Don't commit `/requirements/`.
- Small commits that each build. Imperative subject ≤ 72 chars, and a body explaining **why** when it isn't
  obvious, referencing the ADR (`See ADR-0004`).
- Conventional prefixes are optional (`feat:`, `fix:`, `test:`, `docs:`, `refactor:`). If used, be consistent.
- Separate refactors from behaviour changes.

## Interview readiness check (before submitting)

For each ADR and each file in WHERE-TO-LOOK, the author should be able to answer, without notes:
what it does, why it's built this way, what was rejected and why, how it fails, and how it would change
for production (real providers, in-country deployment, real identities).
If any answer is "the AI did that", simplify the code until it isn't.
