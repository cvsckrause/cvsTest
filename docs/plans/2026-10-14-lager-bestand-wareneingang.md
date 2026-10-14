---
title: Lager — Bestandübersicht + Wareneingang
date: 2026-10-14
artifact_contract: ce-unified-plan/v1
product_contract_source: ce-plan-bootstrap
execution: code
origin: approach plan lager_approach_plan_5292a318 (slice A)
---

# Lager — Bestandübersicht + Wareneingang (Slice A)

## Goal Capsule

**Objective.** Warehouse operators can open a real Lager workspace, see on-hand / reserved / available stock by article and warehouse, book a goods receipt that increases on-hand, and inspect an append-only movement history for those bookings—without leaving the existing modular-monolith ERP shell.

**Means.** Extend the trading-foundation inventory slice (`StockBalance`, `GET /api/v1/stock`, `Erp.Inventory`) with a goods-receipt write path, a dedicated stock-movement ledger, richer stock reads for the UI, and a React Lager mask wired to the existing nav.

**Authority.** Settled approach-plan slice A; trading-foundation patterns and ADRs (modular monolith, tenancy, outbox/idempotency) override C4 scanner/WMS aspirations for this milestone.

**Stop when.** Bestandübersicht, Wareneingang, and movement history work end-to-end with tests; Lager nav no longer shows the stub notice. Do not start bin scanning, transfers, purchasing inbound, manufacturing issues, offline PWA, or OpenSearch read models.

**Who finishes.** Implementation agent completes code + local verification; shipping (commit/PR) stays with the caller unless the user asks.

## Product Contract

### Summary

Milestone 2 Lager slice A turns the stub Lager nav into a usable warehouse mask for DACH trading: list stock balances, book Wareneingang against an article/warehouse, and review the resulting movement trail. Stock reservations from sales-order release remain unchanged.

### Problem Frame

Trading foundation already owns `StockBalance` for sales release, but operators have no warehouse surface: Lager only shows a notice, there is no goods-receipt API that increases on-hand after opening stock, and there is no queryable movement ledger for warehouse bookings. Target architecture describes a much larger WMS; operators need a thin, honest first mask now.

### Requirements

1. **R1 — Bestandübersicht.** Given articles with stock balances for the current tenant, when an operator opens Lager, they see each balance with article identity (SKU/name), warehouse code, on-hand, reserved, and available.
2. **R2 — Wareneingang.** Given a known article and warehouse, when an operator books a positive receipt quantity (optional reference note), on-hand increases by that quantity; reserved is unchanged; available reflects the new on-hand.
3. **R3 — Missing balance create-on-receipt.** Given an article with no balance row for the chosen warehouse, when a receipt is booked, a `StockBalance` is created with that on-hand (reserved 0).
4. **R4 — Movement / audit history.** Each successful Wareneingang appends an immutable stock-movement record (tenant, article, warehouse, quantity, type, optional reference, actor, timestamp) visible in the Lager workspace.
5. **R5 — Idempotent receipt API.** Duplicate `Idempotency-Key` for the same receipt operation returns the prior result without double-booking stock or movements.
6. **R6 — Lager nav wiring.** The Lager nav item activates the Lager workspace (not a stub notice); Vertrieb remains the sales-order workspace.
7. **R7 — Tenancy preserved.** All stock and movement reads/writes stay tenant-scoped via existing `ITenantContext` / EF query filters; no cross-tenant leakage.

### Key Decisions

- **Slice altitude A** (session-settled: user-directed): Bestand + Wareneingang + movement history only; transfers/scanner UX deferred.
- **Movement ledger as inventory entity** (session-settled: user-approved via approach deliverable): append-only `StockMovement` in `Erp.Inventory`, not only platform `AuditEntry`—AuditEntry may still be written for platform consistency on booking.
- **Extend trading foundation** (session-settled: user-directed): no tenancy/outbox/monolith redesign; inventory stays in modular monolith `erp` schema.

### Scope Boundaries

**In scope**

- Domain: `StockBalance` receive/increase; `StockMovement` append-only goods-receipt records.
- API: enriched stock list; `POST` goods receipt; movement list.
- Persistence: EF mapping + versioned migration for `stock_movements` (+ RLS policy consistent with peers).
- UI: Lager workspace (balance grid, receipt form, recent movements) replacing the Lager stub notice.
- Tests: domain proofs for receive/movement; frontend test for Lager surface; existing reservation tests remain green.

**Deferred for later**

- Bin / Lagerplatz scanning, touch/barcode floor UX.
- Purchasing-driven inbound, warehouse transfers UI, manufacturing issues.
- Offline PWA, OpenSearch-scale movement read models, 100M-row partitioning.

**Outside this product's identity**

- Redesigning sales release/reservation, Cognito, CDK, or worker delivery/invoice flows.

### Acceptance Examples

