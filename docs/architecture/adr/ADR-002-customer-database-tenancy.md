# ADR-002: Database per customer with tenant-scoped companies

- Status: Accepted
- Date: 2026-10-13

## Context

The SaaS platform must isolate customer data, support customer-specific export and restore, and still serve multiple legal entities per customer.

## Decision

Each SaaS customer receives a logical PostgreSQL database and database role. Companies inside that customer database are separated by `tenant_id`. The authenticated server-side customer registry selects the database; clients cannot submit or select `customer_id`. Verified memberships determine the permitted `tenant_id` values.

Application predicates are mandatory and PostgreSQL row-level security provides defense in depth. Tenant context is reset before a pooled connection is reused.

The API sets that context with `set_config('app.tenant_id', …, false)` in `TenantConnectionInterceptor` when a connection opens. The value is session-scoped. `SET LOCAL` is not used, because the interceptor runs before a transaction exists and `SET LOCAL` outside a transaction has no effect. Isolation between requests depends on Npgsql's default pool reset (`No Reset On Close=false`): the driver runs `DISCARD ALL` before a pooled connection is reused and clears the GUC. Do not disable that reset.

## Consequences

- Customer provisioning and migrations operate across a database fleet.
- A shared RDS instance remains a common capacity and failure boundary.
- Single-customer restore requires a tested export/import runbook after instance restore.
- Cross-customer leakage tests are release-blocking security tests.
