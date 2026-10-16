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

if ! aws sesv2 get-email-identity --region "${AWS_REGION}" --email-identity "${REGISTRATION_MAILBOX:-ckrause@cvs.de}" >/dev/null 2>&1; then
  echo "SES identity ${REGISTRATION_MAILBOX:-ckrause@cvs.de} is missing. The next deploy recreates it and needs another verification click." >&2
fi

aws iam get-user --user-name "${DEPLOY_USER_NAME}" >/dev/null

echo "Destroy left CDKToolkit, the three ECR repositories, and the deploy user ${DEPLOY_USER_NAME}."
