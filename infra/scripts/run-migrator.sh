#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/aws-dev-cell.sh"

CLUSTER="$(stack_output ClusterName)"
TASK_DEF="$(stack_output MigratorTaskDefinitionArn)"
SECURITY_GROUP="$(stack_output MigratorSecurityGroupId)"
SUBNETS="$(stack_output PublicSubnetIds)"

SUBNET_JSON="$(printf '%s' "${SUBNETS}" | tr ',' '\n' | jq -R . | jq -s .)"
NETWORK="$(jq -n --argjson subnets "${SUBNET_JSON}" --arg sg "${SECURITY_GROUP}" '{
  awsvpcConfiguration: {
    subnets: $subnets,
    securityGroups: [$sg],
    assignPublicIp: "ENABLED"
  }
}')"

RUN="$(aws ecs run-task \
  --region "${AWS_REGION}" \
  --cluster "${CLUSTER}" \
  --launch-type FARGATE \
  --task-definition "${TASK_DEF}" \
  --network-configuration "${NETWORK}" \
  --output json)"

FAILURES="$(jq -r '.failures | length' <<<"${RUN}")"
if [[ "${FAILURES}" != "0" ]]; then
  echo "ECS refused the migrator task: ${RUN}" >&2
  exit 1
fi

TASK_ARN="$(jq -r '.tasks[0].taskArn' <<<"${RUN}")"
echo "Migrator task ${TASK_ARN}"
aws ecs wait tasks-stopped --region "${AWS_REGION}" --cluster "${CLUSTER}" --tasks "${TASK_ARN}"

DESCRIBE="$(aws ecs describe-tasks \
  --region "${AWS_REGION}" \
  --cluster "${CLUSTER}" \
  --tasks "${TASK_ARN}" \
  --output json)"
EXIT_CODE="$(jq -r '.tasks[0].containers[] | select(.name=="Migrator") | .exitCode' <<<"${DESCRIBE}")"
REASON="$(jq -r '.tasks[0].stoppedReason // ""' <<<"${DESCRIBE}")"
if [[ "${EXIT_CODE}" != "0" ]]; then
  echo "Migrator exited ${EXIT_CODE}: ${REASON}" >&2
  jq -r '.tasks[0].containers[] | "\(.name) exit=\(.exitCode // "none") \(.reason // "")"' <<<"${DESCRIBE}" >&2
  exit 1
fi

echo "Migrator finished."
