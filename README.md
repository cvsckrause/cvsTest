# cvsTest

Cloud-first ERP foundation for DACH mid-market trading. The first executable slice is:

`Customer → Article → Sales Order → Release / Stock Reservation → Delivery → Invoice`

Architecture documents live in `docs/`. The milestone plan is [`docs/plans/2026-10-13-erp-trading-foundation.md`](docs/plans/2026-10-13-erp-trading-foundation.md).

## Local development

The API and worker expect PostgreSQL at `localhost:5432` (`erp_dev` / `erp` / `erp`). Docker Desktop is not installed on this workstation yet; install it or provide an equivalent PostgreSQL instance before applying migrations.

```powershell
dotnet ef database update --project src/Erp.Persistence/Erp.Persistence.csproj
dotnet run --project src/Erp.Migrator/Erp.Migrator.csproj
dotnet run --project src/Erp.Api/Erp.Api.csproj
dotnet run --project src/Erp.Worker/Erp.Worker.csproj
npm run dev --prefix src/erp-web
```

- API: `http://localhost:5080` (or the AWS cell via `ERP_API_ORIGIN`)
- UI: `http://127.0.0.1:5173` — `/api` is proxied; do not point the browser at the ALB
- OpenAPI: `http://localhost:5080/openapi/v1.json`

Local identity uses `DevelopmentIdentity:TenantId`. Cognito PKCE is reserved for the AWS cell.

## Tests

```powershell
dotnet test Erp.slnx
npm test --prefix src/erp-web
```

## AWS

See [`docs/operations/aws-dev-cell.md`](docs/operations/aws-dev-cell.md). Do not deploy until the cost gate in that document has been approved.