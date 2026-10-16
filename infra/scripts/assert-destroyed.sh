#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/aws-dev-cell.sh"

if aws cloudformation describe-stacks --region "${AWS_REGION}" --stack-name "${STACK_NAME}" >/dev/null 2>&1; then
  echo "Stack ${STACK_NAME} still exists." >&2
  exit 1
fi

if ! aws cloudformation describe-stacks --region "${AWS_REGION}" --stack-name CDKToolkit >/dev/null 2>&1; then
  echo "CDKToolkit is missing. The bootstrap stack stays after destroy." >&2
  exit 1
fi

for repo in erp-dev-api erp-dev-worker erp-dev-migrator; do
  aws ecr describe-repositories --region "${AWS_REGION}" --repository-names "${repo}" >/dev/null
done

aws iam get-role --role-name "${DEPLOY_ROLE_NAME}" >/dev/null
aws iam get-open-id-connect-provider \
  --open-id-connect-provider-arn "arn:aws:iam::${AWS_ACCOUNT_ID}:oidc-provider/token.actions.githubusercontent.com" \
  >/dev/null

echo "Destroy left CDKToolkit, the three ECR repositories, the GitHub OIDC provider, and ${DEPLOY_ROLE_NAME}."
