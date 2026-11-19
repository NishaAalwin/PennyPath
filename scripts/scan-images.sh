#!/usr/bin/env bash
# Scans the images this project runs for known HIGH/CRITICAL vulnerabilities that already have a fix.
#
#   docker compose build --pull && ./scripts/scan-images.sh
#   FAIL_ON=CRITICAL ./scripts/scan-images.sh img1 img2     # what CI and deploy run: HIGH is reported, CRITICAL blocks
#
# Each image is exported to a temporary folder and scanned from there, so the scanner never gets access to the Docker
# socket. Trivy's vulnerability database is cached in the "trivycache" volume. Exit code 1 if any fixable finding of a
# FAIL_ON severity (default HIGH,CRITICAL) is found.
set -euo pipefail
export MSYS_NO_PATHCONV=1

TRIVY="${TRIVY_IMAGE:-aquasec/trivy:0.58.1}"
FAIL_ON="${FAIL_ON:-HIGH,CRITICAL}"
if [ $# -gt 0 ]; then IMAGES=("$@"); else IMAGES=(pennypath-api pennypath-web pennypath-proxy postgres:17-alpine); fi

dir="$(mktemp -d)"
trap 'rm -rf "$dir"' EXIT
mount="$dir"
command -v cygpath >/dev/null 2>&1 && mount="$(cygpath -w "$dir")"

trivy() {
  docker run --rm -v "$mount:/scan:ro" -v trivycache:/root/.cache "$TRIVY" image --input "/scan/$1" \
    --scanners vuln --ignore-unfixed --quiet --format table --severity "$2" "${@:3}"
}

fail=0
for img in "${IMAGES[@]}"; do
  file="$(echo "$img" | tr ':/' '__').tar"
  docker save "$img" -o "$mount/$file"
  echo "=== $img"
  report="$(trivy "$file" HIGH,CRITICAL 2>/dev/null | grep -vE '^\s*$' || true)"
  if echo "$report" | grep -q 'Total: [1-9]'; then
    echo "$report"
  else
    echo "no fixable HIGH/CRITICAL findings"
  fi
  # Second pass from the cached database: decides the exit code for the blocking severities only.
  if ! trivy "$file" "$FAIL_ON" --exit-code 1 >/dev/null; then
    echo "BLOCKED: $img has a fixable $FAIL_ON vulnerability (or the scan itself failed; see above)"
    fail=1
  fi
  rm -f "$dir/$file"
done
exit $fail
