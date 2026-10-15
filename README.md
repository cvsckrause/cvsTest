# cvsTest

Cloud-first ERP foundation for DACH mid-market trading. The first executable slice is:

`Customer → Article → Sales Order → Release / Stock Reservation → Delivery → Invoice`

Architecture documents live in `docs/`. The milestone plan is [`docs/plans/2026-10-13-erp-trading-foundation.md`](docs/plans/2026-10-13-erp-trading-foundation.md).

## Toolchain pins

| Tool | Pin | Source |
| --- | --- | --- |
| .NET SDK | `10.0.401` | [`global.json`](global.json) |
| Node.js | `22.22.0` | [`.nvmrc`](.nvmrc) (also `engines.node` in `src/erp-web` and `infra`) |

CI and local Node installs should use the `.nvmrc` value (`nvm use`, `fnm use`, or `actions/setup-node` with `node-version-file: .nvmrc`).

## Local development

The API and worker expect PostgreSQL at `localhost:5432` (`erp_dev` / `erp` / `erp`). Docker Desktop is not installed on this workstation yet; install it or provide an equivalent PostgreSQL instance before applying migrations.

```powershell
dotnet ef database update --project src/Erp.Persistence/Erp.Persistence.csproj
dotnet run --project src/Erp.Migrator/Erp.Migrator.csproj
dotnet run --project src/Erp.Api/Erp.Api.csproj
dotnet run --project src/Erp.Worker/Erp.Worker.csproj
npm run dev --prefix src/erp-web
```

- API: `http://localhost:5080` (or the AWS cell ALB)
- UI (local): `http://127.0.0.1:5173` with `/api` proxied via `ERP_API_ORIGIN`
- UI (AWS cell): `http://<AlbDnsName>/` — same origin as the API; no Vite required
- OpenAPI: `http://localhost:5080/openapi/v1.json`

Local identity uses `DevelopmentIdentity:TenantId`. Cognito PKCE is reserved for the AWS cell.

## Tests

```powershell
dotnet test Erp.slnx
npm test --prefix src/erp-web
```

## CI

GitHub Actions workflow [`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs on every pull request and on pushes to `main`. Required job names (for branch rulesets):

- `backend` — `dotnet restore` / `build -c Release` / `test` with the SDK from `global.json`
- `web` — `npm ci`, lint, test, and production build in `src/erp-web`
- `infra-synth` — `npm ci` and `npx cdk synth` in `infra` (no AWS credentials)

### Branch protection on `main`

`main` is protected by the repository ruleset **Protect main** (block deletions, block force-pushes, require a pull request).

After this CI workflow has reported at least once, add **required status checks** for `backend`, `web`, and `infra-synth` on that ruleset so a red job blocks merge. Until those checks are required, a PR can still merge with failing CI.

Current posture (acceptable for bootstrapping CI, not final): PR required, **0** approving reviews, no required status checks yet. Prefer enabling the three checks above before treating Wave 0 as done.

## AWS

See [`docs/operations/aws-dev-cell.md`](docs/operations/aws-dev-cell.md). Do not deploy until the cost gate in that document has been approved.