# Check Log

Newest first. One entry per task: date · task · notes checked · result · gaps.

## 2026-11-03 · M12 security gate
- Notes checked: [[M12 Security gate]], [[Security checklist]], [[Non-functional requirements]], [[M1 Sign up and login]] (cookie sessions replace the stored token), [[M2 Gmail import]] and [[M10 Account lifecycle]] (hand-offs), [[Data minimisation]] (audit log holds no financial data).
- Result: **pass, with gaps.** Backend 193 tests, frontend 32 tests, lint, build, `npm audit` clean; NuGet scan clean after fixing a vulnerable Data Protection version I had added (10.0.4 → 10.0.14). Two migrations' worth of schema (sessions, audit events, AI consent) applied on the real Postgres.
- Live over HTTPS: cookie flags, no token in body, CSRF 403s (no header, cross-site), logout then replay → 401, spoofed X-Forwarded-For ignored, direct ports closed, key-ring volume writable by the non-root user and already holding a real key. Backup/restore round trip identical (counts, sum, content hash), failure cases refused, no plaintext in encrypted files.
- **Real browser (Edge over DevTools): 43/43 checks, three consecutive runs**, which closes the long-standing "UI not browser-tested" gap for M1, M5, M6, M7, M9: sign-up, import, numbers, chart hover and keyboard focus, search, filters, invalid and valid edit, category change, "Not a duplicate", "Mark as transfer", paging over 35 rows, privacy panel, logout, re-login, wrong password, CSP probes. Screenshots viewed in light and dark. **The browser found a real UI flaw** (the page label "26-35 of 35" ran ahead of the rows while loading); fixed and re-verified.
- Mistakes caught on the way: a heredoc mangled test regexes (fixed); `Git Bash` rewrote `/keys` so the first key-ring backup was empty (fixed with MSYS_NO_PATHCONV and re-verified with a real archive listing); my first browser heuristics produced false failures (the app was right; the test was fixed, not the assertions loosened to hide anything).
- Gaps: refresh tokens, DB/key-file encryption at rest, backup scheduling and off-machine copies, audit alerting, Docker image scanning, pen test, real-domain TLS, user actions on GitHub. Breaking change: app is now at https://localhost:8443; login responses carry no token.

## 2026-11-03 · M7 transaction list: search, filter, edit, pagination
- Notes checked: [[M7 Transaction list]], [[Security checklist]], [[Non-functional requirements]], [[M5 Categorization]] and [[M9 Data quality]] (edit side effects), [[Data minimisation]] (no new stored fields).
- Result: **pass, with gaps.** Backend 159 tests, frontend 29 tests, lint, build, `npm audit` (high) clean; no new packages; no migration. Live on compose Postgres with 10,000 rows: search/type/category+month/date-range counts equal direct SQL counts; wildcard and injection-style search return 0 and delete nothing; page, size, type, category, month, date and 101-character search all return 400; edit works, validation messages shown, other user 404, no token 401; each list query ~0.21–0.24 s. Test users and rows were deleted afterwards.
- Security walk: edit and list are `[Authorize]`, owner from the token, owner-scoped queries, server-side validation and caps (search 100, page size 100, merchant 200, amount range, date range), EF-parameterised search, no secrets, no new data stored.
- Gaps: UI not browser-tested; edit does not re-run duplicate/refund detection; fixed sort order; no amount-range filter.
- Mistake caught: my first long-search check was truncated by the test script, so I reran it properly (101 chars → 400).

## 2026-11-03 · M6 completion: trend, charts, performance
- Notes checked: [[M6 Monthly dashboard]], [[Non-functional requirements]], [[Security checklist]], [[Data minimisation]] (summary uses only stored fields), [[M9 Data quality]] (exclusion rules reused).
- Result: **pass, with gaps.** Backend 131 tests, frontend 26 tests, lint, build, `npm audit` (high) clean. New `GET /api/trend` is `[Authorize]`, owner-scoped, validated (2–12 months). Palette run through the dataviz validator in light and dark (all checks pass). Charts rendered with the real components and viewed in Edge, light and dark. With 10,000 rows in the compose Postgres each endpoint took ~0.22–0.32 s; September totals matched a direct SQL sum exactly. Test user and rows were deleted afterwards.
- Gaps: tooltip/focus/pointer not tested in a real browser; no texture fallback; no direct labels on columns; chart card self-themed because the app has no theme; list endpoint returns all rows (pagination is M7).

