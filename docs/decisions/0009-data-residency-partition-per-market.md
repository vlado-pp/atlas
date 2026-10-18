# 0009. Data residency: partition every store by market

Status: Accepted · Date: 2026-10-18 · Conflicts with: Platform's single-region topology (Teams 18 Jul)

## Context
GC §1: personal data, identity documents and biometrics must not leave the customer's country of residence.
Platform says everything runs in West Europe with one SQL instance and one shared Service Bus namespace.
Both can't be true. Compliance's position is a requirement, and the topology is an infrastructure choice.

## Decision
- **SQL:** one database per market (`atlas_ma` … `atlas_mf`). Each service owns its own schema within it
  (`onboarding.*`, `accounts.*`). A `IMarketDbContextFactory` resolves the connection string from the
  market. In production each market's connection string points to an in-country server, with no code change.
- **Documents:** blob storage, one container per market (`documents-mb`) in Azurite locally, one storage
  account per country in production. Base64 is decoded once and streamed to blob at submit. Images are never
  stored in SQL, logs or messages.
- **Routing without a global index:** the `applicationId` embeds the market (`mb-0199f6…`, a GUID v7), so
  `GET /applications/{id}` and every consumer can route to the right partition without a cross-border
  lookup table.
- **Messages carry no PII** (IDs, market, outcomes only). When Accounts needs the customer's name to open
  an account, it calls Onboarding's internal profile endpoint *within the market*, under its own
  service identity, and that read is audited (ADR-0010).
- **Logs/telemetry** (Seq) contain no PII, so a central log sink doesn't breach residency.
- The conflict with Platform's topology needs a decision from Platform and Compliance. The design makes
  in-country deployment a configuration exercise but cannot solve it alone.

## Alternatives considered
- **One database with a `Market` column**: one misplaced `WHERE` leaks across borders, and it cannot be
  moved in-country without a data migration.
- **One deployment per country**: the strongest isolation, but it is what Product means by "six of everything".
  This design runs as one codebase and one artifact, and can be deployed per country if Compliance
  requires it.
- **Schema per market in one DB**: same physical-location problem as a `Market` column.

## Consequences
- Migrations run per market database (one command, looped, in the run script).
- The workflow runner and outbox dispatcher iterate over the configured markets.
- The provider calls' own data residency (where IDNow and World-Check process data) still needs confirming.

## In 30 seconds
I can't move the servers, but I can make sure no row, blob or message has to move when someone does.
Every store is per market and the ID tells you which.
