---
title: R-14 — per-tenant gapless number ranges
date: 2026-10-16
artifact_contract: ce-unified-plan/v1
product_contract_source: legacy-requirements
execution: code
---

# R-14 — per-tenant gapless number ranges

## Goal Capsule

Orders, deliveries, and invoices get numbers from a per-tenant range locked inside the business transaction. Invoice numbers stay a gapless sequence. A typed order number is still accepted.

Authority: issue #26 and `docs/plans/2026-10-14-roadmap-next-work-items.md` (R-14, decision D4). Stop if allocation cannot share the caller's transaction.

## Product Contract

### Requirements

- R1. `erp.number_ranges` stores tenant, document type, prefix, next value, year, and whether the range resets yearly.
- R2. Allocation takes the next value with `SELECT … FOR UPDATE` in the same transaction as the order, delivery, or invoice. A rollback does not consume the number.
- R3. A blank order number is assigned `AU-n`. A provided order number is stored as today, uppercased, and does not consume the range.
- R4. The worker assigns `LS-n` to a delivery and a bare integer to the invoice. Cancelled orders allocate nothing.
- R5. Defaults do not reset yearly. A range row with `ResetYearly` starts again at 1 in the next UTC year.
- R6. The table has forced `tenant_isolation`, like the other tenant tables.

### Out of scope

VAT rates and invoice lines (R-16). An admin screen for prefixes. Changing numbers that already exist.

## Planning Contract

- KTD1. Lock a row instead of a PostgreSQL sequence. A sequence would leave gaps when the transaction rolls back.
- KTD2. Invoice prefix is empty, so the stored number is `1`, `2`, `3`. Delivery prefix is `LS-`. Order prefix is `AU-`.
- KTD3. The first insert uses `ON CONFLICT DO NOTHING`, then the winner's row is locked. The caller's transaction is required.
- KTD4. Year `0` means the range does not reset. A yearly range uses the UTC year as the key.

## Implementation Units

### U1. Range table and allocator

- Files: `src/Modules/Erp.Platform/Domain/NumberRange.cs`, `src/Erp.Persistence/NumberRangeAllocator.cs`, `src/Erp.Persistence/Class1.cs`, migration `AddNumberRanges`.
- Tests: `tests/Erp.Domain.Tests/NumberRangePolicyTests.cs`, RLS guard, `tests/Erp.Integration.Tests/NumberRangeTests.cs` (rollback and yearly reset).

### U2. Assign numbers on create and on release

- Files: `src/Erp.Api/Endpoints/SalesEndpoints.cs`, `src/Erp.Worker/Worker.cs`, `openapi/v1.json`, `src/erp-web/src/api.ts`, `src/erp-web/src/workspaces/sales-model.ts`, `src/erp-web/src/workspaces/SalesWorkspace.tsx`.
- Tests: blank versus manual order number; 50 parallel releases produce a contiguous invoice sequence; existing delivery tests read the allocated number.

## Verification Contract

- `dotnet test Erp.slnx -c Release`
- `npm test`, `npm run lint`, and `npm run build` in `src/erp-web`
- `node scripts/export-openapi.mjs` and `npm run gen:api` leave no unexpected diff

## Definition of Done

- Fifty invoices created from parallel releases form a contiguous integer sequence, and the tenant's invoice numbers are `1..N` with no gaps.
- A rolled-back allocation returns the same number on the next attempt.
- The sales form leaves the order number empty unless the operator types an override.
