# ADR-004: Cost-limited AWS development cell

- Status: Accepted for development only
- Date: 2026-10-13

## Context

The target production architecture uses private Fargate tasks, multiple Availability Zones, WAF, and Multi-AZ RDS in Frankfurt. The first milestone needs a real AWS integration environment while minimizing idle cost. The selected development region is `eu-north-1`.

## Decision

Deploy one API task, one worker task, and single-AZ RDS PostgreSQL in `eu-north-1`. RDS remains private. To avoid a continuously billed NAT Gateway in this disposable development cell, Fargate tasks may use public subnets and public IPs; security groups admit API traffic only from the ALB and no inbound worker traffic.

CloudFront/WAF and production redundancy are deferred. For this development cell the React UI is baked into the API image and served from the public ALB hostname (HTTP only). The baked UI should carry a build stamp (version, UTC build time, short git SHA) so operators can see which image is live (roadmap R-21). CDK applies mandatory project/environment/owner tags, an AWS Budget of **USD 100 / month** (`erp-dev-monthly`), bounded log retention, deletion protection off, and documented teardown.

## Consequences

- This cell is not production-shaped and must never host production data.
- Public task networking is an explicit development exception.
- Production deployment must restore private subnets, no public task IPs, at least two API tasks across AZs, WAF, Frankfurt, and Multi-AZ RDS.
- The stack must pass a cost gate before deployment and be destroyed when not in use.
