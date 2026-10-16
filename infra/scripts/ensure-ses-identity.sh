#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/aws-dev-cell.sh"

# Matches registrationMailbox in infra/lib/erp-dev-stack.ts.
MAILBOX="${REGISTRATION_MAILBOX:-ckrause@cvs.de}"

if identity="$(aws sesv2 get-email-identity --region "${AWS_REGION}" --email-identity "${MAILBOX}" 2>/dev/null)"; then
  if [[ "$(jq -r '.VerifiedForSendingStatus' <<<"${identity}")" == "true" ]]; then
    echo "SES identity ${MAILBOX} is verified."
    exit 0
  fi
  message="SES identity ${MAILBOX} is not verified yet. Click the link in the AWS verification mail, or registration mail will fail."
else
  aws sesv2 create-email-identity --region "${AWS_REGION}" --email-identity "${MAILBOX}" >/dev/null
  message="Created SES identity ${MAILBOX}. AWS sent a verification mail; click its link before testing registration."
fi

if [[ "${GITHUB_ACTIONS:-}" == "true" ]]; then
  echo "::warning::${message}"
else
  echo "${message}"
fi
