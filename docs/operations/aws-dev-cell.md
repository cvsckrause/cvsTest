# AWS development cell

Lowest-cost disposable environment for the trading slice. It is not production-shaped.

## Settled exceptions versus the target architecture

| Production target | This cell |
| --- | --- |
| `eu-central-1` | `eu-north-1` |
| Private Fargate, no public task IPs, NAT/VPC endpoints | Public subnets + public task IPs, no NAT Gateway |
| Multi-AZ RDS, two API tasks, WAF/CloudFront | Single-AZ `db.t4g.micro`, one API task, HTTP ALB only |
| Cognito required for API access | Cognito is provisioned; API still uses the development tenant until PKCE is wired |

## Continuously billed resources

List prices, idle, `eu-north-1`, ~730 hours. Public IPv4 is in-scope because this cell uses public task IPs instead of NAT.

| Resource | Idle estimate |
| --- | --- |
| Application Load Balancer (hourly + 2 AZ IPs) | USD 16–24 |
| RDS PostgreSQL `db.t4g.micro` + 20 GB | USD 14–17 |
| Two Fargate tasks (0.25 vCPU / 512 MB, x86) | USD 18–20 |
| Two Fargate public IPv4 addresses | USD 7 |
| KMS + Secrets Manager + logs | USD 2–4 |
| SQS, EventBridge, S3, Cognito, ECR | usage-priced, near zero idle |

Estimated idle cost: **USD 55–70 / month**. AWS Budget: **USD 50** (will alarm at idle). Destroy instead of leaving it running overnight.

Images are built in CodeBuild (`erp-dev-images`) and pushed to ECR (`erp-dev-api`, `erp-dev-worker`, `erp-dev-migrator`). Local Docker is not required for synth or deploy.

## Deploy

```powershell
cd infra
npm ci
npx cdk bootstrap aws://098981324459/eu-north-1
aws codebuild start-build --project-name erp-dev-images --region eu-north-1
npx cdk diff
npx cdk deploy Erp-dev
```

After deploy, run the migrator once (subnet and security group IDs come from stack outputs):

```powershell
aws ecs run-task --cluster <ClusterName> --launch-type FARGATE --task-definition <MigratorTaskDefinitionArn> --network-configuration "awsvpcConfiguration={subnets=[<public-subnet>],securityGroups=[<MigratorSecurityGroupId>],assignPublicIp=ENABLED}"
```

Smoke:

1. `GET /health`
2. `POST /api/v1/customers` then `POST /api/v1/articles`
3. `POST /api/v1/sales-orders` with `Idempotency-Key`
4. `POST /api/v1/sales-orders/{id}/release`
5. Confirm a delivery and invoice appear after the worker poll

## Destroy

```powershell
cd infra
npx cdk destroy Erp-dev --force
```

Nothing in this cell has deletion protection. Destroy before leaving it idle overnight if you do not want the ALB/RDS idle charge.

These stay behind after stack destroy and should be removed separately:

```powershell
aws ecr delete-repository --repository-name erp-dev-api --force --region eu-north-1
aws ecr delete-repository --repository-name erp-dev-worker --force --region eu-north-1
aws ecr delete-repository --repository-name erp-dev-migrator --force --region eu-north-1
aws codebuild delete-project --name erp-dev-images --region eu-north-1
aws iam delete-role-policy --role-name erp-dev-codebuild --policy-name erp-dev-codebuild
aws iam delete-role --role-name erp-dev-codebuild
aws budgets delete-budget --account-id 098981324459 --budget-name erp-dev-monthly
aws s3 rm s3://cdk-hnb659fds-assets-098981324459-eu-north-1/erp-src.zip
```

`CDKToolkit` is the CDK bootstrap stack. Leave it unless you are tearing the account down.
