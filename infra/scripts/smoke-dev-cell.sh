#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/aws-dev-cell.sh"

if [[ ! "${SHORT_SHA:-}" =~ ^[0-9a-f]{7,12}$ ]]; then
  echo "SHORT_SHA must be the short commit baked into the API image." >&2
  exit 1
fi

DOMAIN="$(stack_output DistributionDomainName)"
ALB="$(stack_output AlbDnsName)"
POOL="$(stack_output UserPoolId)"
CLIENT="$(stack_output UserPoolClientId)"
BASE="https://${DOMAIN}"
RUN_ID="${GITHUB_RUN_ID:-$(date +%s)}"
SMOKE_USER="smoke-${RUN_ID}"
PASSWORD="$(python3 -c 'import secrets,string; alphabet=string.ascii_letters+string.digits+"!@#%"; print("Aa1!"+"".join(secrets.choice(alphabet) for _ in range(24)))')"
if [[ "${GITHUB_ACTIONS:-}" == "true" ]]; then
  echo "::add-mask::${PASSWORD}"
fi

cleanup() {
  aws cognito-idp admin-delete-user \
    --region "${AWS_REGION}" \
    --user-pool-id "${POOL}" \
    --username "${SMOKE_USER}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "Checking that the ALB refuses a direct internet request."
set +e
curl --connect-timeout 8 --max-time 12 -o /dev/null -sS "http://${ALB}/"
alb_status=$?
set -e
if [[ "${alb_status}" -eq 0 ]]; then
  echo "Direct request to http://${ALB}/ succeeded. The ALB must accept CloudFront only." >&2
  exit 1
fi

echo "Waiting for ${BASE}/health"
healthy=0
for _ in $(seq 1 24); do
  if curl --max-time 15 -fsS "${BASE}/health" | jq -e '.status == "ok"' >/dev/null; then
    healthy=1
    break
  fi
  sleep 5
done
if [[ "${healthy}" != "1" ]]; then
  echo "Health check did not pass." >&2
  exit 1
fi

html="$(curl --max-time 20 -fsS "${BASE}/")"
bundle="$(mktemp)"
mapfile -t assets < <(printf '%s' "${html}" | grep -oE '(src|href)="[^"]+\.js"' | sed -E 's/.*="([^"]+)"/\1/' | sort -u)
if [[ "${#assets[@]}" -eq 0 ]]; then
  echo "The shell page did not reference a JavaScript bundle." >&2
  exit 1
fi
for asset in "${assets[@]}"; do
  curl --max-time 20 -fsS "${BASE}${asset}" >>"${bundle}"
done
mapfile -t chunks < <(grep -oE '/assets/[A-Za-z0-9._-]+\.js' "${bundle}" | sort -u || true)
for asset in "${chunks[@]}"; do
  curl --max-time 20 -fsS "${BASE}${asset}" >>"${bundle}" || true
done
if ! grep -q 'Auftragsliste' "${bundle}"; then
  echo "The order-entry shell was not in the UI bundle." >&2
  exit 1
fi
if ! grep -q "${SHORT_SHA}" "${bundle}"; then
  echo "Build stamp ${SHORT_SHA} was not in the UI bundle." >&2
  exit 1
fi
rm -f "${bundle}"
echo "Shell and build stamp ${SHORT_SHA} are live."

aws cognito-idp admin-create-user \
  --region "${AWS_REGION}" \
  --user-pool-id "${POOL}" \
  --username "${SMOKE_USER}" \
  --message-action SUPPRESS \
  --temporary-password "${PASSWORD}" >/dev/null
aws cognito-idp admin-set-user-password \
  --region "${AWS_REGION}" \
  --user-pool-id "${POOL}" \
  --username "${SMOKE_USER}" \
  --password "${PASSWORD}" \
  --permanent >/dev/null

AUTH_JSON="$(jq -n \
  --arg pool "${POOL}" \
  --arg client "${CLIENT}" \
  --arg user "${SMOKE_USER}" \
  --arg pass "${PASSWORD}" \
  '{
    UserPoolId: $pool,
    ClientId: $client,
    AuthFlow: "ADMIN_USER_PASSWORD_AUTH",
    AuthParameters: { USERNAME: $user, PASSWORD: $pass }
  }')"
