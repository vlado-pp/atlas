---
name: atlas-provider-integration
description: How to integrate external providers (IDNow identity verification, Refinitiv World-Check sanctions/PEP screening, and the core-banking/card seams) behind anti-corruption adapters, with resilience (timeouts, bounded retries, circuit breaker, idempotency) and scenario-driven stand-ins that reviewers can steer without editing code. Use when writing provider clients, stub/fake providers, resilience policies, or anything that calls an external system.
---

# Provider integration for Atlas

The brief: *"Your job is to decide when to call them, what to do with the answer, and what happens when
the answer is slow, unhelpful or doesn't arrive"* and *"whether we can drive their different responses
through your stand-in without editing your code."* Both lines are evaluated.

## Shape

```
Application layer:   ISanctionsScreeningProvider.ScreenAsync(ScreeningRequest, ct) → ScreeningOutcome
                     IIdentityVerificationProvider.VerifyAsync(...)              → IdentityCheckOutcome
                     IAccountOpening / ICardOrdering (core-banking seam)
Infrastructure:      WorldCheckClient (typed HttpClient) + WorldCheck DTOs + mapper
                     IdNowClient      (typed HttpClient) + IdNow DTOs + mapper
Stand-in:            separate runnable stub host (e.g. Atlas.ProviderStubs), speaking the providers' wire format
```

- Provider DTOs mirror the documented contract exactly (`documentResult`, `faceMatch`, `confidence`,
  `status`, `matches[]`) and **live only in Infrastructure**.
- The mapper is the anti-corruption layer. It turns provider vocabulary into domain outcomes, handles
  unknown enum values defensively (unknown → treat as *not clear*, never as pass), and applies configurable
  thresholds (e.g. minimum `confidence`).
- Production code talks to the stub **over HTTP via the same typed client** it would use against the real
  provider. Only the base URL differs (configuration). This makes the stub a real test of the client,
  serializer and resilience pipeline.

## Driving scenarios without code edits

Give the stub at least two mechanisms and document both in the README:

1. **Deterministic rules from input data**, configured in the stub's `appsettings.json`, e.g. last name
   `PossibleMatch*` → `POSSIBLE_MATCH`, `Timeout*` → hangs past the client timeout, `Flaky*` → 503 twice then
   200, a document image marker → `INVALID` / `INCONCLUSIVE`. Easy to use from curl/`.http` files.
2. **Runtime control endpoint** on the stub (`POST /_scenarios` with `{ match, response, latencyMs,
   failTimes }`), so a reviewer can change behaviour without restarting.

Also model World-Check's **asynchronous review**: a `POSSIBLE_MATCH` case that resolves later (via callback
or a pollable case endpoint, matching the decision in the ADR), plus a control endpoint to resolve it
(`/_cases/{caseId}/resolve`) that simulates the compliance officer's decision.

Provide a `requests/*.http` file per scenario so reviewers can click through them.

## Resilience rules

Use `Microsoft.Extensions.Http.Resilience` (`AddResilienceHandler` / `AddStandardResilienceHandler`) and tune
it explicitly. Don't rely on defaults you can't explain.

- **Timeouts:** per-attempt timeout and a total timeout derived from the request's **time budget** (the
  3-minute promise minus work already done). Pass `CancellationToken`s through.
- **Retries:** only on transient failures (timeouts, 408, 429 honouring `Retry-After`, 5xx, connection
  errors). Use exponential backoff with jitter and a small attempt count. **Never retry 4xx validation errors.**
- **Circuit breaker:** stops hammering a provider that is down and fails fast so the application can be parked.
- **When the budget runs out:** the application is persisted in a waiting state and continued by a
  background worker/message. The HTTP caller gets an honest non-final status. "Provider down" must not mean
  "customer lost" and must not mean "request held open for minutes".
- **Idempotency:** send a stable idempotency/reference key derived from our application ID, and persist the
  provider's `identificationId` / `caseId` *before* acting on the result, so a retry after a crash doesn't
  screen twice or open two accounts. The core-banking seam must accept an idempotency key.
- **Hedging:** don't use it against providers that bill per call or create cases.

## Data protection at the boundary

- Send providers only the fields their contract needs.
- Images go from blob storage straight to the provider request. Never log request/response bodies. Log
  provider IDs, status codes, latency.
- Calls are made from within the customer's market partition (GC §1). Note in open questions that provider
  data residency needs confirmation.

## Tests

- Unit: mapper tables (every enum value, unknown values, threshold edges).
- Integration (WireMock or the stub): timeout → retry → success, 503 storm → circuit opens, 429 with
  `Retry-After`, malformed JSON, slow response beyond budget → application parked, duplicate call
  suppressed by idempotency.