## 2026-11-03 · Data minimisation + monthly summary (user request)
- User request: read only amount and where/what, summarise it, save in PostgreSQL. Decisions with the user: keep amount, where/what, date, debit/credit; stop storing unreadable messages; summary = totals by where/what (M6).
- Notes checked: [[Data minimisation]] (new), [[M6 Monthly dashboard]], [[M9 Data quality]] (revised), [[M3 Transaction parser]], [[M4 SMS import]], [[Security checklist]], [[Non-functional requirements]].
- Result: **pass, with gaps.** Backend 119 tests, frontend 14 tests, lint, build, `npm audit` (high) clean. Live on compose Postgres: migration `MinimiseStoredData` upgraded a DB that held old rows; psql shows no `UnparsedMessages` table and no `Account`/`Reference` columns; stored rows hold only amount/merchant/date/type/category/flags/hash. Summary checked by hand: 15,000 income, 600 spent (Swiggy 250 duplicate excluded, Amazon 450 − 100 refund), 14,400 saved; bad month 400; no token 401.
- Security walk: new `/api/summary` is `[Authorize]`, owner from token, owner-scoped (tests); fewer data kept; no new dependencies.
- Reverses the M9 exception (stored unreadable text) and drops automatic transfer detection; both recorded.
- Gaps: no trend/charts, performance unmeasured, hash of message still stored (HMAC suggested), merchant text may carry names, UI not browser-tested, cross-source dedupe untested.

## 2026-11-03 · M9 data quality
- Notes checked: [[M9 Data quality]], [[M4 SMS import]], [[M3 Transaction parser]], [[M5 Categorization]] (refund/transfer set categories), [[M6 Monthly dashboard]] (rules recorded), [[Security checklist]], [[Non-functional requirements]].
- Result: **pass, with gaps.** Backend 117 tests, frontend 14 tests, lint, build, `npm audit` (high) clean; NuGet scan clean (no new packages). Live on compose Postgres: migration `AddDataQuality` applied; same-ref second message flagged (possibleDuplicates 1); different ref not flagged; refund linked to purchase; transfer pair marked on both rows; unreadable message listed; other user 404, no token 401.
- Security walk: all 5 new endpoints `[Authorize]`, owner from token, owner-scoped queries, 401 tests; no secrets; no new dependencies; stored-text exception recorded in the Security checklist.
- Decision made with the user: store capped unparsed text (exception to "no raw bodies"); build all of M9 in one pass.
- Gaps: M6 must apply the exclusion/refund rules (not built); no accounts entity; email source untested (M2 missing); real-message accuracy unmeasured; partial refunds unsupported; UI not browser-tested.
- Not re-verified: M1, M8 ticks (exercised only through the test run); stale `docs/security.md` refresh done in the same commit.

## 2026-11-03 · M5 categorization (rules only)
- Notes checked: [[M5 Categorization]], [[Security checklist]], [[Non-functional requirements]], [[M3 Transaction parser]] (parser unchanged), [[M4 SMS import]] (import now categorizes).
- Result: **pass for the rules part.** Backend 70 tests, frontend 12 tests, lint, build, `npm audit` (high) and NuGet vulnerability scan all clean. Live on compose Postgres: migration applied, import categorized Swiggy as Food & Dining, correction saved, bad category 400, no token 401, other user 404.
- Security walk: new endpoints `[Authorize]`, owner from token, ownership-scoped query, fixed category list, no secrets, no data sent out, no new dependencies.
- Gaps: AI fallback not built (by design); accuracy unmeasured; no backfill of old rows; dropdown not tested in a real browser; Docker `api`+`db` containers were started for the check.
- Not re-verified in this task: M1, M3, M4, M8 ticks (only exercised indirectly by the full test run).

