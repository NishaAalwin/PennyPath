# PennyPath: Security

PennyPath stores personal financial data, so security is a requirement, not a later add-on.
Last reviewed: 2026-11-03 (security gate M12; see the last section). Nothing here replaces a professional security audit before a public launch.

## Protected assets
Account credentials, financial transactions (amounts, merchants, accounts), and later Gmail OAuth tokens.

## Measures in place

### Authentication and accounts
| Threat | Measure | Verified |
|---|---|---|
| Stolen database | Passwords stored as salted, slow ASP.NET Identity hashes (PBKDF2); never logged or returned | Yes (DB check, unit tests) |
| Brute force on one account | Lockout: 5 failures locks login for 15 minutes (also blocks the correct password); counter resets on success | Yes (unit tests + live) |
| Credential stuffing / scanning | Rate limit 20 req/min/IP on auth endpoints, 300 req/min/IP globally | Partly (live lockout seen; rate limiter not load-tested) |
| User enumeration by response | Same error and same hashing work for unknown email and wrong password | Yes (unit test; timing not measured) |
| Weak passwords | 8-128 chars, common-password list, must not contain email name | Yes |
| Token forgery | HS256 only, issuer/audience/expiry validated, `alg: none` tokens rejected, key >= 32 bytes, placeholder keys refused outside Development | Yes (tests) |

### Authorization and data
- Every transaction endpoint requires a valid JWT, and queries are always filtered by the token's user id. A second user cannot see another user's data (tested).
- Entity Framework parameterises all queries (no string-built SQL).
- Import limits: 500 messages, 2000 characters each, 1 MB request body.

### Network and browser
- CORS allows only the configured origins, only `Authorization`/`Content-Type`, and only the methods the API uses (verified: an unknown origin gets no CORS headers).
- API responses carry `nosniff`, `X-Frame-Options: DENY`, `no-referrer`, `Cache-Control: no-store` and a deny-all CSP; the server banner is removed.
- The web app is served with a strict Content-Security-Policy (scripts and styles from the same origin only, connections only to the API), which limits XSS impact. React escapes output by default and the code has no `dangerouslySetInnerHTML`.
- HSTS is enabled outside Development (effective once served over HTTPS).
- OpenAPI/Swagger is only exposed in Development (verified 404 otherwise).

### Containers and secrets
- No secrets in the repo: `.env` is git-ignored; compose refuses to start without `POSTGRES_PASSWORD` and `JWT_KEY` (verified).
- Database, API and web ports bind to `127.0.0.1` only.
- API runs as a non-root user with a read-only filesystem; web runs as unprivileged nginx; all containers use `no-new-privileges`, and api/web drop all Linux capabilities.

### Pipeline
- CI fails on known-vulnerable NuGet packages (including transitive) and on high npm advisories; the current tree has none.
- CodeQL scans C# and TypeScript on every push/PR and weekly; Dependabot proposes weekly dependency, Docker and Actions updates.
- Workflows use least-privilege `permissions`.

## Not done / needs your action
1. **Revoke the GitHub tokens that were pasted into chat** (both of them). Treat them as compromised.
2. **Enable in GitHub settings** (not possible from code): branch protection on `main` (require CI + CodeQL, require PRs), secret scanning with push protection, Dependabot alerts, and private vulnerability reporting.
3. **HTTPS for a real deployment.** Locally the proxy serves HTTPS with a development certificate from Caddy's own CA (browsers warn until it is trusted). A public deployment needs a real domain (`SITE_ADDRESS`) with ports 80/443 open so Caddy can get a Let's Encrypt certificate; hosting is still undecided. See [operations.md](operations.md).
4. **No refresh tokens.** Sessions last 60 minutes and are not extended. They can be ended early (log out, sign out of all devices), but there is no sliding expiry, rotation or reuse detection.
5. **No email verification, password reset or MFA yet** (M10).
6. **Database is not encrypted at rest** (PostgreSQL does not do it here); use disk/volume encryption on the host. The **Data Protection key files are not encrypted at rest** either (no certificate configured). Backups are encrypted and restore-tested, but **not scheduled and not copied off the machine**.
7. **Gmail (M2):** the encryption service and persisted key ring exist, but no Gmail code uses them yet. Only read-only scopes may be requested.
8. **Audit log exists but nothing watches it:** no alerting, no log shipping, no tamper protection.
9. **Docker base images are not scanned** (add Trivy or similar).
10. **Not penetration tested.** The app was exercised in a real browser (Edge, 43 checks, see below), but that is a functional check, not a penetration test.
11. **Privacy/compliance:** account deletion and data export are not implemented (M10; needed for GDPR/DPDP-style obligations). AI consent exists (off by default) but no AI feature exists yet, and the gate only works if every future AI feature calls it.
12. **Rate limiting** is per client IP, now the real client address behind the proxy. It was not load-tested. On Docker Desktop the client appears as the Docker gateway address (172.28.0.1), so all local requests share one limit.

