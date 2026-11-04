---
id: M12
release: MVP
status: done-with-gaps
blocks: M2
verified: 193 backend + 32 frontend tests; live over HTTPS on the compose stack; 43 real-browser checks in Edge, run three times (2026-11-03)
---
# M12 Security gate before Gmail

**Requirement:** the items that make Gmail tokens safe to hold must be done before [[M2 Gmail import]] ships.

## Code check
- [x] **HTTPS in front of the app; `ForwardedHeaders:KnownProxies` set.** Caddy proxy is the only published port (`https://localhost:8443`, loopback); API and web have no host ports (5000/8080 closed, verified); known proxy `172.28.0.10`; a spoofed `X-Forwarded-For` did not reach the audit log. `proxy/Caddyfile`, `docker-compose.yml`
- [x] **JWT moved out of `localStorage` to an HttpOnly SameSite cookie with CSRF protection.** `__Host-pp_session` (HttpOnly, Secure, SameSite=Strict, Path=/, no Domain); never in a response body; header check + `Sec-Fetch-Site` check → 403 (verified live and in unit tests); browser confirmed `document.cookie` empty and storage empty
- [x] **Token revocation.** Server-side `Sessions`; logout and "sign out of all devices"; expired/unknown sessions rejected; replay of a copied cookie → 401 after logout (verified live)
- [ ] **Refresh strategy** — none by design: fixed 60-minute sessions, sign in again. No rotation or reuse detection. Documented
- [x] **Data Protection key ring persisted; encryption service for stored secrets.** `ITokenProtector` over ASP.NET Data Protection; key ring on the `dpkeys` volume (writable by the non-root user; the app already created a real key there); round trip, tamper, wrong purpose, restart and lost-key tests
- [x] **Gmail refresh tokens actually encrypted with it** — done in [[M2 Gmail import]] (2026-11-04): only `ITokenProtector`, ciphertext verified in tests
- [x] **AI consent screen and opt-out.** Off by default; `GET/PUT /api/privacy/ai`; UI checkbox with plain-language text; audit trail; `IAiConsent` is the gate
- [ ] **Data minimisation enforced in code for AI** — no AI feature exists, so nothing to enforce yet; the gate only works if each future AI feature calls it ([[M5 Categorization]] AI fallback must check `IAiConsent` and send merchant text only)
- [x] **Backups plan, tested.** `scripts/backup.sh` / `restore.sh` (AES-256 + PBKDF2) for the database and the key ring; restore into a scratch DB matched live exactly; wrong passphrase / live name / odd name refused; no plaintext in the encrypted files
- [ ] **Database encryption at rest** — not provided by PostgreSQL here; needs host disk/volume encryption (documented in `docs/operations.md`). Key ring files also unencrypted at rest. Backups not scheduled or copied off-machine
- [x] **Audit log** for sign-up, sign-in, failure, lockout, sign-out, consent changes (type, time, IP, account; 90-day retention; users can see their own). Nothing watches it (no alerting)
- [x] **CSP and forms checked in a real browser.** Edge via DevTools: 43/43 checks incl. forms, charts, list, edit, privacy panel; CSP blocked an injected inline script, a blob script and a cross-origin request; the app itself caused no violation; every console error was one I provoked. Script kept as `scripts/browser-e2e.mjs`
- [ ] **GitHub settings done by the user** (branch protection, secret scanning, Dependabot alerts, private vulnerability reporting, revoke the pasted tokens) — user action, listed in `docs/operations.md` §6
- [x] Dependency scan clean (a vulnerable Data Protection version I first added was caught and moved to 10.0.14)

## Known gaps
- Refresh tokens/sliding sessions, MFA, email verification and password reset ([[M10 Account lifecycle]]) are not built.
- No alerting or monitoring on the audit log.
- Local HTTPS uses Caddy's own CA; a real domain is needed for a public deployment (hosting undecided).
- Docker images are not scanned; the stack was not penetration tested.
- Dark mode now covers the whole page (blue-gradient theme, 2026-11-04); contrast was looked at in screenshots, not measured with a tool.
- The browser script is not in CI.

See [[Security checklist]] for the full open list.
