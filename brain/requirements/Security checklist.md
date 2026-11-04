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
- [ ] State-changing endpoints work with the cookie + `X-Requested-With` header and are covered by the CSRF tests; no endpoint puts a token in a response body
- [ ] New secrets stored at rest go through `ITokenProtector`, never plain columns
- [ ] AI features call `IAiConsent.IsEnabledAsync` first and send merchant text or aggregates only
- [ ] Security-relevant events are written to the audit log (no personal or financial data in it)

## Data stored (see [[Data minimisation]])
Only amount, merchant, date, debit/credit, category/flags and a SHA-256 of the message. No account numbers, references, balances or message text. Unreadable messages are never stored (an earlier exception was removed 2026-11-03).
- [ ] Any new feature must keep to this set, or record a decision with the user first
- [ ] Consider an HMAC (server secret) instead of a plain SHA-256 for the message hash

## Open items (keep current; updated 2026-11-03 after [[M12 Security gate]])
1. **User action:** revoke the GitHub tokens pasted in chat earlier
2. **User action:** GitHub settings: branch protection, secret scanning, Dependabot alerts, private vulnerability reporting
3. HTTPS: local uses Caddy's own CA; a public deployment needs a real domain (`SITE_ADDRESS`) and open ports 80/443
4. No refresh tokens (fixed 60-minute sessions; no rotation or reuse detection)
5. No email verification, password reset or MFA ([[M10 Account lifecycle]])
6. No DB encryption at rest (use host disk/volume encryption); Data Protection key files unencrypted at rest; backups not scheduled or copied off-machine
7. Gmail tokens are stored with `ITokenProtector` (done 2026-11-04); a real Google connection and sender spoofing (no SPF/DKIM check) remain open, see [[M2 Gmail import]]
8. Audit log exists; no alerting or monitoring
9. Docker images scanned 2026-11-04 (`scripts/scan-images.sh`): API, web, proxy system layers clean; Caddy binary and gosu in Postgres still carry fixable Go findings (update when upstream ships); not penetration tested (browser check was functional, not a pen test)
10. Account deletion and data export not implemented ([[M10 Account lifecycle]]); AI features not built (gate exists)
11. Rate limit is per IP and not load-tested; on Docker Desktop all local requests share one address
