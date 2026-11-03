# Security checklist

Standing requirement from the user: *the application must be secured from external attack.* Full detail: `docs/security.md`. Walk this list for every feature.

## Per-task check
- [ ] Every new endpoint has `[Authorize]` and filters by the token's user id (no ID-only lookups)
- [ ] Cross-user access test added (user B cannot read/modify user A's data)
- [ ] Input validated; lengths/counts/body size bounded
- [ ] Queries via EF (no string-built SQL); no `dangerouslySetInnerHTML`
- [ ] No secrets/tokens in code, logs, docs, vault or responses; config via `.env`/env vars
- [ ] Error responses don't leak internals or allow user enumeration
- [ ] New dependency: CI vulnerability audit still passes
- [ ] CORS/CSP/headers not loosened; Swagger stays Development-only
- [ ] Sensitive data minimised (esp. anything sent to the Claude API)
- [ ] `docs/security.md` updated, including what is **not** done

## Data stored (see [[Data minimisation]])
Only amount, merchant, date, debit/credit, category/flags and a SHA-256 of the message. No account numbers, references, balances or message text. Unreadable messages are never stored (an earlier exception was removed 2026-11-03).
- [ ] Any new feature must keep to this set, or record a decision with the user first
- [ ] Consider an HMAC (server secret) instead of a plain SHA-256 for the message hash

## Open items (keep current)
1. Revoke the GitHub tokens pasted in chat earlier (user action)
2. GitHub settings: branch protection, secret scanning, Dependabot alerts, private vuln reporting (user action)
3. HTTPS not set up; do not expose beyond localhost
4. JWT in `localStorage`; move to httpOnly SameSite cookie + CSRF; no revocation/refresh
5. No email verification, password reset, MFA
6. DB not encrypted at rest, no backups
7. Gmail tokens must be encrypted at rest before [[M2 Gmail import]] ships
8. No audit log / monitoring
9. Docker images not scanned
10. Not penetration tested; CSP not checked in a real browser
11. Account deletion and data export not implemented; AI needs consent and minimisation
