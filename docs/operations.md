# PennyPath: Operations

How to run, back up and harden a deployment. Last verified: 2026-11-03 on Docker Desktop (Windows).
Security background and open items: [security.md](security.md).

## 1. Running over HTTPS

`docker compose up --build` starts four containers. Only the proxy publishes a port: **https://localhost:8443** (loopback only).
The page and the API share that one origin; the proxy sends `/api/*` to the API and everything else to the web container.
The database port 5433 is also loopback only. The API and web containers are not reachable from outside the compose network.

**Trusting the local certificate.** For `localhost` Caddy signs with its own local CA, so browsers warn. To trust it:
```
docker compose cp proxy:/data/caddy/pki/authorities/local/root.crt ./caddy-root.crt
```
then import `caddy-root.crt` into your OS/browser trust store as a trusted root. Remove it again when you stop developing this app,
because anyone with the matching private key (it lives in the `caddy_data` volume) could mint certificates your machine trusts.

**A real domain.** Set `SITE_ADDRESS=pennypath.example.com` in `.env`, publish ports 80 and 443 for the `proxy` service
(replace `127.0.0.1:8443:8443` in `docker-compose.yml`), and Caddy obtains and renews a Let's Encrypt certificate by itself.
Outside Development the API also sends `Strict-Transport-Security` (it skips `localhost`).

**Real client addresses.** The API trusts `X-Forwarded-For`/`-Proto` only from the proxy's fixed address `172.28.0.10`
(`ForwardedHeaders__KnownProxies__0`). Keep that in step with the subnet in `docker-compose.yml`. If another proxy sits in front of Caddy, add it.
Verified: a client-sent `X-Forwarded-For: 6.6.6.6` does not reach the audit log.

## 2. Sessions

- Signing in sets one cookie, `__Host-pp_session`: `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, no `Domain`. The token is never in a response body or in `localStorage`.
- Every session has a row in `Sessions`. **Log out** ends that session on the server, so a copied cookie stops working at once; **Sign out of all devices** ends all of them.
- Sessions last 60 minutes and are not extended; the user signs in again. There are no refresh tokens (a refresh flow would need rotation and reuse detection; not built).
- State-changing requests must carry `X-Requested-With: PennyPath` and must not be marked cross-site by the browser, otherwise 403. A `Bearer` token in the `Authorization` header still works for non-browser clients and is exempt (it is not ambient), but the API does not hand out tokens in response bodies, so such clients need a separate token flow before they can be used.
- `Jwt__Key` signs tokens. Rotating it signs everyone out.

## 3. Secrets, the key ring and encryption at rest

- Secrets live in `.env` (git-ignored): `POSTGRES_PASSWORD`, `JWT_KEY`, optional `SITE_ADDRESS`. Nothing has a default.
- **Data Protection key ring** (`dpkeys` volume, `/var/dpkeys`): encrypts stored secrets such as Gmail refresh tokens (`ITokenProtector`). Keys rotate every 90 days and old keys are kept so old values stay readable.
  **If the key ring is lost, every encrypted secret becomes unreadable** (users would have to reconnect Gmail). The backup script includes it.
  The key files themselves are **not encrypted at rest** (no certificate is configured); they are protected by volume/disk permissions and disk encryption below.
- **Database encryption at rest is not provided by PostgreSQL here.** Use full-disk or volume encryption on the host (BitLocker, LUKS, or the cloud provider's encrypted disks). The app limits the damage of a stolen disk by storing only amount, merchant, date and direction.

## 4. Backups

```
BACKUP_PASSPHRASE='<16+ random characters, stored away from the backups>' scripts/backup.sh
# writes backups/pennypath-<time>.sql.enc  (database, pg_dump)
#        backups/pennypath-<time>.keys.enc  (Data Protection key ring)
BACKUP_PASSPHRASE=... scripts/restore.sh backups/pennypath-<time>.sql.enc    # into scratch database "pennypath_restore"
```
Both files are AES-256 encrypted (PBKDF2, 600,000 iterations), so they can be copied off the machine. `restore.sh` never writes over the live database and checks the passphrase before it drops anything.
Verified 2026-11-03: a restore into a scratch database matched live exactly (row counts, amount sum, content hash); a wrong passphrase, the live database name and an injection-style name were refused; the encrypted files contained none of the known plaintext.
**Not done:** scheduling, off-machine copies and retention. Run it from a scheduled task or cron and copy the files elsewhere; test a restore regularly.

## 5. Audit log

`AuditEvents` records sign-ups, sign-ins, failed sign-ins, lockouts, sign-outs and consent changes: type, time, client IP and the account (if known). No emails, passwords or financial data. Entries older than 90 days are deleted as new ones arrive. Users see their own entries under "Privacy and security". There is no alerting on the log.

## 6. GitHub settings (cannot be done from code)

In the repository settings, turn on:
1. Branch protection on `main`: require pull requests, and require the CI and CodeQL checks.
2. Secret scanning with push protection.
3. Dependabot alerts (version updates are already configured in `.github/dependabot.yml`).
4. Private vulnerability reporting.
5. **Revoke the GitHub tokens that were pasted into chat earlier.**

## 7. Browser end-to-end check

`scripts/browser-e2e.mjs` drives the real app in Edge or Chrome (DevTools protocol, no extra packages) and checks 43 things: sign-up, cookie flags, nothing in `localStorage`, import, summary numbers, charts with hover and keyboard, search, filters, edit (valid and invalid), category change, "Not a duplicate", "Mark as transfer", paging, the privacy panel, CSP probes, logout, replay of the old session and every browser console error. Usage is in the script header. It needs the full stack running and is not part of CI. Last run 2026-11-03: 43 of 43 passed, three runs in a row.
