# Where to look

**Read these four:**

1. [Application.cs](src/Onboarding/Atlas.Onboarding.Domain/Applications/Application.cs): the onboarding state machine. The compliance rules are invariants here: no automatic decision on a possible match, the MD branch gate, and "a provider outage is never a rejection". It has no infrastructure dependencies; [ComplianceReviewTests](tests/Atlas.Onboarding.UnitTests/Domain/ComplianceReviewTests.cs) pins down the review rules.
2. [ApplicationWorkflow.cs](src/Onboarding/Atlas.Onboarding.Api/Workflow/ApplicationWorkflow.cs) and [WorkClaims.cs](src/Onboarding/Atlas.Onboarding.Api/Workflow/WorkClaims.cs): how the async flow survives restarts, three replicas and outages. The database is the scheduler (lease by `UPDATE … READPAST … OUTPUT`) and the aggregate is the process manager.
3. [AuditingCustomerRecords.cs](src/Onboarding/Atlas.Onboarding.Api/CustomerRecords/AuditingCustomerRecords.cs) and [MarketScope.cs](src/BuildingBlocks/Atlas.BuildingBlocks/Markets/MarketScope.cs): data residency and attributable access. Every unit of work is bound to one market and one actor, and personal data is only reachable through the audit decorator.
4. [OnboardingFlowTests.cs](tests/Atlas.IntegrationTests/OnboardingFlowTests.cs): the awkward paths, end to end, against real SQL, RabbitMQ and blob storage.

**The decision I most want to be asked about:** not building the API the ticket specified
([ADR-0003](docs/decisions/0003-asynchronous-application-api.md), together with
[ADR-0005](docs/decisions/0005-possible-matches-go-to-compliance-review.md)). Compliance makes "one call, final
answer in 3 minutes, no humans" impossible for some customers, so I changed the contract instead of auto-rejecting
them, and wrote it up as a proposal to the mobile team.

**Safe to skim or ignore:**
- `Persistence/Migrations/` (generated).
- The RabbitMQ plumbing in `BuildingBlocks/Messaging`. It is standard confirm/ack code; the interesting part is the outbox in `BuildingBlocks/Outbox`.
- The internals of `src/ProviderStubs`. Only its rules and control API matter, documented in [requests/stubs.http](requests/stubs.http).
