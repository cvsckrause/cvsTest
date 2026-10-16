---
title: R-13 — Sales-order list, deliveries, and invoices read
date: 2026-10-15
artifact_contract: ce-unified-plan/v1
product_contract_source: ce-plan-bootstrap
execution: code
origin: docs/plans/2026-10-14-roadmap-next-work-items.md (R-13) / GitHub #25
---

# R-13 — Sales-order list, deliveries, and invoices read

## Goal Capsule

- **Objective.** An operator can find a sales order (including one just created) from a list, open it, and after release see the delivery and invoice numbers appear without a manual page reload.
- **Means.** Add keyset-paginated and status-filtered `GET` sales-order list plus delivery/invoice read endpoints on the existing sales API surface; extend the Sales workspace so the list is the finder and steps 3–4 poll the new reads until documents exist.
- **Authority.** Roadmap R-13 and issue #25 (accepted 2026-10-15: list + status filter are the search; URL-openable documents already shipped in R-10). Precedence: accepted roadmap scope > this plan > local inventiveness.
- **Stop when.** Acceptance on #25 is met, OpenAPI + generated client stay in sync, cross-tenant tests cover the new reads, and Vitest covers list open + poll-until-documents. Do not start R-14/R-15/R-16 content (number ranges, cancel, DACH invoice lines).
- **Who finishes.** Implementation agent completes code + local verification and opens the PR; merge stays with the user.

## Product Contract

### Problem Frame

Today an operator can create and release an order but cannot browse orders, and steps 3–4 only say the worker will create delivery and invoice. There is no `GET /sales-orders` list and no read access to `deliveries` or `invoices`. Finding a just-created order or confirming post-release documents requires guesswork.

### Requirements

- R1. `GET /api/v1/sales-orders` returns the current tenant's orders with keyset pagination and an optional status filter.
- R2. `GET /api/v1/sales-orders/{id}/deliveries` and `GET /api/v1/sales-orders/{id}/invoices` return those documents for an order the tenant can see; unknown or other-tenant orders yield 404.
- R3. `GET /api/v1/invoices/{id}` returns one invoice for the current tenant; other-tenant or missing ids yield 404.
- R4. The Sales UI shows an order list (status filter is the search). Opening a row navigates to the existing `/sales/{id}` document route.
- R5. After release, steps 3 and 4 show real delivery and invoice numbers and poll until the worker has created them (no manual reload).
- R6. A newly created order appears in the list and can be opened from it.
- R7. Tenant isolation for the new endpoints is covered in the R-05-style integration suite.

### Scope Boundaries

- In scope: read APIs above, list + filter UI, poll on deliver/invoice steps, OpenAPI refresh + `npm run gen:api`, integration + Vitest coverage.
- Out of scope: free-text search across articles/stock/other documents; invoice DACH content (R-16); goods issue (R-12); cancel/warehouse (R-15); number ranges (R-14); changing how the worker creates documents.

### Key Decisions (product)

- KD1. `session-settled:user-approved` — The order list plus status filter is the finder/search for this issue (accepted 2026-10-15). Rejected: separate global search. Reason: meeting feedback recorded on the roadmap and #25.
- KD2. `session-settled:user-approved` — Opening an order uses the R-10 URL route (`/sales/{id}`). Rejected: list-only navigation without shareable URLs. Reason: R-10 already ships that contract.

### Actors / Flows / Acceptance Examples

- A1. Operator in the Sales workspace.
- F1. Create order → see it in the list → open it → release → wait on steps 3–4 until delivery and invoice numbers appear.
- AE1. After release, delivery number `DEL-…` and invoice number `INV-…` appear without reload once the worker has processed the outbox.
- AE2. Tenant B calling list or detail endpoints never sees tenant A's order, delivery, or invoice ids.

## Planning Contract

### Assumptions

