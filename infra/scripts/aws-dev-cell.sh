#!/usr/bin/env bash
# Shared settings for the dev-cell workflows. Source this file; do not execute it.

: "${AWS_REGION:=eu-north-1}"
: "${AWS_ACCOUNT_ID:=098981324459}"
: "${STACK_NAME:=Erp-dev}"
: "${DEPLOY_USER_NAME:=erp-dev-github-deploy}"

export AWS_REGION AWS_ACCOUNT_ID STACK_NAME
export AWS_DEFAULT_REGION="${AWS_REGION}"

stack_output() {
  local key="$1"
  aws cloudformation describe-stacks \
    --region "${AWS_REGION}" \
    --stack-name "${STACK_NAME}" \
    --query "Stacks[0].Outputs[?OutputKey=='${key}'].OutputValue" \
    --output text
}

ecr_registry() {
  echo "${AWS_ACCOUNT_ID}.dkr.ecr.${AWS_REGION}.amazonaws.com"
}