## 2026-11-03 · Requirements review applied
- Added M9–M12, measurable NFR targets, acceptance criteria for all MVP items and open decisions 4–7 to `docs/requirements.md` and the brain.
- Docs/brain only; no code changed, nothing re-verified. Per-note acceptance criteria for M1/M3/M4/M5/M7 live in `docs/requirements.md` §7; the notes' Code check lists were not individually extended.
- Open: decisions 4–7 await the user.

## 2026-11-03 · Create the brain vault
- Notes checked: n/a (vault created from `docs/requirements.md`, `docs/security.md`, `docs/verification.md`).
- Initial Code check ticks reflect the state recorded in those docs at commit 98faa8b; they were **not** re-run when the vault was created. First real task should re-verify them.

## 2026-11-04 · Blue-gradient UI restyle
- Notes checked: [[M6 Monthly dashboard]] (summary/chart/stat tiles), [[M7 Transaction list]] (table, filters, paging, edit), [[M1 Sign up and login]] (form), [[M12 Security gate]] (CSP, forms), [[Non-functional requirements]].
- Change: new `frontend/src/index.css` theme (blue gradient page, cards, stat tiles, gradient buttons, light + dark, narrow-screen layout); class names replace most inline styles; page title "PennyPath"; no behaviour, API or dependency change. System fonts only, styles from `'self'`, so the CSP is unchanged.
- Evidence: 32 frontend tests, lint and build pass; web image rebuilt; `scripts/browser-e2e.mjs` 43/43 (two checks adapted to the new stat-tile markup); screenshots viewed in light, dark and 390 px.
- Gaps: contrast not measured by a tool; the transactions table scrolls sideways on phones; Android/Safari/Firefox not viewed.

## 2026-11-04 · Tabs for each section
- Notes checked: [[M6 Monthly dashboard]], [[M7 Transaction list]], [[M12 Security gate]] (privacy panel, CSP).
- Change: Summary, Income and spending by month, Transactions (import above the list) and Privacy and security are separate tabs (`Tabs.tsx`); the month picker moved above the tabs and drives summary, chart and list. No API, data or dependency change.
- Evidence: 32 frontend tests, lint, build; web image rebuilt; `scripts/browser-e2e.mjs` 46/46 (tab existence, single visible panel, arrow-key navigation added; steps now switch tabs); screenshots of each tab and at 390 px viewed.
- Gaps: selected tab is not remembered after reload; Safari/Firefox/mobile devices not viewed.

