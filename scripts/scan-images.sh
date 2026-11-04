#!/usr/bin/env bash
# Scans the images this project runs for known HIGH/CRITICAL vulnerabilities that already have a fix.
#
#   docker compose build --pull && ./scripts/scan-images.sh
#
# Each image is exported to a temporary folder and scanned from there, so the scanner never gets access to the Docker
# socket. Trivy's vulnerability database is cached in the "trivycache" volume. Exit code 1 if anything fixable is found.
set -euo pipefail
export MSYS_NO_PATHCONV=1

TRIVY="${TRIVY_IMAGE:-aquasec/trivy:0.58.1}"
if [ $# -gt 0 ]; then IMAGES=("$@"); else IMAGES=(pennypath-api pennypath-web pennypath-proxy postgres:17-alpine); fi

dir="$(mktemp -d)"
trap 'rm -rf "$dir"' EXIT
mount="$dir"
command -v cygpath >/dev/null 2>&1 && mount="$(cygpath -w "$dir")"

fail=0
for img in "${IMAGES[@]}"; do
  file="$(echo "$img" | tr ':/' '__').tar"
  docker save "$img" -o "$mount/$file"
  echo "=== $img"
  report="$(docker run --rm -v "$mount:/scan:ro" -v trivycache:/root/.cache "$TRIVY" image --input "/scan/$file" \
    --severity HIGH,CRITICAL --ignore-unfixed --scanners vuln --quiet --format table 2>/dev/null | grep -vE '^\s*$' || true)"
  if echo "$report" | grep -q 'Total: [1-9]'; then
    echo "$report"
    fail=1
  else
    echo "no fixable HIGH/CRITICAL findings"
  fi
done
exit $fail
