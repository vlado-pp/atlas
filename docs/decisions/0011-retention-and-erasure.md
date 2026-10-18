# 0011. Ten-year retention takes precedence over erasure; PII kept separable

Status: Accepted (data model); erasure endpoint deferred · Date: 2026-10-18

## Context
GC §4: keep onboarding records, including rejected applications and all documents, for ten years. GC §5:
delete personal data on an erasure request. Under GDPR, an erasure right does not apply where processing is
necessary to comply with a legal obligation (Art. 17(3)(b)), which AML retention normally is. This needs
confirmation from Legal.

## Decision
- Every application gets `RetainUntil = SubmittedAt + 10 years` (configurable per market in case local AML
  periods differ).
- Applicant PII lives in its own table (`onboarding.Applicants`) and documents in blob storage. The
  workflow record (`onboarding.Applications`, status history, provider references) holds no PII beyond the
  surrogate key. So erasure deletes PII and documents without breaking the non-personal audit trail.
- An erasure request is **recorded** (`ErasureRequests`). While within `RetainUntil`, the applicant is marked
  *restricted*: no further processing, and reads require a stated purpose and are audited. At
  `RetainUntil`, a purge job deletes PII + blobs and keeps the anonymised record and access log.
- No cascade deletes on customer data anywhere.

## Alternatives considered
- **Erase immediately on request**: breaks AML retention (GC §4).
- **Ignore erasure for 10 years**: the restriction-of-processing obligation still applies, and the
  request must be recorded and honoured at expiry.
- **Crypto-shredding (per-subject encryption keys)**: elegant for backups. Deferred, because it needs a key
  store, which is out of scope locally. Listed as the production direction.

## Consequences
For v1 the data model, `RetainUntil` and the PII separation are in place. The erasure endpoint and purge job
are not built yet.

## In 30 seconds
Both rules hold if the PII can be removed without removing the record. That is a data-modelling decision you
have to make on day one, not a feature you add later.