- **AE1.** Operator opens Lager → sees seeded MAIN balances with SKU and quantities.
- **AE2.** Book Wareneingang quantity 5 for article BIKE-001 / MAIN → on-hand +5; movement row appears with quantity 5 and type goods receipt.
- **AE3.** Replay same Idempotency-Key → no second +5; same resource returned.
- **AE4.** Click Vertrieb → sales-order workspace still works; existing reservation tests pass.

## Planning Contract

### Technical Design

| Area | Approach |
| --- | --- |
| Domain | Add `StockBalance.Receive(decimal quantity)`; add `StockMovement` with factory `GoodsReceipt(...)`. |
| API | Keep `GET /api/v1/stock` but enrich with `sku`/`name` via join to articles; add `POST /api/v1/stock/receipts` (Idempotency-Key); add `GET /api/v1/stock/movements`. |
| Persistence | `DbSet<StockMovement>`; table `erp.stock_movements`; unique index on tenant+occurred as needed for listing; RLS policy like `stock_balances`. |
| Events/audit | On receipt: write `AuditEntry` (`Stock.GoodsReceipt`) and optional outbox event `erp.stock.goods-received.v1` only if cheap and consistent with release pattern—prefer AuditEntry + movement row as minimum; outbox optional if it stays within existing patterns without new worker handlers. |
| UI | `workspace: 'sales' \| 'lager'`; Lager shows balance table, receipt form (article, warehouse default MAIN, quantity, reference), movements list. Match existing shell/CSS language. |
| Patterns | Mirror sales-order idempotency + transaction around stock mutate; domain tests in `Erp.Domain.Tests`; Vitest for nav/workspace. |

### Assumptions

- Warehouse codes remain free-form strings (default `MAIN`); no warehouse master data table in this slice.
- Opening stock from article create continues to create balances without writing a historical movement (legacy opening is not rewritten).
- No new microservice or separate inventory database.

### Sequencing

1. Domain + unit tests (proof-first).
2. Persistence + migration.
3. API endpoints.
4. React Lager workspace + API client + tests.
5. Full local verification.

## Implementation Units

### U1. Domain: receive + stock movement

**Goal.** Stock can increase on-hand via receive; each receipt can be represented as an immutable movement.

**Files.** `src/Modules/Erp.Inventory/Domain/StockBalance.cs`, new `StockMovement.cs`; `tests/Erp.Domain.Tests/UnitTest1.cs` (or sibling test file).

**Approach.** Proof-first: failing tests for receive happy path, reject non-positive quantity, movement factory fields; then implement.

**Verification.** `dotnet test --filter FullyQualifiedName~Erp.Domain.Tests`.

### U2. Persistence + migration

**Goal.** Persist `StockMovement` with tenant filters and RLS parity.

**Files.** `src/Erp.Persistence/Class1.cs` (DbContext), new EF migration under `src/Erp.Persistence/Migrations/`, update `initial.sql` only if the repo treats it as the replayable bootstrap (prefer EF migration as source of truth; sync `initial.sql` if existing convention requires it).

**Verification.** Solution builds; migration compiles; snapshot includes `stock_movements`.

### U3. API: stock list enrichment, receipts, movements

**Goal.** Operators can list enriched stock, book idempotent receipts, list movements.

**Files.** `src/Erp.Api/Program.cs` (and request/response records).

**Approach.** Transactional receipt: load/create balance, `Receive`, add movement + audit + idempotency record; enrich GET stock with article join; GET movements newest-first with optional limit.

**Verification.** Domain/architecture tests green; manual or smoke via existing local API when DB available. Prefer focused API-level tests only if a harness already exists (none today—do not invent a full WebApplicationFactory suite unless cheap).

### U4. React Lager workspace

**Goal.** Lager nav opens a real mask; sales workspace unchanged.

**Files.** `src/erp-web/src/App.tsx`, `api.ts`, `App.css` (minimal), `App.test.tsx`.

**Verification.** `npm test` / vitest; production build if already in CI habit.

## Verification Contract

| Check | Command / evidence |
| --- | --- |
| Domain + architecture | `dotnet test` |
| API build | `dotnet build --configuration Release` |
| Frontend unit | `npm test` (or `npx vitest run`) in `src/erp-web` |
| Frontend build | `npm run build` in `src/erp-web` |
| Manual smoke (optional if Postgres up) | Open Lager → list stock → book receipt → see movement; release sales order still reserves |

## Definition of Done

- [ ] R1–R7 satisfied in code.
- [ ] Lager stub notice removed for the Lager nav path.
- [ ] Domain tests cover receive and insufficient/invalid quantity.
- [ ] Movement rows created on receipt; idempotent replay does not double-apply.
- [ ] Existing sales reservation tests still pass.
- [ ] Architecture module-boundary tests still pass.
- [ ] No commits unless the user requested them (caller owns shipping).

## Explicit non-goals

Bin topology, scanner UX, warehouse transfers UI, purchasing inbound, manufacturing issues, offline PWA, OpenSearch movement indexes, inventory microservice split.
