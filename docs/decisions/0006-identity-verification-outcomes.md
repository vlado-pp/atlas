# 0006. Identity verification first; mapping of IDNow outcomes

Status: Accepted · Date: 2026-10-18

## Context
IDNow returns `documentResult` (`VALID | INVALID | INCONCLUSIVE`), `faceMatch` and `confidence`. The ticket
doesn't say what to do with anything but the obvious cases, and there is no human review for identity
(only GC §3 mandates human review, and only for screening).

## Decision
**Order: IDNow, then World-Check, sequentially.**
Screening someone whose identity isn't verified wastes World-Check cases, and on a possible match it wastes
compliance officers' time on what may be a forged identity. Sequential adds a few seconds, well within
the SLO.

**Mapping (anti-corruption layer):**

| IDNow result | Domain outcome | Application |
|---|---|---|
| `VALID` + `faceMatch: true` + `confidence ≥ threshold` | `Verified` | continue to screening |
| `INVALID`, or `faceMatch: false` | `Failed` | `Rejected` (`IDENTITY_NOT_VERIFIED`) |
| `INCONCLUSIVE`, or `confidence < threshold` | `Inconclusive` | `Rejected` (`RECAPTURE_REQUIRED`), and the customer may submit a new application straight away |
| Unknown/new enum value | `Inconclusive` (logged as warning) | as above, and never a pass |
| Timeout / 5xx / 429 | not an outcome | retried by the workflow (ADR-0004), never a rejection |

- `threshold` is configuration per market (`Markets:{code}:IdentityVerification:MinConfidence`).
- The `identificationId` is stored before acting on the result, so a crash and retry does not verify twice.
- Every outcome, including rejections, is retained with its documents (GC §4).

## Alternatives considered
- **INCONCLUSIVE → human review**: contradicts "fully automated" and isn't mandated by compliance. If
  the INCONCLUSIVE rate turns out high this changes, so it should be confirmed with Product and Compliance.
- **INCONCLUSIVE → ask the customer to re-capture within the same application**: the better experience,
  but it needs a resumable API. Deferred.
- **Parallel IDNow + World-Check**: saves seconds and costs review cases on unverified identities.

## In 30 seconds
Verify who they are before asking whether they are sanctioned. Anything that isn't a clear pass is not a
pass, and a technical failure is a retry, never a decision about the customer.
