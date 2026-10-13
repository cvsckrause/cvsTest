# ADR-001: Modular monolith for the ERP core

- Status: Accepted
- Date: 2026-10-13

## Context

The target customers have roughly 30–50 regular users, while sales, inventory, delivery, and invoicing require strong transactional consistency. A distributed service boundary for each business concept would add operational and consistency costs before those boundaries are proven.

## Decision

Build the ERP core as an ASP.NET Core modular monolith. Each module owns its domain, application layer, persistence mapping, API surface, and contracts. Modules may use typed application contracts and domain events; they must not access another module's internal tables or domain types.

The API, background worker, and integration workloads are separate deployable processes. Document generation, search, reporting, integrations, or planning may be extracted later when measured workload or release independence justifies it.

## Consequences

- Cross-module transactions stay possible for atomic ERP invariants.
- Module boundaries require architecture tests and explicit contracts.
- Independent scaling is initially limited to API and worker processes.
- Service extraction remains a deliberate migration, not an assumed starting state.
