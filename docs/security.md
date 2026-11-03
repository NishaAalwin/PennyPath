# PennyPath: Security

PennyPath stores personal financial data, so security is a requirement, not a later add-on.
Last reviewed: 2026-11-02. Nothing here replaces a professional security audit before a public launch.

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
3. **HTTPS is not set up.** TLS must be terminated by a reverse proxy or the host platform (the app is HTTP inside the compose network). Until then, tokens and passwords travel in clear text, so do not expose this beyond localhost. When a proxy is added, set `ForwardedHeaders:KnownProxies` so rate limits see real client IPs.
4. **JWT is stored in `localStorage`.** The CSP reduces the risk, but an XSS bug would still expose the token. Moving to an httpOnly, SameSite cookie (with CSRF protection) is a recommended follow-up. Tokens last 60 minutes and cannot be revoked early (no refresh or revocation yet).
5. **No email verification, password reset or MFA yet.**
6. **Database is not encrypted at rest** and has no backups; use disk/volume encryption and managed backups in production. Transaction text fields are stored in clear text.
7. **Gmail (M2):** OAuth refresh tokens must be encrypted at rest (ASP.NET Data Protection with a persisted key ring) before that milestone ships, and only read-only scopes requested.
8. **No audit log or security monitoring/alerting.**
9. **Docker base images are not scanned** (add Trivy or similar), and the API container's `Server` banner is removed but nginx still shows `Server: nginx` (version hidden).
10. **Not penetration tested.** The CSP and form behaviour were not checked in a real browser.
11. **Privacy/compliance:** account deletion and data export are not implemented (needed for GDPR/DPDP-style obligations); the AI features will need consent and data minimisation (send aggregates, not raw messages).

## Update 2026-11-03: stored unparsed messages (M9)
Messages the parser cannot read are kept so the user can review them: up to 500 characters each, up to 200 per user, deleted when dismissed or when the account is deleted (cascade). They can contain account digits or balances the user pasted. They are never sent to any external service. Account deletion/export (item 11 above) must cover them. New endpoints (`/api/review/unparsed`, `/api/transactions/{id}` delete, transfer and not-duplicate) all require a JWT and are scoped to the token's user; cross-user access returns 404 (tested).
