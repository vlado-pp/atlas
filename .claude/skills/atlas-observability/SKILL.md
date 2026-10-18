---
name: atlas-observability
description: How to add logging, tracing, metrics and health checks to Atlas services: Serilog/OpenTelemetry to Seq, structured log templates, correlation across HTTP and messages, PII-free logging, and measuring the 3-minute time-to-decision promise. Use when touching Program.cs logging setup, adding log statements, metrics, health checks or diagnosing a flow in Seq.
---

# Observability for Atlas

Seq is in `infra/docker-compose.yml` (`http://127.0.0.1:5341`), so reviewers will look at it. A reviewer should be able to
follow one application through every service in Seq by filtering on `ApplicationId`.

## Logging

- Serilog (`Serilog.AspNetCore`, `Serilog.Sinks.Seq`) or `ILogger` + OpenTelemetry exporter to Seq (Seq
  accepts OTLP). Pick one solution-wide and configure the endpoint from configuration (`SEQ_PORT` override).
- **Message templates, not interpolation:** `logger.LogInformation("Screening completed for {ApplicationId}
  with {Outcome}", id, outcome)`. For hot paths, use `LoggerMessage` source generators.
- **Enrich** every event with `Service`, `Environment`, `ApplicationId`, `Market`, `CorrelationId`/`TraceId`
  via scopes or enrichers.
- **Never log PII:** names, dates of birth, national IDs, email, phone, images, base64, provider
  request/response bodies. Log provider reference IDs (`identificationId`, `caseId`), status codes, latency.
  Add a destructuring policy or a redaction step as a safety net, and treat a PII log line as a bug.
- Levels: `Information` for state transitions (one line per transition is the audit trail operators need),
  `Warning` for retries/degraded paths, `Error` for failures needing action. Avoid log noise in loops.
- Request logging: `UseSerilogRequestLogging()` without bodies.
- The operational log is **not** the GC §2 access audit. That one is a durable, retained data record (see
  `atlas-data-persistence`).

## Tracing

- OpenTelemetry for ASP.NET Core, HttpClient, EF Core/SqlClient and the messaging library. Propagate
  `traceparent` through message headers so one trace spans submit → screening → account opening.

## Metrics that matter here

- `atlas_time_to_decision_seconds` histogram, tagged by market and outcome. This is how AC2 "under 3
  minutes" is actually measured. Report what share of applications get a final answer within the budget.
- Provider latency / error rate / circuit-breaker state per provider.
- Count of applications in `PendingComplianceReview` and their age, against the 48h expectation.
- Outbox backlog and dead-letter count.

## Health checks

`/health/live` (process up) and `/health/ready` (SQL per market partition, broker, blob, provider reachable)
using `AspNetCore.HealthChecks.*` or custom checks. Readiness failing must not take liveness with it.
