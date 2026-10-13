# ERP Trading Foundation — Milestone 1

## Outcome

Deliver an executable vertical slice for a DACH trading company:

`Customer → Article → Sales Order → Release / Stock Reservation → Delivery → Invoice`

The slice runs locally, preserves the target modular-monolith boundaries, and can be deployed as a deliberately low-cost AWS development cell in `eu-north-1`.

## Settled decisions

- Backend: ASP.NET Core on .NET 10.
- Frontend: React + TypeScript using Vite.
- Database: PostgreSQL; EF Core for writes and ordinary CRUD, explicit SQL permitted for measured query paths.
- Architecture: modular monolith with separate API and worker processes.
- Tenancy: logical database per SaaS customer; `tenant_id` separates companies inside that database.
- Reliability: transactional outbox, inbox deduplication, retries, and DLQ semantics.
- AWS: CDK, ECS/Fargate, RDS PostgreSQL, Cognito, S3/KMS, EventBridge, SQS, and CloudWatch.
- Development deployment: `eu-north-1`, single-AZ RDS and one task per service. These are cost-driven development exceptions, not production defaults.

## Scope

### Domain

- Customers and articles as minimal master data.
- Stock balances and reservations.
- Sales orders with draft and released states.
- Atomic release plus reservation.
- Delivery and invoice creation as idempotent follow-up processing.
- Append-only audit entries and stable integration-event contracts.

### API

- `/api/v1/customers`
- `/api/v1/articles`
- `/api/v1/stock`
- `/api/v1/sales-orders`
- `/api/v1/sales-orders/{id}/release`
- OpenAPI, correlation IDs, idempotency keys, and keyset pagination.

### UI

- ERP shell and navigation.
- Customer/article lookup.
- Keyboard-oriented sales-order entry and release workflow.
- Generated or contract-derived API types.
- Local development identity adapter and Cognito-ready PKCE configuration.

### Infrastructure

- Versioned PostgreSQL migrations.
- API and worker container images.
- CDK stacks for network, data, messaging, identity, application, observability, and cost controls.
- One synthetic customer database with two tenants.
- Deployment, smoke-test, and teardown runbooks.

## Explicit non-goals

- Purchasing, manufacturing, MRP, service management, full accounting, DATEV, e-invoicing, offline mode, OpenSearch, ElastiCache, multi-region DR, production HA, and customer-specific extensions.
- A general workflow engine or generic event-sourcing framework.
- Deploying production-shaped NAT, WAF, Multi-AZ RDS, or two API tasks in this cost-limited development milestone.

## Implementation units

1. Record ADRs and repository conventions.
2. Create solution structure and module-boundary tests.
3. Implement domain behavior and tests proof-first.
4. Add persistence, migrations, tenancy, audit, outbox, and integration tests.
5. Expose API contracts and implement the worker.
6. Build the React sales-order workspace.
7. Add containers and CDK.
8. Verify locally; synthesize and diff AWS.
9. Stop at the cost gate before billable deployment.
10. Deploy, migrate, and smoke-test only after explicit cost-gate approval.

## Verification contract

- `dotnet test`
- `dotnet build --configuration Release`
- frontend unit tests and production build
- architecture-boundary tests
- cross-tenant access tests
- duplicate API request and duplicate message tests
- `cdk synth`
- `cdk diff` against AWS account `098981324459` in `eu-north-1`
- post-deployment health, authenticated API, order-release, outbox, worker, and DLQ smoke tests

## Cost gate

Before deployment, report:

- every continuously billed resource,
- the estimated monthly idle cost range,
- the configured AWS Budget threshold,
- the exact destroy command and any retained resources,
- any IAM permission blocker.

No billable stack is deployed until the user approves that gate.
