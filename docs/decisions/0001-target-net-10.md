# 0001. Target .NET 10 (LTS)

Status: Accepted · Date: 2026-10-18

## Context
The brief accepts .NET 8, 9 or 10 and calls .NET 8 "the safest default" because production runs it.
.NET 8 support ends on 10 November 2026, a few weeks after this task. .NET 10 is the current LTS
(supported to November 2028).

## Decision
All projects target `net10.0`, set once in `Directory.Build.props`. The SDK is pinned in `global.json`.

## Alternatives considered
- **.NET 8**: closest to production, but a greenfield system would go live on a runtime that is about to
  lose support, and production has to migrate anyway. Nothing in the design depends on .NET 10, so moving
  down to 8 means changing the TFM plus two small APIs (`Guid.CreateVersion7`, built-in OpenAPI).
- **.NET 9**: STS, and its support also ends in November 2026. No advantage.

## Consequences
- Reviewers need the .NET 10 SDK (the brief says they can run all three).
- We can use `Guid.CreateVersion7()` for time-ordered keys and `Microsoft.AspNetCore.OpenApi`.

## In 30 seconds
Building a new product on a runtime that dies next month would be the first thing to undo. The cost of
going back to 8 is small and I've kept it that way.
