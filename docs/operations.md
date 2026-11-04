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

## 8. Connect Gmail (M2)

The Gmail import is off until you give the server a Google OAuth client. Nothing in the repository contains one.

**Create the client (once, in Google Cloud Console)**
1. Create a project and enable the **Gmail API**.
2. **OAuth consent screen:** user type **External**, publishing status **Testing**. Under *Test users* add your own Gmail address. In Testing mode Google lets only listed test users connect (up to 100) and expires the grant after **7 days**, so you reconnect weekly; the app tells you when that happens.
3. **Credentials, Create credentials, OAuth client ID:** type **Web application**. Add the authorised redirect URI **exactly**: `https://localhost:8443/api/gmail/callback` (for a real domain use `https://your-domain/api/gmail/callback` and set `GOOGLE_REDIRECT_URI` to the same).
4. Put the values in your local `.env` (git-ignored), never in chat, code or the vault:
   ```
   GOOGLE_CLIENT_ID=...apps.googleusercontent.com
   GOOGLE_CLIENT_SECRET=...
   ```
5. `docker compose up -d api` to pick them up. In the app, open **Transactions** and choose **Connect Gmail**. Google shows a "this app isn't verified" warning for test apps: choose Advanced, then continue, since it is your own app.

**What the app does**
- Asks only for `gmail.readonly` (it cannot send, delete or change mail). A grant with any other scope is refused and revoked.
- Searches only emails from a fixed list of bank and payment-app domains (`GmailQuery.SenderDomains`) that contain alert words (debited, credited, spent, UPI, ...), and `-in:spam -in:trash`. First sync: last 90 days. Later syncs: since the previous sync (one day of overlap). At most 300 emails per sync; the page says when more are waiting.
- Reads each email in memory, extracts amount, where, date and in or out, and drops it. The email text, sender and ids are not stored. The repeat-proof key is a SHA-256 of the Gmail message id.
- Stores only the **refresh token, encrypted** with the Data Protection key ring (`dpkeys` volume). Losing the key ring makes the saved token unreadable; the user then reconnects. Back the key ring up as described in section 4.
- **Disconnect** deletes the stored token and revokes the grant at Google. Transactions already imported stay.

**If your bank is not in the list or an alert is not recognised:** the sync result lists the subjects of emails it could not read (shown once, not stored). Send the subject wording (no account numbers) and the sender domain and the list or parser can be extended.

**Not verified against real Google yet.** The flow is tested against a stand-in for Google; the first real connection is the real test.

## 9. Scanning the images for known vulnerabilities
```
docker compose build --pull
./scripts/scan-images.sh        # needs Docker; downloads the Trivy scanner image and its database once
```
Exit code 1 means a HIGH or CRITICAL finding that already has a fix. After a rebuild, API, web and proxy system layers are clean; the open findings (Caddy binary, `gosu` in Postgres) are listed in docs/security.md. Run it after every pull and before exposing the app beyond your own machine.

## 10. Accounts: email, password reset, two-factor, deletion (M10)

**Email.** New accounts must open a link emailed to them before they can use their data (accounts that existed before this feature count as verified). Password reset also works by emailed link. Links carry the token after a `#`, work once, and expire (verify 24 hours, reset 30 minutes).
- **Locally** the app sends mail to **Mailpit**, a catcher that shows every message at **http://127.0.0.1:8025** (loopback only, messages kept in memory). Sign up, open that page, click the link.
- **For real email** set in `.env`: `SMTP_HOST`, `SMTP_PORT` (usually 587), `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM`, and `SMTP_USE_TLS=true`; the app refuses to send a mail password without TLS. Then remove the `mailpit` service. Set `SITE_ADDRESS` to your domain so links point there. The real-server path has not been tested yet.
- To switch verification off (for example where no mail can be sent) set `Auth__RequireVerifiedEmail: "false"` on the `api` service; new accounts then count as verified at once.

**Forgot your password.** Log-in screen, "Forgot your password?". A reset signs out every device, clears any lockout and also marks the email verified. It does **not** switch two-factor off.

**Two-factor login (optional).** Privacy and security tab, "Set up two-factor login": type the key into an authenticator app, enter the 6-digit code, and save the 10 one-time **recovery codes** (shown once). Signing in then needs the password and a code. Lost the phone: use a recovery code, then turn two-factor off and on again for fresh codes. Lost both phone and recovery codes: there is no self-service way back in; an operator would have to clear the account's two-factor settings in the database.

**Your data and deletion.** The same tab downloads everything stored about you as JSON (no password hash or token), and deletes the account and all its data after the password (and a code) is confirmed again. Old backups made before a deletion still contain the account; delete them if that matters.