## 2026-11-04 · M2 Gmail import
- Notes checked before coding: [[M2 Gmail import]], [[Data minimisation]], [[M12 Security gate]], [[Security checklist]], [[Non-functional requirements]].
- Built: Google OAuth (read-only scope, PKCE, single-use hashed state), encrypted refresh token via `ITokenProtector`, bank-email search, in-memory parsing with footer cut, repeat-proof sync keyed on a hash of the message id, disconnect with revoke, audit events, rate limit, UI card, migration `AddGmailConnection`.
- Evidence: 255 backend tests (62 new), 39 frontend tests, lint/build/audit clean; migration and endpoints live on compose Postgres; consent address inspected with a dummy client id (scope, PKCE, no secret in URL); forged callback → `?gmail=failed`, no login → 401, no header → 403; browser 47/47.
- Security walk: every endpoint but the Google return address has `[Authorize]` and user-scoped queries; cross-user tests; no token or secret in any response, log or URL; CSRF header; no new package; docs/security.md updated with what is not done.
- NOT verified: a real Google connection (needs the user's OAuth client), real bank email formats and accuracy. Marked "built, not verified against real Google".
- Open: sender list by domain; no SPF/DKIM check; Testing-mode 7-day expiry; Google restricted-scope verification not started; unreadable emails re-read each sync.

## 2026-11-04 · Security hardening pass (user: "this should be very secure")
- Notes checked: [[M2 Gmail import]], [[M12 Security gate]], [[Security checklist]], [[Non-functional requirements]].
- Did: (1) refuse forged bank emails (Gmail DKIM/DMARC verdict, Gmail-written header only, domain must be on the list and match From); (2) COOP/CORP everywhere and HSTS on real domains; (3) SQL logging off; (4) image scan with Trivy, base images refreshed, `apk upgrade` for web and proxy, proxy now built from `proxy/Dockerfile`, `scripts/scan-images.sh` added; (5) checked logs hold no tokens.
- Evidence: 269 backend tests, 40 frontend tests, lint/build/audit clean, browser 47/47 twice (one earlier run flaked at the keyboard-focus step), live header checks, image rescan.
- Still open (not claimed fixed): Caddy binary and gosu Go findings; not pen-tested; not tested with real Google; disk encryption and off-machine backups; MFA and password reset (M10).

## 2026-11-04 · M10 Account lifecycle (user: "1 first": reset, verification, two-factor, deletion, export)
- Notes checked before coding: [[M10 Account lifecycle]], [[M1 Sign up and login]], [[M12 Security gate]], [[Security checklist]], [[Non-functional requirements]]. Scope and decisions written into the M10 note first.
- Built: email verification (default policy blocks unverified), password reset, TOTP two-factor with recovery codes, export, delete-my-account, queued SMTP with a Mailpit catcher, audit events, rate limits and CSRF coverage for the new anonymous endpoints, UI for all of it, migration `AddAccountLifecycle` (existing users grandfathered as verified).
- Evidence: 348 backend tests (79 new, TOTP against RFC vectors), 54 frontend tests, lint/typecheck/build/audit clean; migration, deletion and logs checked on real Postgres; browser 67/67 (flows driven through the real UI, emails read from Mailpit); image scan clean for API, web, Mailpit.
- Test mistakes caught and fixed: merchant case in two "does not contain" assertions (one was vacuous), a flash of the verify screen after confirming (fixed in the app).
- Security walk: every new endpoint authorised or deliberately anonymous with CSRF + rate limit; no enumeration at sign-in or reset; hashed tokens; secrets encrypted with the key ring; no new NuGet package (SmtpClient is built in); one new Docker image, pinned and scanned.
- NOT verified: a real SMTP server; two simultaneous uses of one TOTP code. Open: sign-up reveals existing emails; no change-password/email while signed in; no QR code.

## 2026-11-19 · M8 complete CI/CD workflows (user: "Add the complete CI/CD YAML")
- Notes checked before coding: [[M8 Docker and CI]], [[Constraints and decisions]] #3, [[Security checklist]], [[Non-functional requirements]]. New Code check items added to M8 first.
- Built: `ci.yml` now has an `infra` job (actionlint, shellcheck, `az bicep build`, compose config for local and production) and a `docker` job that builds like deploy and Trivy-scans (fixable CRITICAL blocks, HIGH reported); per-PR cancellation, timeouts, `persist-credentials: false`. `deploy.yml`: skips a commit if main has moved on (out-of-order CI), shared scan script, timeouts. `scan-images.sh`: `FAIL_ON` setting. `vm-deploy.sh`: pulls with `.env.new` before swapping `.env`, checks each running container's image tag, SC2015 fix.
- Evidence (local, Docker): actionlint 1.7.7 clean on all three workflows; shellcheck 0.10.0 clean on all four scripts; `docker compose config` passes for local and production files, and with `--env-file .env.new` resolves images to the SHA tag; `FAIL_ON=CRITICAL scan-images.sh` on local images exits 0 with HIGH listed.
- NOT verified: Bicep build (no `az` locally); any GitHub run of CI or Deploy; VM-side behaviour on a real VM. Gap: actions pinned by tag, not SHA. Fixable HIGH findings in all three images (see M8).
