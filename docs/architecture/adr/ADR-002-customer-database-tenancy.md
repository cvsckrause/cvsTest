# ADR-002: Database per customer with tenant-scoped companies

- Status: Accepted
- Date: 2026-10-13

## Context

The SaaS platform must isolate customer data, support customer-specific export and restore, and still serve multiple legal entities per customer.

## Decision

Each SaaS customer receives a logical PostgreSQL database and database role. Companies inside that customer database are separated by `tenant_id`. The authenticated server-side customer registry selects the database; clients cannot submit or select `customer_id`. Verified memberships determine the permitted `tenant_id` values.

Application predicates are mandatory and PostgreSQL row-level security provides defense in depth. Tenant context is transaction-scoped and reset before a pooled connection is reused.

## Consequences

- Customer provisioning and migrations operate across a database fleet.
- A shared RDS instance remains a common capacity and failure boundary.
- Single-customer restore requires a tested export/import runbook after instance restore.
- Cross-customer leakage tests are release-blocking security tests.
