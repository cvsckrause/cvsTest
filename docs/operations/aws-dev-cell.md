# AWS development cell

Lowest-cost disposable environment for the trading slice. It is not production-shaped.

## Settled exceptions versus the target architecture

| Production target | This cell |
| --- | --- |
| `eu-central-1` | `eu-north-1` |
| Private Fargate, no public task IPs, NAT/VPC endpoints | Public subnets + public task IPs, no NAT Gateway |
| Multi-AZ RDS, two API tasks, WAF, custom domain | Single-AZ `db.t4g.micro`, one API task, CloudFront default certificate in front of an HTTP ALB |
| Cognito required for API access | The UI uses hosted-UI PKCE when the API has an authority. A pool user's `sub` must equal `tenant_memberships.user_id`, or the API returns 403 |

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

Once, before the first deploy, from a shell that already has admin credentials for account `098981324459` and a logged-in `gh`:

```powershell
./infra/scripts/bootstrap-github-oidc.ps1
```

That creates the GitHub OIDC provider, the role `erp-dev-github-deploy`, and the GitHub environment `aws-dev` with a required reviewer. The role trusts only `repo:cvsckrause/cvsTest:environment:aws-dev`. There are no static access keys. Approving that environment is the cost gate: both workflows wait there before they change the account. CDK itself is invoked with `--require-approval never` because the environment approval already happened. Do not start either workflow until this script has been run.

Then run **AWS dev cell deploy** (`workflow_dispatch` on `.github/workflows/aws-dev-deploy.yml`). One run on an empty cell:

1. Creates the ECR repositories if they are missing (immutable tags on first create).
2. Creates the SES email identity `ckrause@cvs.de` if it is missing (see [Registration mail](#registration-mail)).
3. Builds the API, worker, and migrator images and pushes them by digest.
4. Bootstraps CDK if needed and deploys `Erp-dev` with those digests.
5. Runs the migrator task and waits until it exits 0.
6. Runs `infra/scripts/smoke-dev-cell.sh`.

The migrator uses the RDS master password only to install `erp_owner`, `erp_app`, and `erp_worker`, then applies migrations as `erp_owner`. The API connects as `erp_app` (DML only). The worker connects as `erp_worker` (DML plus `claim_outbox`). That run does not load demo data. To seed the development tenant on a manual `run-task`, add:

```powershell
--overrides "{\"containerOverrides\":[{\"name\":\"Migrator\",\"command\":[\"--seed-demo\"]}]}"
```

A database that still has trading tables but no `erp."__EFMigrationsHistory"` row for `InitialTradingSchema` must be destroyed and recreated. The migrator no longer patches that history.

The UI is baked into the API image and served from CloudFront (same origin as `/api/v1`). Open `https://<DistributionDomainName>/`. CloudFront redirects HTTP to HTTPS and uses the default `*.cloudfront.net` certificate. `/api/*` is not cached. The ALB stays HTTP and accepts port 80 only from the CloudFront origin-facing prefix list, so a direct request to the ALB from the internet is refused. Local Vite (`npm run dev --prefix src/erp-web`) remains optional for UI-only development and still proxies `/api` via `ERP_API_ORIGIN`.

The shell status bar shows a build stamp (`version · dd.MM.yyyy / HH:mm:ss Europe/Berlin · short git SHA`) so you can tell which image is live without opening ECR or ECS.

`infra/scripts/smoke-dev-cell.sh` checks `https://<DistributionDomainName>`:

1. `http://<AlbDnsName>/` does not connect from the internet.
2. `GET /health` returns ok, and the UI bundle contains the order-entry shell plus the short git SHA from the image build.
3. Creates a Cognito user and signs in. `GET /api/v1/session` must resolve that user to the bootstrap tenant.
4. Anonymous `POST /api/v1/registrations/approve` with an unknown token returns 404. That proves the public route passes CloudFront without sending mail.
5. `POST /api/v1/customers`, an address linked as Shipping, and `POST /api/v1/articles` (opening stock covers the release).
6. `POST /api/v1/sales-orders` with `Idempotency-Key`.
7. `POST /api/v1/sales-orders/{id}/release`.
8. A delivery and invoice appear after the worker poll, with the delivery number reading `LS-n` and the invoice number reading `R-n`.

## Registration mail

Mandant registration (R-23) mails the approver and, after approval, the new customer. The API sends through SES v2 from `ckrause@cvs.de`, which is also the approver address (`registrationMailbox` in `infra/lib/erp-dev-stack.ts`).

- The identity is created by `infra/scripts/ensure-ses-identity.sh`, not by the stack, so destroy leaves it in place and a redeploy needs no new verification. After the first create, AWS mails a verification link to `ckrause@cvs.de`. Until someone clicks it, the deploy prints a warning and registration returns 503.
- The account is in the SES sandbox: mail goes only to verified identities, at most 200 per day. A customer address that is not verified fails the approval with 503 and leaves the Mandant pending, so the link can be used again. Verify a test recipient with `aws sesv2 create-email-identity --email-identity <address>`. Production access is tracked as R-24 ([#87](https://github.com/cvsckrause/cvsTest/issues/87)).
- The API task role may call `AdminCreateUser`, `AdminSetUserPassword`, and `AdminGetUser` on the pool and `ses:SendEmail` with `ses:FromAddress` pinned to the mailbox.
- The registration rate limit counts per viewer IP. The API reads `X-Forwarded-For` with `ForwardedHeaders__ForwardLimit=2`, one hop for CloudFront and one for the ALB.
- To delete the identity when tearing the account down: `aws sesv2 delete-email-identity --email-identity ckrause@cvs.de`.

## Destroy

Run **AWS dev cell destroy** (`.github/workflows/aws-dev-destroy.yml`). It uses the same `aws-dev` environment approval, then `cdk destroy Erp-dev --force`. Nothing in this cell has deletion protection. Destroy before leaving it idle overnight if you do not want the ALB/RDS idle charge.

The workflow then deletes leftovers from the old manual runbook when they are still present: the CodeBuild project `erp-dev-images`, the IAM role `erp-dev-codebuild`, and `s3://cdk-hnb659fds-assets-098981324459-eu-north-1/erp-src.zip`.

The workflow fails if any of these is missing:

- `CDKToolkit` (the CDK bootstrap stack; leave it unless you are tearing the account down)
- ECR repositories `erp-dev-api`, `erp-dev-worker`, and `erp-dev-migrator`
- The GitHub OIDC provider and `erp-dev-github-deploy`

It also warns, without failing, when the SES identity `ckrause@cvs.de` is gone.

The account budget `erp-dev-monthly` stays when it already exists. Destroy does not delete it. It is the account-level cost alarm, not part of the cell stack.

`Erp-dev` is gone. Other stacks in the account are left alone.
