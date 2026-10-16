# AWS development cell

Lowest-cost disposable environment for the trading slice. It is not production-shaped.

## Settled exceptions versus the target architecture

| Production target | This cell |
| --- | --- |
| `eu-central-1` | `eu-north-1` |
| Private Fargate, no public task IPs, NAT/VPC endpoints | Public subnets + public task IPs, no NAT Gateway |
| Multi-AZ RDS, two API tasks, WAF, custom domain | Single-AZ `db.t4g.micro`, one API task, CloudFront default certificate in front of an HTTP ALB |
| Cognito required for API access | When the API has an authority, the UI shows a Mandantennummer / User / Passwort form and signs in with Cognito `USER_PASSWORD_AUTH` (username `{Mandantennummer}.{User}`); there is no Hosted UI. A pool user's `sub` must equal `tenant_memberships.user_id`, or the API returns 403 |

## Continuously billed resources

List prices, idle, `eu-north-1`, ~730 hours. Public IPv4 is in-scope because this cell uses public task IPs instead of NAT.

| Resource | Idle estimate |
| --- | --- |
| Application Load Balancer (hourly + 2 AZ IPs) | USD 16–24 |
| RDS PostgreSQL `db.t4g.micro` + 20 GB | USD 14–17 |
| Two Fargate tasks (0.25 vCPU / 512 MB, x86) | USD 18–20 |
| Two Fargate public IPv4 addresses | USD 7 |
| KMS + Secrets Manager (master plus `erp_owner`, `erp_app`, `erp_worker`) + logs | USD 2–4 |
| CloudFront (default certificate, price class 100) | usage-priced, near zero idle |
| SQS, EventBridge, S3, Cognito, ECR | usage-priced, near zero idle |

Estimated idle cost: **USD 55–70 / month**. AWS Budget: **USD 100** (alarms above the idle band; destroy when not in use). Destroy instead of leaving it running overnight.

Images are built by the deploy workflow and pushed to ECR (`erp-dev-api`, `erp-dev-worker`, `erp-dev-migrator`). ECS runs the immutable digest from that push, not a moving tag. Local Docker is not required for synth or deploy. When building the API image locally, pass the short git SHA so the UI stamp (R-21) is accurate:

```powershell
docker build -f src/Erp.Api/Dockerfile --build-arg VITE_APP_GIT_SHA=$(git rev-parse --short HEAD) -t <account>.dkr.ecr.eu-north-1.amazonaws.com/erp-dev-api:latest .
```

## Deploy

Once, before the first deploy, from PowerShell in the repository root, with AWS credentials for account `098981324459` (the `awscli-cvstest` user is enough) and a logged-in `gh`:

```powershell
.\infra\scripts\bootstrap-github-deploy.ps1
```

From `cmd.exe`, use `powershell -ExecutionPolicy Bypass -File .\infra\scripts\bootstrap-github-deploy.ps1`.

The script creates the IAM user `erp-dev-github-deploy` with `AdministratorAccess` (CDK changes IAM roles), creates an access key, and stores it as the secrets `AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY` of the GitHub environment `aws-dev`. It leaves that environment without required reviewers so a started workflow runs through. The organization's service control policy denies GitHub OIDC providers in this account, so the workflows cannot federate and use this key instead.

The key is long-lived. Only jobs of the `aws-dev` environment can read it. Run the script again to rotate the key: it stores a new key and then deletes the old one.

Starting **AWS dev cell deploy** or **destroy** (`workflow_dispatch`) is the cost gate: once the run starts, it changes the account without a second approval. CDK is invoked with `--require-approval never`. Do not start either workflow until this script has been run.

Then run **AWS dev cell deploy** (`workflow_dispatch` on `.github/workflows/aws-dev-deploy.yml`). One run on an empty cell:

