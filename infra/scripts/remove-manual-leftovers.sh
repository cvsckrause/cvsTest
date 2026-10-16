#!/usr/bin/env bash
# Removes resources the old manual runbook left outside the CDK stack.
set -euo pipefail
source "$(dirname "$0")/aws-dev-cell.sh"

project="$(aws codebuild batch-get-projects \
  --region "${AWS_REGION}" \
  --names erp-dev-images \
  --query 'projects[0].name' \
  --output text)"
if [[ "${project}" == "erp-dev-images" ]]; then
  aws codebuild delete-project --region "${AWS_REGION}" --name erp-dev-images >/dev/null
  echo "Deleted CodeBuild project erp-dev-images."
fi

if aws iam get-role --role-name erp-dev-codebuild >/dev/null 2>&1; then
  aws iam delete-role-policy --role-name erp-dev-codebuild --policy-name erp-dev-codebuild >/dev/null 2>&1 || true
  aws iam delete-role --role-name erp-dev-codebuild >/dev/null
  echo "Deleted IAM role erp-dev-codebuild."
fi

aws s3 rm "s3://cdk-hnb659fds-assets-${AWS_ACCOUNT_ID}-${AWS_REGION}/erp-src.zip" >/dev/null 2>&1 || true
echo "Manual-runbook leftovers are gone."
