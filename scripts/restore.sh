#!/usr/bin/env bash
# Restores an encrypted database backup into a SCRATCH database, never over the live one.
#
#   BACKUP_PASSPHRASE='...' scripts/restore.sh backups/pennypath-XXXX.sql.enc [target-database]
#
# The default target is "pennypath_restore". Look at it, then (if you really mean to replace production) point the
# app at it or dump and load it yourself. Refusing to overwrite "pennypath" here is deliberate.
set -euo pipefail

: "${BACKUP_PASSPHRASE:?Set BACKUP_PASSPHRASE}"
in="${1:?Usage: restore.sh <file.sql.enc> [target-database]}"
db="${2:-pennypath_restore}"

[ "$db" != "pennypath" ] || { echo "Refusing to restore over the live database 'pennypath'." >&2; exit 1; }
[[ "$db" =~ ^[a-z_][a-z0-9_]*$ ]] || { echo "Target database name must be lowercase letters, digits and underscores." >&2; exit 1; }

# Decrypt first so a wrong passphrase or a damaged file fails before anything is dropped.
tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT
openssl enc -d -aes-256-cbc -pbkdf2 -iter 600000 -pass env:BACKUP_PASSPHRASE -in "$in" -out "$tmp"

psql() { docker compose exec -T db psql -U pennypath -v ON_ERROR_STOP=1 -q "$@"; }
psql -d postgres -c "DROP DATABASE IF EXISTS \"$db\"" -c "CREATE DATABASE \"$db\""
psql -d "$db" < "$tmp"
echo "Restored into database '$db'."
