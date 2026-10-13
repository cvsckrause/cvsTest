# ERP docs preview on AWS

The public site is the existing docs preview, exported to static HTML and served over HTTPS.

**URL:** https://dt9udciv2ut48.cloudfront.net

Open that address in a browser. Nothing needs to run on your computer. The two architecture documents, the contents list, and the Mermaid diagrams are on that site.

The repository does not contain the ERP application described in those documents. This deployment publishes the docs preview only. There is no database and there are no API keys.

## What is running

| Resource | Value |
| --- | --- |
| Region for the bucket | eu-north-1 (Stockholm), matching the configured AWS CLI credentials |
| S3 bucket | `erp-docs-098981324459` (private, encrypted with SSE-S3) |
| CloudFront distribution | `E2TFQ4QFVZ1QB` |
| Origin access control | `E4CKTMR9R3IGC` |
| Public hostname | `dt9udciv2ut48.cloudfront.net` |

CloudFront is global. HTTP requests redirect to HTTPS. The S3 bucket blocks public access. Only this CloudFront distribution can read the objects. A direct request to the bucket returns 403.

Expected cost at docs-site traffic is about $0–$1 per month. S3 storage for a few megabytes is a fraction of a cent. CloudFront’s first-year free tier covers 1 TB of transfer and 10 million HTTPS requests per month. There is no always-on server.

A custom domain such as `my-app.example.com` is not configured. That needs a domain you own and a Route 53 hosted zone ($0.50 per month). The CloudFront certificate for `*.cloudfront.net` is already in use.

## Redeploy after editing the documents

From the repository root, in PowerShell:

```powershell
powershell -File tools/docs-preview/publish-aws.ps1
```

The script renders `docs/*.md` with the existing preview server, uploads the HTML to S3, and invalidates the CloudFront cache. New Markdown files in `docs/` are included. The update is visible within a few minutes, after the invalidation finishes.

Node.js is required locally for the export. It is installed at `C:\Program Files\nodejs\node.exe`. The AWS CLI must be authenticated as it is now (`eu-north-1`).

Check the result:

```powershell
curl.exe -s https://dt9udciv2ut48.cloudfront.net/health
```

`/health` is generated at publish time. It lists the documents that were exported. It does not reflect later edits until you publish again.

## Logs

There is no application server in AWS, so there is no application log stream.

Request counts and error rates are in the CloudFront console for distribution `E2TFQ4QFVZ1QB`, under Monitoring. Access logging to S3 is turned off so it does not add another bucket or storage charge. Turn on CloudFront standard logging in that console page if you later need a per-request log.

## Delete the AWS resources

Do this when you no longer want the public site. Order matters: CloudFront must be disabled and finished deploying before it can be deleted. Then empty and delete the bucket, then delete the origin access control.

```powershell
aws cloudfront get-distribution-config --id E2TFQ4QFVZ1QB --output json > cf-config.json
```

In `cf-config.json`, set `DistributionConfig.Enabled` to `false`. Keep the `ETag` value from the same response.

```powershell
aws cloudfront update-distribution --id E2TFQ4QFVZ1QB --if-match ETAG_FROM_GET --distribution-config file://edited-distribution-config.json
aws cloudfront wait distribution-deployed --id E2TFQ4QFVZ1QB
aws cloudfront get-distribution-config --id E2TFQ4QFVZ1QB --query ETag --output text
aws cloudfront delete-distribution --id E2TFQ4QFVZ1QB --if-match NEW_ETAG
aws s3 rm s3://erp-docs-098981324459 --recursive
aws s3api delete-bucket --bucket erp-docs-098981324459 --region eu-north-1
aws cloudfront delete-origin-access-control --id E4CKTMR9R3IGC --if-match ETAG
```

The origin-access-control ETag comes from `aws cloudfront get-origin-access-control --id E4CKTMR9R3IGC`. Disabling CloudFront usually takes several minutes. After the distribution and bucket are gone, this site no longer incurs charges.

Local preview is unchanged: `tools/docs-preview/start-preview.cmd` still serves http://127.0.0.1:4173/.
