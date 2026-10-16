#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/aws-dev-cell.sh"

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
if [[ ! "${SHORT_SHA:-}" =~ ^[0-9a-f]{7,12}$ ]]; then
  SHORT_SHA="$(git -C "${ROOT}" rev-parse --short=7 HEAD)"
fi
BUILD_TIME="${BUILD_TIME:-$(date -u +%Y-%m-%dT%H:%M:%SZ)}"
IMAGE_TAG="${IMAGE_TAG:-git-${SHORT_SHA}-$(date -u +%Y%m%d%H%M%S)}"
REGISTRY="$(ecr_registry)"

aws ecr get-login-password --region "${AWS_REGION}" \
  | docker login --username AWS --password-stdin "${REGISTRY}"

publish() {
  local name="$1"
  local dockerfile="$2"
  shift 2
  local digest=""
  docker buildx build \
    --platform linux/amd64 \
    --provenance=false \
    --sbom=false \
    --push \
    -f "${ROOT}/${dockerfile}" \
    "$@" \
    -t "${REGISTRY}/${name}:${IMAGE_TAG}" \
    "${ROOT}" >&2
  local attempt
  for attempt in 1 2 3 4 5; do
    digest="$(aws ecr describe-images \
      --region "${AWS_REGION}" \
      --repository-name "${name}" \
      --image-ids "imageTag=${IMAGE_TAG}" \
      --query 'imageDetails[0].imageDigest' \
      --output text 2>/dev/null || true)"
    if [[ "${digest}" =~ ^sha256:[0-9a-f]{64}$ ]]; then
      echo "${digest}"
      return 0
    fi
    sleep 2
  done
  echo "Build of ${name} did not produce an ECR digest for tag ${IMAGE_TAG} (got '${digest}')." >&2
  exit 1
}

API_IMAGE_DIGEST="$(publish erp-dev-api src/Erp.Api/Dockerfile \
  --build-arg "VITE_APP_GIT_SHA=${SHORT_SHA}" \
  --build-arg "VITE_APP_BUILD_TIME=${BUILD_TIME}")"
WORKER_IMAGE_DIGEST="$(publish erp-dev-worker src/Erp.Worker/Dockerfile)"
MIGRATOR_IMAGE_DIGEST="$(publish erp-dev-migrator src/Erp.Migrator/Dockerfile)"

echo "api ${API_IMAGE_DIGEST}"
echo "worker ${WORKER_IMAGE_DIGEST}"
echo "migrator ${MIGRATOR_IMAGE_DIGEST}"
echo "stamp ${SHORT_SHA}"

if [[ -n "${GITHUB_ENV:-}" ]]; then
  {
    echo "API_IMAGE_DIGEST=${API_IMAGE_DIGEST}"
    echo "WORKER_IMAGE_DIGEST=${WORKER_IMAGE_DIGEST}"
    echo "MIGRATOR_IMAGE_DIGEST=${MIGRATOR_IMAGE_DIGEST}"
    echo "SHORT_SHA=${SHORT_SHA}"
  } >>"${GITHUB_ENV}"
fi
