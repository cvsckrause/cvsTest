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

- API: `http://localhost:5080`
- UI (local): `http://127.0.0.1:5173` with `/api` proxied via `ERP_API_ORIGIN`
- UI (AWS cell): `https://<DistributionDomainName>/` — CloudFront in front of the API; no Vite required
- The UI status bar shows `version · dd.MM.yyyy / HH:mm:ss Europe/Berlin · git SHA` so you can tell which image is live
- OpenAPI: `http://localhost:5080/openapi/v1.json`

Local identity comes from `DevelopmentIdentity` in `appsettings.Development.json` and is used only when authentication is off. Cognito PKCE is reserved for the AWS cell.

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

`main` is protected by the repository ruleset **Protect main**:

- Block deletions and force-pushes
- Require a pull request (0 approving reviews)
- Require status checks `backend`, `web`, and `infra-synth` (GitHub Actions)
- Require the branch to be up to date with `main` before merge (`strict_required_status_checks_policy`)

## AWS

See [`docs/operations/aws-dev-cell.md`](docs/operations/aws-dev-cell.md).

Once, before the first deploy, from PowerShell in the repository root, with AWS credentials for account `098981324459` and a logged-in `gh`:

```powershell
.\infra\scripts\bootstrap-github-deploy.ps1
```

That creates the IAM user `erp-dev-github-deploy` with `AdministratorAccess`, stores its access key as secrets of the GitHub environment `aws-dev`, and leaves that environment without required reviewers so a started workflow runs through. The organization SCP blocks GitHub OIDC in this account, which is why the workflows use a key. Starting **AWS dev cell deploy** or **destroy** (`workflow_dispatch`) is the cost gate: once the run starts, it changes the account without a second approval. Do not start either workflow until this script has been run.

Scoping that key down from `AdministratorAccess` and optionally restoring a reviewer or CI gate is tracked in [#103](https://github.com/cvsckrause/cvsTest/issues/103).