1. Creates the ECR repositories if they are missing (immutable tags on first create).
2. Creates the SES email identity `ckrause@cvs.de` if it is missing (see [Registration mail](#registration-mail)).
3. Builds the API, worker, and migrator images and pushes them by digest.
4. Bootstraps CDK if needed and deploys `Erp-dev` with those digests.
5. Runs the migrator task and waits until it exits 0.
6. Runs `infra/scripts/smoke-dev-cell.sh`.

The migrator uses the RDS master password only to install `erp_owner`, `erp_app`, and `erp_worker`, then applies migrations as `erp_owner`. The API connects as `erp_app` (DML only). The worker connects as `erp_worker` (DML plus `claim_outbox`). That run creates the bootstrap Mandant 100 "Northstar Demo GmbH" if it is missing, but loads no other demo data and leaves its seller details empty. To seed the development tenant on a manual `run-task`, add:

```powershell
--overrides "{\"containerOverrides\":[{\"name\":\"Migrator\",\"command\":[\"--seed-demo\"]}]}"
```

A database that still has trading tables but no `erp."__EFMigrationsHistory"` row for `InitialTradingSchema` must be destroyed and recreated. The migrator no longer patches that history.

### Invoice data (R-16)

The R-16 Phase A migrations add `TaxCode` to `articles`, and `Description`, `Position`, and `TaxCode` to `sales_order_lines`. Existing articles and lines become Standard19, and existing lines take the current article name and a position by physical row order.

- **Deploy window.** The workflow deploys the new images before the migrator runs, so until the migrator exits the API returns 500 on article, order, and seller routes, and the worker fails to load released orders. Deploy with an empty outbox and release no orders until the migrator finishes. Replay any message dead-lettered in that window with `SELECT erp.replay_outbox('<id>');`.
- **Code rollback.** Lines are unique on `(TenantId, SalesOrderId, Position)`, and code from before R-16 never sets `Position`, so its second line on any order fails. Before redeploying an older image, drop `erp."IX_sales_order_lines_TenantId_SalesOrderId_Position"` and recreate the non-unique `IX_sales_order_lines_TenantId_SalesOrderId`. Do not run the Down migrations: they drop the frozen line descriptions.
- **Seller details.** Every Mandant needs street, postal code, city, country, and a Steuernummer or USt-IdNr. Enter them on the **Firma** tab (`/stamm/firma`) or with `PUT /api/v1/mandant/seller`. Phase B refuses to release orders for a Mandant with incomplete seller details, so complete them for every active Mandant before Phase B ships. Each change writes a `Mandant.SellerDetailsChanged` audit entry.

The UI is baked into the API image and served from CloudFront (same origin as `/api/v1`). Open `https://<DistributionDomainName>/`. CloudFront redirects HTTP to HTTPS and uses the default `*.cloudfront.net` certificate. `/api/*` is not cached. The ALB stays HTTP and accepts port 80 only from the CloudFront origin-facing prefix list, so a direct request to the ALB from the internet is refused. Local Vite (`npm run dev --prefix src/erp-web`) remains optional for UI-only development and still proxies `/api` via `ERP_API_ORIGIN`.

The shell status bar shows a build stamp (`version · dd.MM.yyyy / HH:mm:ss Europe/Berlin · short git SHA`) so you can tell which image is live without opening ECR or ECS.

`infra/scripts/smoke-dev-cell.sh` checks `https://<DistributionDomainName>`:

1. `http://<AlbDnsName>/` does not connect from the internet.
2. `GET /health` returns ok, and the UI bundle contains the order-entry shell plus the short git SHA from the image build.
3. Creates a Cognito user and signs in. `GET /api/v1/session` must resolve that user to the bootstrap tenant.
4. Anonymous `POST /api/v1/registrations/approve` with an unknown token returns 404. That proves the public route passes CloudFront without sending mail.
5. `GET /api/v1/mandant/seller` for the bootstrap Mandant. If fields are missing, `PUT` fills only the empty ones with demo values (Hafenstraße 1, 20095 Hamburg, DE, Steuernummer 22/123/45678, USt-IdNr DE123456789). Values entered on the cell are kept, but the bootstrap Mandant then carries placeholder tax identifiers.
6. `POST /api/v1/customers`, an address linked as Shipping, and `POST /api/v1/articles` (opening stock covers the release).
7. `POST /api/v1/sales-orders` with `Idempotency-Key`.
8. `POST /api/v1/sales-orders/{id}/release`.
9. A delivery and invoice appear after the worker poll, with the delivery number reading `LS-n` and the invoice number reading `R-n`.

## Registration mail

Mandant registration (R-23) mails the approver and, after approval, the new customer. The API sends through SES v2 from `ckrause@cvs.de`, which is also the approver address (`registrationMailbox` in `infra/lib/erp-dev-stack.ts`).

- The identity is created by `infra/scripts/ensure-ses-identity.sh`, not by the stack, so destroy leaves it in place and a redeploy needs no new verification. After the first create, AWS mails a verification link to `ckrause@cvs.de`. Until someone clicks it, the deploy prints a warning and registration returns 503.
- The account is in the SES sandbox: mail goes only to verified identities, at most 200 per day. A customer address that is not verified fails the approval with 503 and leaves the Mandant pending, so the link can be used again. Verify a test recipient with `aws sesv2 create-email-identity --email-identity <address>`. Production access is tracked as R-24 ([#87](https://github.com/cvsckrause/cvsTest/issues/87)).
- The API task role may call `AdminCreateUser`, `AdminSetUserPassword`, and `AdminGetUser` on the pool and `ses:SendEmail` with `ses:FromAddress` pinned to the mailbox.
- The registration rate limit counts per viewer IP. The API reads `X-Forwarded-For` with `ForwardedHeaders__ForwardLimit=2`, one hop for CloudFront and one for the ALB.
- To delete the identity when tearing the account down: `aws sesv2 delete-email-identity --email-identity ckrause@cvs.de`.

## Destroy

Run **AWS dev cell destroy** (`.github/workflows/aws-dev-destroy.yml`). It uses the same `aws-dev` environment for secrets, then `cdk destroy Erp-dev --force`. Nothing in this cell has deletion protection. Destroy before leaving it idle overnight if you do not want the ALB/RDS idle charge.

The workflow then deletes leftovers from the old manual runbook when they are still present: the CodeBuild project `erp-dev-images`, the IAM role `erp-dev-codebuild`, and `s3://cdk-hnb659fds-assets-098981324459-eu-north-1/erp-src.zip`.

The workflow fails if any of these is missing:

- `CDKToolkit` (the CDK bootstrap stack; leave it unless you are tearing the account down)
- ECR repositories `erp-dev-api`, `erp-dev-worker`, and `erp-dev-migrator`
- The deploy user `erp-dev-github-deploy`

It also warns, without failing, when the SES identity `ckrause@cvs.de` is gone.

The account budget `erp-dev-monthly` stays when it already exists. Destroy does not delete it. It is the account-level cost alarm, not part of the cell stack.

`Erp-dev` is gone. Other stacks in the account are left alone.
