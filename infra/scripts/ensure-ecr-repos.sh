#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/aws-dev-cell.sh"

for name in erp-dev-api erp-dev-worker erp-dev-migrator; do
  if aws ecr describe-repositories --region "${AWS_REGION}" --repository-names "${name}" >/dev/null 2>&1; then
    echo "ECR repository ${name} already exists."
    continue
  fi

  aws ecr create-repository \
    --region "${AWS_REGION}" \
    --repository-name "${name}" \
    --image-tag-mutability IMMUTABLE \
    --image-scanning-configuration scanOnPush=true >/dev/null
  echo "Created immutable ECR repository ${name}."
done
