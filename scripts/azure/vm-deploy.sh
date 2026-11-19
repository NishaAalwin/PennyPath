#!/usr/bin/env bash
# Runs ON THE AZURE VM as root, sent by .github/workflows/deploy.yml through Azure Run Command (no SSH).
# The workflow writes docker-compose.yml and docker-compose.prod.yml into /opt/pennypath first and exports:
#   IMAGE_PREFIX    ghcr.io/<owner>/<repo>  (images are <prefix>-api, -web, -proxy)
#   IMAGE_TAG       commit SHA to run
#   SITE_ADDRESS    public host name (the VM's Azure DNS name or your own domain)
#   KEY_VAULT_NAME  vault holding the app secrets
#   GHCR_USER, GHCR_TOKEN  short-lived GitHub token to pull the images (expires when the workflow job ends)
#
# Secrets are read from Key Vault with the VM's own managed identity and written to /opt/pennypath/.env (mode 600).
# They are never printed. The last line of output is PENNYPATH_DEPLOY_OK <tag> on success; the workflow checks for it.
set -euo pipefail

: "${IMAGE_PREFIX:?}" "${IMAGE_TAG:?}" "${SITE_ADDRESS:?}" "${KEY_VAULT_NAME:?}"
[[ "$IMAGE_TAG" =~ ^[0-9a-f]{7,40}$ ]] || { echo "IMAGE_TAG must be a commit SHA" >&2; exit 1; }

# The first deploy can arrive while cloud-init is still installing Docker.
cloud-init status --wait >/dev/null 2>&1 || true
command -v docker >/dev/null || { echo "Docker is not installed (cloud-init failed? see /var/log/cloud-init-output.log)" >&2; exit 1; }
mountpoint -q /var/lib/docker || { echo "Data disk is not mounted at /var/lib/docker" >&2; exit 1; }

cd /opt/pennypath
umask 077

# ---- Secrets from Key Vault (managed identity, nothing stored in GitHub) ----
token="$(curl -fsS -H Metadata:true \
  'http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fvault.azure.net' \
  | jq -r .access_token)"
if [ -z "$token" ] || [ "$token" = null ]; then echo "Could not get a managed identity token" >&2; exit 1; fi

# secret <name> required|optional  -> prints the value (empty when optional and absent)
secret() {
  local out code body
  out="$(curl -sS -w '\n%{http_code}' -H "Authorization: Bearer $token" \
    "https://${KEY_VAULT_NAME}.vault.azure.net/secrets/$1?api-version=7.4")"
  code="${out##*$'\n'}"
  body="${out%$'\n'*}"
  if [ "$code" = 200 ]; then
    jq -r .value <<<"$body"
  elif [ "$code" = 404 ] && [ "$2" = optional ]; then
    printf ''
  else
    echo "Key Vault: cannot read secret '$1' (HTTP $code)" >&2
    return 1
  fi
}

# Write NAME='value' (single quotes: compose does not expand $ inside them). Refuse quotes/newlines in values.
line() {
  case "$2" in *"'"* | *$'\n'*) echo "Value for $1 contains a quote or a line break" >&2; exit 1 ;; esac
  printf "%s='%s'\n" "$1" "$2"
}

postgres_password="$(secret postgres-password required)"
jwt_key="$(secret jwt-key required)"
google_client_id="$(secret google-client-id optional)"
google_client_secret="$(secret google-client-secret optional)"
smtp_host="$(secret smtp-host optional)"
smtp_port="" smtp_user="" smtp_password="" smtp_from=""
if [ -n "$smtp_host" ]; then
  smtp_port="$(secret smtp-port required)"
  smtp_user="$(secret smtp-user optional)"
  smtp_password="$(secret smtp-password optional)"
  smtp_from="$(secret smtp-from required)"
fi

{
  echo "COMPOSE_FILE=docker-compose.yml:docker-compose.prod.yml"
  echo "COMPOSE_PROJECT_NAME=pennypath"
  line IMAGE_PREFIX "$IMAGE_PREFIX"
  line IMAGE_TAG "$IMAGE_TAG"
  line SITE_ADDRESS "$SITE_ADDRESS"
  line POSTGRES_PASSWORD "$postgres_password"
  line JWT_KEY "$jwt_key"
  if [ -n "$google_client_id" ]; then
    line GOOGLE_CLIENT_ID "$google_client_id"
    line GOOGLE_CLIENT_SECRET "$google_client_secret"
    line GOOGLE_REDIRECT_URI "https://${SITE_ADDRESS}/api/gmail/callback"
  fi
  if [ -n "$smtp_host" ]; then
    line SMTP_HOST "$smtp_host"
    line SMTP_PORT "$smtp_port"
    line SMTP_USER "$smtp_user"
    line SMTP_PASSWORD "$smtp_password"
    line SMTP_FROM "$smtp_from"
    echo "SMTP_USE_TLS=true"
  fi
} > .env.new
chmod 600 .env.new
unset token postgres_password jwt_key google_client_secret smtp_password

# ---- Pull, then switch .env and start ----
# Validate and pull with the new settings first: if the images cannot be pulled (for example a rollback to a SHA whose
# images were never pushed), the old .env and the running containers are left exactly as they were.
new=(docker compose --env-file .env.new -f docker-compose.yml -f docker-compose.prod.yml)
"${new[@]}" config --quiet
if [ -n "${GHCR_TOKEN:-}" ]; then
  trap 'docker logout ghcr.io >/dev/null 2>&1 || true' EXIT
  printf '%s' "$GHCR_TOKEN" | docker login ghcr.io -u "${GHCR_USER:-github}" --password-stdin >/dev/null
fi
"${new[@]}" pull --quiet
mv .env.new .env
docker compose up -d --no-build --remove-orphans --wait --wait-timeout 300
docker compose ps --format '{{.Service}}: {{.Status}}'

# Prove the new version is what runs: each app container must use the image for this commit.
for s in api web proxy; do
  running="$(docker inspect --format '{{.Config.Image}}' "$(docker compose ps -q "$s")")"
  if [ "$running" != "${IMAGE_PREFIX}-${s}:${IMAGE_TAG}" ]; then
    echo "$s runs $running, expected ${IMAGE_PREFIX}-${s}:${IMAGE_TAG}" >&2
    exit 1
  fi
done

# Keep a week of old images for a quick rollback; drop older unused ones.
docker image prune -af --filter 'until=168h' >/dev/null || true

echo "PENNYPATH_DEPLOY_OK ${IMAGE_TAG}"