TOKEN="$(aws cognito-idp admin-initiate-auth \
  --region "${AWS_REGION}" \
  --cli-input-json "${AUTH_JSON}" \
  --query 'AuthenticationResult.AccessToken' \
  --output text)"
if [[ -z "${TOKEN}" || "${TOKEN}" == "None" ]]; then
  echo "Cognito did not return an access token." >&2
  exit 1
fi

request() {
  local method="$1"
  local path="$2"
  local payload="${3:-}"
  local idempotency_key="${4:-}"
  local response status body
  local args=(-sS --max-time 30 -H "Authorization: Bearer ${TOKEN}" -H "Content-Type: application/json" -X "${method}" -w $'\n__STATUS__%{http_code}')
  if [[ -n "${idempotency_key}" ]]; then
    args+=(-H "Idempotency-Key: ${idempotency_key}")
  fi
  if [[ -n "${payload}" ]]; then
    args+=(-d "${payload}")
  fi
  response="$(curl "${args[@]}" "${BASE}${path}")" || {
    echo "${method} ${path} failed before a status line." >&2
    return 1
  }
  status="${response##*__STATUS__}"
  body="${response%__STATUS__*}"
  if [[ ! "${status}" =~ ^2 ]]; then
    echo "${method} ${path} returned ${status}: ${body}" >&2
    return 1
  fi
  printf '%s' "${body}"
}

suffix="${RUN_ID}"
customer="$(request POST /api/v1/customers "$(jq -nc --arg n "SMK-${suffix}" '{number:$n,name:"Smoke Customer"}')")"
customer_id="$(jq -er '.id' <<<"${customer}")"
address="$(request POST /api/v1/addresses "$(jq -nc '{name:"Smoke Warehouse",street:"1 Test Street",postalCode:"10115",city:"Berlin",countryCode:"DE"}')")"
address_id="$(jq -er '.id' <<<"${address}")"
request POST "/api/v1/customers/${customer_id}/addresses" "$(jq -nc --arg id "${address_id}" '{addressId:$id,role:"Shipping"}')" >/dev/null
article="$(request POST /api/v1/articles "$(jq -nc --arg sku "SMK-${suffix}" '{sku:$sku,name:"Smoke Article",salesPrice:10,openingStock:10,warehouseCode:"MAIN"}')")"
article_id="$(jq -er '.id' <<<"${article}")"
order="$(request POST /api/v1/sales-orders \
  "$(jq -nc --arg customer "${customer_id}" --arg address "${address_id}" --arg article "${article_id}" \
    '{customerId:$customer,shipToAddressId:$address,lines:[{articleId:$article,quantity:1}]}')" \
  "smoke-${suffix}-order")"
order_id="$(jq -er '.id' <<<"${order}")"
request POST "/api/v1/sales-orders/${order_id}/release" '{}' "smoke-${suffix}-release" >/dev/null

delivery_number=""
invoice_number=""
for _ in $(seq 1 36); do
  deliveries="$(request GET "/api/v1/sales-orders/${order_id}/deliveries")"
  invoices="$(request GET "/api/v1/sales-orders/${order_id}/invoices")"
  delivery_number="$(jq -r '.[0].number // empty' <<<"${deliveries}")"
  invoice_number="$(jq -r '.[0].number // empty' <<<"${invoices}")"
  if [[ "${delivery_number}" == LS-* && "${invoice_number}" == R-* ]]; then
    echo "Smoke passed. Delivery ${delivery_number}, invoice ${invoice_number}."
    exit 0
  fi
  sleep 5
done

echo "Delivery or invoice did not appear. Last delivery='${delivery_number}' invoice='${invoice_number}'." >&2
exit 1
