#!/usr/bin/env bash
# Encrypted backup of the database and the Data Protection key ring.
#
#   BACKUP_PASSPHRASE='a long random passphrase' scripts/backup.sh [output-prefix]
#
# Writes <prefix>.sql.enc (pg_dump) and <prefix>.keys.enc (the key ring that decrypts stored secrets such as Gmail
# tokens). Both are AES-256 encrypted with a key derived from the passphrase, so the files are safe to copy off-machine.
# Keep the passphrase somewhere other than next to the backups. A backup you have never restored is not a backup:
# run scripts/restore.sh into a scratch database to check it.
set -euo pipefail

: "${BACKUP_PASSPHRASE:?Set BACKUP_PASSPHRASE (a long random passphrase)}"
[ "${#BACKUP_PASSPHRASE}" -ge 16 ] || { echo "BACKUP_PASSPHRASE must be at least 16 characters" >&2; exit 1; }

prefix="${1:-backups/pennypath-$(date -u +%Y%m%dT%H%M%SZ)}"
mkdir -p "$(dirname "$prefix")"
umask 077

encrypt() { openssl enc -aes-256-cbc -pbkdf2 -iter 600000 -salt -pass env:BACKUP_PASSPHRASE -out "$1"; }

docker compose exec -T db pg_dump -U pennypath -d pennypath --no-owner | encrypt "$prefix.sql.enc"

# The key ring volume is named <compose project>_dpkeys.
project="$(docker compose config --format json | sed -n 's/.*"name": *"\([^"]*\)".*/\1/p' | head -1)"
# MSYS_NO_PATHCONV stops Git Bash on Windows from rewriting /keys into a Windows path (no effect elsewhere).
MSYS_NO_PATHCONV=1 docker run --rm -v "${project:-pennypath}_dpkeys:/keys:ro" alpine tar -c -C /keys . | encrypt "$prefix.keys.enc"

echo "Wrote $prefix.sql.enc and $prefix.keys.enc"
