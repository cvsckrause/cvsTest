# ADR-004: Cost-limited AWS development cell

- Status: Accepted for development only
- Date: 2026-10-13

## Context

The target production architecture uses private Fargate tasks, multiple Availability Zones, WAF, and Multi-AZ RDS in Frankfurt. The first milestone needs a real AWS integration environment while minimizing idle cost. The selected development region is `eu-north-1`.

## Decision

Deploy one API task, one worker task, and single-AZ RDS PostgreSQL in `eu-north-1`. RDS remains private. To avoid a continuously billed NAT Gateway in this disposable development cell, Fargate tasks may use public subnets and public IPs; security groups admit API traffic only from the ALB and no inbound worker traffic.

CloudFront sits in front of the HTTP ALB and uses the default `*.cloudfront.net` certificate. The ALB security group admits port 80 only from the CloudFront origin-facing prefix list, so the internet cannot call the ALB directly. WAF, a custom domain, and production redundancy stay deferred. The React UI is baked into the API image and served from the CloudFront hostname over HTTPS. `/api/*` is not cached. CDK applies mandatory project/environment/owner tags, an AWS Budget of **USD 100 / month** (`erp-dev-monthly`), bounded log retention, deletion protection off, and documented teardown. CloudFront has no hourly charge, so the idle band stays **USD 55–70 / month**.

## Consequences

- This cell is not production-shaped and must never host production data.
- Public task networking is an explicit development exception.
- Production deployment must restore private subnets, no public task IPs, at least two API tasks across AZs, WAF, Frankfurt, and Multi-AZ RDS.
- The stack must pass a cost gate before deployment and be destroyed when not in use.