- A1. R-10 shell, generated client, and `/sales/{id}` routing are already on `main` (PR #51 / #54); issue #22 may still be open but is not a code blocker.
- A2. Worker still creates `DEL-{orderNumber}` / `INV-{orderNumber}` with current Invoice shape (`number`, `netAmount`, `createdAt`). R-16 will enrich content later; this plan exposes what exists.
- A3. Seeded integration tenants release orders but do not process the outbox unless a test calls the existing outbox helper; delivery/invoice tests must process outbox or insert rows explicitly.
- A4. Status filter values match `SalesOrderStatus` names (`Draft`, `Released`). Invalid status → 400 ProblemDetails (existing error contract from R-06).

### Key Technical Decisions

- KTD1. Keyset cursor for the order list is `(createdAt desc, id desc)` encoded as opaque `after` = `{createdAt:o}/{id}` (UTC ISO + guid), matching "newest first / find what I just created". Rejected: order-number ascending like customers — operators hunt recent work, not alphabetical numbers. Mirror the clamp/limit pattern from `MapMasterDataEndpoints` (`limit` default 50, max 200).
- KTD2. Put all new routes in `src/Erp.Api/Endpoints/SalesEndpoints.cs` next to existing sales routes (R-02 layout). Rejected: a new BillingEndpoints file this round — deliveries/invoices are read in the sales document flow; a later ADR can move module ownership.
- KTD3. List response is a slim summary (`id`, `orderNumber`, `customerId`, `status`, `netAmount`, `createdAt`) without lines. Detail stays on existing `GET /sales-orders/{id}`.
- KTD4. Delivery/invoice responses expose stable DTO fields already on the entities (ids, numbers, salesOrderId, netAmount for invoice, createdAt, ship-to snapshot fields for delivery). No schema migration.
- KTD5. UI polling: after release (and when opening a released order on deliver/invoice steps), poll deliveries then invoices every ~2s with a modest cap (~60s), stop when both exist or on navigation away. Show "wird erzeugt…" until present. Rejected: websockets / SignalR — out of slice; rejected: manual refresh button as the only path.
- KTD6. Status filter query param name: `status`. Omit = all statuses.

### Technical Design

**API**

| Method | Path | Behavior |
| --- | --- | --- |
| GET | `/api/v1/sales-orders?status=&after=&limit=` | Keyset page, optional status |
| GET | `/api/v1/sales-orders/{id}/deliveries` | Array (0..n); 404 if order missing |
| GET | `/api/v1/sales-orders/{id}/invoices` | Array (0..n); 404 if order missing |
| GET | `/api/v1/invoices/{id}` | Single invoice; 404 if missing |

Order existence checks use the tenant-filtered `SalesOrders` set so other tenants get 404, not empty arrays that leak existence differently than detail routes.

**UI**

- Sales workspace gains a list panel (or list-first view when `documentId` is null): status select + rows + open → `navigate({ workspace: 'sales', documentId })`.
- After create, refresh/prepend list so R6 holds without full reload.
- Steps 3–4 replace the static worker note with document numbers (or waiting copy) driven by poll state in `sales-model.ts`.

**OpenAPI / client**

- Refresh `openapi/v1.json` the same way CI expects (existing export path).
- Run `npm run gen:api` and add typed helpers on `erpApi` in `src/erp-web/src/api.ts`.

### Dependencies and Sequencing

1. U1 API reads + OpenAPI
2. U2 Integration tests (needs U1; uses outbox processor for documents)
3. U3 UI list + poll + Vitest
4. U4 Regen client + wire `erpApi` (can land with U1/U3)

Prerequisite: R-02 and R-10 on main (satisfied). Soft: R-05 harness exists (satisfied).

### Risks

- Polling races if the worker is slow: cap + waiting copy; acceptance still passes when worker runs in the same environment.
- Cursor encoding bugs on timezone: always UTC round-trip ISO.
- CrossTenantIsolationTests must be extended or a dedicated test added so new list endpoints cannot regress silently.

## Implementation Units

### U1. Sales read endpoints + OpenAPI

- **Goal.** Expose list/deliveries/invoices reads with tenant isolation via existing EF filters/RLS.
- **Files.** `src/Erp.Api/Endpoints/SalesEndpoints.cs`; response records co-located; `openapi/v1.json` refreshed.
- **Approach.** Follow `MapGet("/customers"…)` pagination clamp; order by `CreatedAt` desc, `Id` desc; parse `status` enum; for nested routes, load order first and 404 if null.
- **Covers.** R1–R3, KTD1–KTD4, KTD6.
- **Test scenarios.**
  - Unit/integration: list returns only current tenant; status filter Draft excludes Released; `after` advances without duplicates; invalid status → 400.
  - Nested deliveries/invoices: 404 for other tenant's order id; empty arrays when order exists but worker not yet run; populated after outbox process.
  - Invoice by id: 404 cross-tenant; 200 own.

### U2. Integration coverage for new reads

- **Goal.** R-05-style proof that lists/details never leak and happy paths work.
- **Files.** `tests/Erp.Integration.Tests/CrossTenantIsolationTests.cs` (extend list assertions); new `tests/Erp.Integration.Tests/SalesOrderReadTests.cs` (or equivalent) using `OutboxProcessor` / existing worker helpers.
- **Approach.** After release + process outbox, assert delivery/invoice numbers; assert tenant B cannot see tenant A documents via list or ids.
- **Covers.** R7, AE2.
- **Test scenarios.** See U1 integration bullets; plus list includes newly created order before and after release.

### U3. Sales UI list, status filter, and document polling

- **Goal.** Operator finder + live steps 3–4.
- **Files.** `src/erp-web/src/workspaces/sales-model.ts`, `SalesWorkspace.tsx`, `App.css` (minimal layout for list), Vitest under `src/erp-web/src/` (prefer model/hooks tests with mocked `erpApi`).
- **Approach.** When no `orderId`, show list; when present, keep document workflow. Poll helper clears on unmount / order change. After `saveOrder`, refresh list and navigate to new id (existing behavior) so list still shows it when returning.
- **Covers.** R4–R6, R5, KTD5, AE1.
- **Test scenarios.**
  - Selecting a list row calls navigate with that document id.
  - Status filter changes the request query.
  - Released order without documents shows waiting; when poll returns numbers, UI surfaces them.
  - Existing create/release Vitest paths stay green.

### U4. Generated client wiring

- **Goal.** Keep contract-derived types authoritative.
- **Files.** `src/erp-web/src/api.ts`, `src/erp-web/src/api.generated.ts` (generated).
- **Approach.** Add `salesOrders`, `salesOrderDeliveries`, `salesOrderInvoices`, `invoice` methods typed from generated schemas.
- **Covers.** R1–R3 consumer side.
- **Test scenarios.** CI gen:api freshness check remains green; TypeScript build passes.

## Verification Contract

- `dotnet build Erp.slnx -c Release`
- `dotnet test Erp.slnx` (domain + architecture + integration; Docker required for integration)
- `cd src/erp-web && npm ci && npm run lint && npm test && npm run build`
- `npm run gen:api` then confirm no diff on `api.generated.ts` / `openapi/v1.json` after intentional refresh
- Manual smoke (optional in agent): create → list → open → release → wait for DEL/INV numbers with API + worker running

## Definition of Done

- [ ] U1–U4 complete with scenarios above passing
- [ ] #25 acceptance bullets satisfied
- [ ] OpenAPI before/after documented in PR (new paths added; existing paths unchanged in behavior)
- [ ] No R-14/R-15/R-16 scope creep
- [ ] PR open against `main`

## Appendix

### Research breadcrumbs

- List pagination pattern: `src/Erp.Api/Endpoints/MasterDataEndpoints.cs` (`after`/`limit` clamp).
- Sales surface: `src/Erp.Api/Endpoints/SalesEndpoints.cs`.
- Entities: `src/Modules/Erp.Billing/Domain/BillingEntities.cs`, `src/Modules/Erp.Sales/Domain/SalesOrder.cs`.
- Worker create: `src/Erp.Worker/Worker.cs` (`DEL-`/`INV-` prefixes).
- UI shell: `src/erp-web/src/workspaces/SalesWorkspace.tsx`, `sales-model.ts`, `route.ts`.
- Isolation tests: `tests/Erp.Integration.Tests/CrossTenantIsolationTests.cs`; outbox helper: `OutboxProcessor.cs`.
- Prerequisite note: R-10 merged on main; close #22 is housekeeping outside this PR unless the user asks.
