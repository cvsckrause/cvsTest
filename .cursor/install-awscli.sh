#!/usr/bin/env bash
# Install AWS CLI v2 onto the default PATH. Idempotent: a second run exits
# once `aws` is already available.
set -euo pipefail

if command -v aws >/dev/null 2>&1; then
  aws --version
  exit 0
fi

case "$(uname -m)" in
  x86_64)
    arch="x86_64"
    ;;
  aarch64 | arm64)
    arch="aarch64"
    ;;
  *)
    echo "AWS CLI install does not support $(uname -m)" >&2
    exit 1
    ;;
esac

for cmd in curl unzip sudo; do
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "AWS CLI install needs $cmd" >&2
    exit 1
  fi
done

workdir="$(mktemp -d)"
trap 'rm -rf "$workdir"' EXIT

curl -fsSL "https://awscli.amazonaws.com/awscli-exe-linux-${arch}.zip" -o "$workdir/awscliv2.zip"
unzip -q "$workdir/awscliv2.zip" -d "$workdir"
sudo "$workdir/aws/install" --bin-dir /usr/local/bin --install-dir /usr/local/aws-cli
aws --version
