# 0008. Market configuration and the customer identifier model

Status: Accepted · Date: 2026-10-18 · Extends: ATLAS-1 request body (additively)

## Context
- `nationalId` as one required string fails (Teams 29 Jul): formats differ per market (Annex B), ME's check
  digit differs from MA/MB's, and in MF non-citizen residents (mostly refugees) have no Personal Number and
  onboard with a passport number.
- Annex B note 2: identifiers are for validation only and must never be a primary key.
- Check-digit algorithms are not given anywhere.
- World-Check needs `nationality`. Using the market code is wrong for exactly the MF passport-in-lieu
  applicants.

## Decision
**Market configuration** (`Markets` section, validated with `ValidateOnStart`):
```json
"MD": {
  "Activation": { "Mode": "InPersonBranch", "VisitDeadlineDays": 30 },
  "Identifiers": [
    { "Scheme": "CitizenNumber", "Pattern": "^[0-9]{10}$", "CheckDigit": "None" }
  ],
  "IdentityVerification": { "MinConfidence": 0.90 }
}
```
- MA/MB: `PersonalNumber` 13 digits. MC: `UnifiedCitizenId` `^[A-Z0-9]{8}[A-Z]$`. MD: 10 digits. ME:
  `CivilNumber` 10 digits. MF: `PersonalNumber` **or** `Passport`.
- Patterns and the market → algorithm assignment are configuration. Check-digit algorithms are code behind
  `ICheckDigitAlgorithm`, selected by name. All markets use `None` until the real algorithms are confirmed.
  We don't invent algorithms and present them as real.
- Validation is a **strategy per market** (`IMarketPolicy`) built from this configuration. An unknown market
  is a `400`.

**Identifier model:** a value object `CustomerIdentifier { Scheme, Value, IssuingCountry }`, stored as
attributes of the applicant. It is never a key and never unique (re-applications exist). Keys are
surrogate, time-ordered GUIDs.

**Request body: additive and backward compatible.**
- `nationalId` stays and is interpreted as the market's default scheme.
- New optional `passportNumber`, accepted only where the market allows `Passport` (MF). Exactly one
  acceptable identifier must be present.
- New optional `nationality` (ISO code). It is required when the identifier is a passport, and otherwise
  defaults to the market's country (an assumption to confirm with Compliance).

## Alternatives considered
- **`identifier: { scheme, value }` object replacing `nationalId`**: cleaner, but it breaks the mobile
  contract. It can be offered in v2.
- **Separate validators coded per market**: market rules would become code and new markets would need a
  deploy (against AC4 and Annex B note 3).
- **National ID as natural key**: forbidden by Annex B note 2 and impossible for MF.

## In 30 seconds
"Required" really means "one acceptable identifier for this market". The rules are data, the algorithms are
code we haven't been given, and the identifier is an attribute, never an identity.