## Update 2026-11-03: data minimisation
Only amount, merchant (where/what), date and debit/credit are stored. Account numbers, bank reference numbers, balances and message text are not read or stored. Messages the parser cannot read are shown once in the import response and are not saved (an earlier version kept them for review; that was removed and a migration deleted the stored text). Only a SHA-256 of the normalised message is kept so re-importing a message is a no-op. A hash of a short, structured SMS can in principle be guessed by someone who has the database; an HMAC with a server secret would prevent that (not done). New endpoints (`/api/summary`, `/api/transactions/{id}` delete, transfer and not-duplicate) require a JWT and are scoped to the token's user; cross-user access returns 404 (tested).

## Update 2026-11-03: list search and edit (M7)
`GET /api/transactions` and `PUT /api/transactions/{id}` require a JWT and are scoped to the token's user (another user's id returns 404). Inputs are bounded on the server: search 100 characters, page size 100, merchant 200 characters without control characters, amount 0 to 999,999,999.99 with 2 decimals, dates 2000-01-01 to tomorrow. Search is parameterised through Entity Framework, so `%`, `_` and quotes are plain text (verified on PostgreSQL). No new data is stored.

## Update 2026-11-03: security gate (M12)
- **Cookie sessions replace the token in `localStorage`.** The login token is only in `__Host-pp_session` (HttpOnly, Secure, SameSite=Strict, Path=/, no Domain) and is never in a response body. A page script cannot read it (verified in Edge: `document.cookie` empty, `localStorage`/`sessionStorage` empty).
- **CSRF:** state-changing requests need `X-Requested-With: PennyPath` and must not be `Sec-Fetch-Site: cross-site/same-site`, otherwise 403 (verified live). Sign-in and sign-up are covered too.
- **Server-side sessions and revocation:** every token carries a session id; each request checks the session is active. Log out ends it, "sign out of all devices" ends all, expired or unknown sessions are rejected. A copied cookie stops working after logout (verified live: 204 then 401).
- **One HTTPS origin:** a Caddy proxy is the only published port (`https://localhost:8443`, loopback). No CORS is needed or allowed. The API and web containers have no host ports (ports 5000 and 8080 are closed, verified). The API trusts `X-Forwarded-*` only from the proxy's fixed address; a spoofed header did not reach the audit log (verified). `Server` banners are removed.
- **CSP** tightened to `connect-src 'self'` only. In Edge it blocked an injected inline script, a blob: script and a request to another origin, and the app itself caused no violations.
- **Audit log** (sign-up, sign-in, failure, lockout, sign-out, consent changes): type, time, IP, account. No emails, passwords or financial data. Users see their own entries; 90-day retention.
- **AI consent** (off by default) with a per-user flag, endpoint, audit trail and UI; `IAiConsent` is the gate future AI features must call.
- **Encrypted secrets:** `ITokenProtector` (ASP.NET Data Protection, authenticated encryption) with the key ring persisted on a volume. Tested: round trip, tampering refused, other purpose refused, survives a restart, unreadable without the ring.
- **Backups:** `scripts/backup.sh` / `restore.sh` (AES-256, PBKDF2) cover the database and the key ring; restore never overwrites the live database. A restore matched live exactly.
- **Dependency finding fixed during this work:** the first Data Protection package version I added (10.0.4) carried a critical advisory (GHSA-9mv3-2cwr-p262, fixed in 10.0.7) and a transitive high one in System.Security.Cryptography.Pkcs (fixed in 10.0.13); it was moved to 10.0.14 and the scan is clean.
- **Breaking change:** the web app no longer works at the old addresses (`:8080`, `:5000`); use `https://localhost:8443`. Login responses no longer contain a token.
