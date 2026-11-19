# Foundation verification status

Last updated: 2026-11-02

| Check | Status | Notes |
|---|---|---|
| Backend restore, build (Release), test | Verified | Same commands as `ci.yml`; 0 warnings, 1 test passes |
| Frontend `npm ci`, lint, test, build | Verified on Node 24 | Default machine Node is 22.1.0, which is too old for Vite 8 (needs >= 22.12) |
| `docker compose up --build` | **Verified** | db (healthy), api and web all start; both Dockerfiles build (the frontend image runs `npm ci` and `npm run build` on Node 24) |
| API connects to PostgreSQL | **Verified** (compose Postgres 17) | `GET http://localhost:5000/api/health` returns `{"status":"healthy","database":true}` |
| Web served | **Verified** | `http://localhost:8080` returns 200, serving the built bundle |
| CORS for web origin | **Verified** | `Access-Control-Allow-Origin: http://localhost:8080` on API responses |
| React page rendering the API result in a real browser | Not verified | Headless Edge returned no DOM output; verified indirectly (bundle built with `VITE_API_URL=http://localhost:5000`, CORS header present) |
| API against the local (non-Docker) PostgreSQL 17 service | Not verified | The `pennypath` login fails because the DB/user hasn't been created yet; optional now that compose Postgres works |
| GitHub Actions CI | **Verified** (old version) | Run on push of 14be2e2 (2026-11-02): backend, frontend and docker jobs all succeeded on GitHub. Repo: https://github.com/NishaAalwin/PennyPath. The 2026-11-19 version (adds `infra` job and image scan) has not run on GitHub yet |
| CD (`deploy.yml`, replaces `cd.yml`) | Linted locally, not run | actionlint and shellcheck clean 2026-11-19; deploys each green `main` commit to the Azure VM. Needs the repo variables listed at the top of `deploy.yml`; until `AZURE_CLIENT_ID` is set it only builds, scans and pushes images |

## Fixes made during verification
- Docker Desktop could not start because WSL was the old inbox version. Fixed with `wsl --update` (WSL 3.0.1.0 installed), then restarted Docker Desktop.
- Compose Postgres host port changed from 5432 to **5433**, because the local PostgreSQL service already uses 5432.

## Not configured
- Hosting/deploy target.
- Secrets: Google OAuth client, Claude API key (needed for later milestones).
- Remote: GitHub `NishaAalwin/PennyPath` (private). Pushes from this machine need a credential with the `workflow` scope.
- Database migrations: the `Transactions` table does not exist yet.

## M1: sign-up and login (2026-11-02)

| Check | Status | Notes |
|---|---|---|
| Backend unit and API tests | Verified | 12 pass: register, validation, duplicate (case-insensitive), login, identical error for wrong password and unknown user, JWT accepted/rejected |
| Frontend tests, lint, build | Verified (Node 24) | 6 tests pass |
| Register, login, `/me` against the compose stack and Postgres | Verified with curl | 201 on register, 409 duplicate, 400 weak password, 401 bad login, `/me` 200 with token and 401 without. Password stored as an ASP.NET Identity salted hash |
| EF migration applied on startup in compose | Verified | Creates `Users` and `Transactions` tables. The one-time `fail:` log line about `__EFMigrationsHistory` is EF's normal first-run check |
| Login/sign-up forms in a real browser | Not verified | No working headless browser here; the form logic was only exercised through the API and the unit tests |
| Rate limiting (20 requests/min/IP on auth) | Not verified | Configured but not load-tested. Behind a reverse proxy the client IP needs forwarded-headers configuration |

### Known limitations / follow-ups
- The JWT is kept in `localStorage`. It is simple, but readable by any XSS. Consider httpOnly cookies later.
- No refresh tokens, email verification or password reset yet. Tokens last 60 minutes.
- The compose `Jwt__Key` default is dev-only. Set `JWT_KEY` for any real deployment.
- The first migration is named `AddUsers` but also creates `Transactions`.

## M3 + M4: parser and SMS import (2026-11-02)

| Check | Status | Notes |
|---|---|---|
| Parser unit tests (17) | Verified | Amounts, debit/credit, merchant, account, date formats; ignores OTPs, due reminders, declined and promo messages; skips "Avl bal" amounts |
| Import service tests (6) | Verified | Dedupe on re-import and within one batch, per-user isolation, month filter and ordering |
| Backend total / frontend total | 37 / 11 tests pass | Frontend lint and build clean (Node 24) |
| Compose end-to-end (curl) | Verified | Upgrade migration applied to an existing DB; import 3 of 4 (OTP reported as unread), re-import 0 new / 3 duplicates, list, month filter, per-user isolation (second user sees none), 401 without token, 400 for bad month or empty body |
| Import and transaction table UI in a real browser | Not verified | Same browser limitation as before |
| Real-world accuracy | **Not measured** | Tested only on hand-written sample messages. The requirement is >= 90% on real bank alerts, so we need a set of real (redacted) messages from the user's banks |

### Known limitations
- Dedupe uses the normalized message text. Two genuinely different transactions with identical text (no reference or time in the SMS) would collapse into one.
- A message with no date uses the import date.
- Rule-based only; there is no Claude fallback for unrecognised formats yet, and no categories (everything is "Uncategorized", M5).
- Amounts are assumed to be INR (Rs, INR or the rupee sign).
- The `AddTransactionImportFields` migration changes the `Type` column from int to text and adds a non-null `UserId`. It is safe only because no transaction rows existed. Future migrations must be written with real data in mind.

## Security hardening (2026-11-02): see docs/security.md

| Check | Status | Notes |
|---|---|---|
| Backend tests | 47 pass | Adds lockout, lockout expiry, weak/oversized passwords, `alg:none` token, security headers, body size limit |
| Dependency audit | Clean | `dotnet list package --vulnerable --include-transitive` and `npm audit`: no findings |
| Live checks on compose | Verified | Headers on API and web, CSP, non-root users, loopback-only ports, lockout (5 failures then 429, even for the right password), CORS refuses unknown origin, OpenAPI 404, compose fails without secrets |
| CodeQL, Dependabot, new CI audit steps | Not yet run | Take effect after the next push; CodeQL needs code scanning available for the repo |
| Browser behaviour under the CSP | Not verified | Only same-origin scripts/styles are in the page, so it should work, but it was not opened in a real browser |

Breaking change for local setup: copy `.env.example` to `.env` and fill in `POSTGRES_PASSWORD` and `JWT_KEY` before `docker compose up`.

## M5: rule-based categorization (2026-11-03)

| Check | Status | Notes |
|---|---|---|
| Backend tests | 70 pass | Adds categorizer rules (incl. word-boundary cases), correction persists across re-import, cross-user update returns not-found, unknown category rejected, endpoints require auth |
| Frontend tests, lint, build, `npm audit` | Verified (Node 24) | 12 tests pass; no vulnerabilities |
| Dependency audit (NuGet) | Clean | No new packages |
| Compose end-to-end (curl) | Verified | Migration `AddCategoryIsManual` applied on Postgres; import categorizes Swiggy as Food & Dining; `PUT` 204 and the change persists; unknown category 400; no token 401; other user 404 |
| Category dropdown in a real browser | Not verified | Same browser limitation as before |
| AI fallback | **Not built** | Waits for the consent design (M12). Default assumption: opt-in, merchant text only |
| Categorization accuracy (>= 85%) | **Not measured** | Keyword lists are unvalidated guesses; needs labelled real transactions |

### Known limitations
- Rows imported before this change stay "Uncategorized" (no backfill), and rule changes do not re-categorize old rows.
- The "Transfers" category exists but nothing assigns it yet (M9).

## M9: data quality (2026-11-03)

> Partly superseded the same day by the data-minimisation section below: reference/account numbers and stored unreadable messages were removed, and automatic transfer detection was dropped.

| Check | Status | Notes |
|---|---|---|
| Backend tests | 117 pass | Adds parser (reference numbers, refund/reversal wording), reconciler rules incl. false-positive guards, service behaviour, per-user isolation, caps, 401 for every new endpoint |
| Frontend tests, lint, build, `npm audit` | Verified (Node 24) | 14 tests pass; no vulnerabilities |
| Dependency audit (NuGet) | Clean | No new packages |
| Compose end-to-end (curl) | Verified | Migration `AddDataQuality` applied on Postgres. Same-reference message flagged as possible duplicate; different reference not flagged; refund linked to its purchase; own-account debit+credit marked as transfer on both rows; unreadable message listed then dismissed; other user gets 404, no token 401 |
| New UI (badges, actions, needs-review panel) in a real browser | Not verified | Same browser limitation as before |
| Cross-source (SMS vs email) dedupe | **Not verified** | Email import (M2) does not exist; rules are source-agnostic and tested on SMS |
| Accuracy of duplicate/transfer/refund rules on real messages | **Not measured** | Needs the real redacted samples from the user |

### Known limitations
- Same-day, same-merchant, same-amount purchases without a reference number are flagged as possible duplicates (user confirms).
- Equal debit/credit on two different accounts can be auto-marked a transfer if unambiguous (user can undo).
- Partial refunds and refunds imported before the purchase are not linked.
- There is no accounts entity yet; "account" is the last digits found in each message.
- The dashboard (M6) is not built; it must exclude duplicates and transfers and treat refunds as negative expense (recorded in the brain).
- (Removed later the same day: unparsed text is no longer stored. See below.)

## Data minimisation + monthly summary (2026-11-03)

User decision: read and keep only amount, where/what (merchant), date and debit/credit, in PostgreSQL; summarise by where/what.

| Check | Status | Notes |
|---|---|---|
| Backend tests | 119 pass | Parser output pinned to amount, type, date, merchant, refund flag; stored model has no account/reference/text columns; unreadable messages not stored; reconciler without account/reference; summary rules and per-user isolation |
| Frontend tests, lint, build, `npm audit` | Verified (Node 24) | 14 tests pass; no vulnerabilities |
| Migration `MinimiseStoredData` on real Postgres | **Verified** | Applied on top of a database holding rows from earlier versions. psql: tables are only `Transactions`, `Users`, `__EFMigrationsHistory`; `Transactions` has no `Account`/`Reference`; no merchant contains a 4-digit run |
| `GET /api/summary` end to end | **Verified** | Income 15,000; spent 600 (Swiggy 250 duplicate excluded; Amazon 450 minus a 100 refund); saved 14,400; by place and by category sum to total expense; bad month 400; no token 401 |
| Summary panel in a real browser | Not verified | Same browser limitation as before |
| Trend, charts, 10k-row performance | **Not done / not measured** | Remaining M6 items |

### Known limitations
- A SHA-256 of the normalised message text is still stored (to skip re-imports). It could be guessed for short structured SMS by someone holding the database; an HMAC with a server secret would fix it.
- Merchant text is free text from the message and may occasionally include a name or UPI handle part.
- The migration permanently deleted previously stored account numbers, references and unreadable-message text from the development database.
- Without reference numbers, two real same-day purchases at one merchant for the same amount are flagged as possible duplicates (the user confirms). Transfers between own accounts are marked by the user.

## M6 completion: trend, charts, performance (2026-11-03)

| Check | Status | Notes |
|---|---|---|
| Backend tests | 131 pass | Adds trend: months oldest first, empty months, each month equals its summary, year boundary, duplicates/transfers excluded, per-user, bad parameters, 401 |
| Frontend tests, lint, build, `npm audit` | Verified (Node 24) | 26 tests pass (chart math, API calls); no vulnerabilities |
| Chart palette | **Verified** | dataviz validator, light and dark: lightness band, chroma, CVD (worst adjacent ΔE 24.7 light / 26.8 dark), normal-vision floor, contrast all pass |
| Charts rendered | **Viewed** | Real components server-rendered to HTML and screenshotted in Edge, light and dark; no overlap or clipping at 480 px wide |
| Hover tooltip, keyboard focus, month picker in a real browser | Not verified | Only the static render was viewed |
| Performance, 10,000 rows | **Measured** | Compose Postgres, one user, 5 runs each over curl: summary 0.22–0.32 s, trend (6 and 12 months) 0.22–0.26 s, transaction list 0.23–0.28 s. API time on a developer laptop with Docker; browser render time not measured. Requirement: under 2 s |
| Correctness at 10k rows | **Verified** | September income and expense from the API equal a direct SQL sum to the paisa |

### Known limitations
- No texture fallback for colour-vision/print/forced-colors beyond the validated colour pair and the table view.
- `GET /api/transactions` returns every row (10,000 rows answered in ~0.25 s here); pagination belongs to M7.
- A month where refunds exceed spending has negative net spend; the chart shows no column for it, the tooltip and table show the value.

## M7: transaction list search, filter, edit, pagination (2026-11-03)

| Check | Status | Notes |
|---|---|---|
| Backend tests | 159 pass | Adds search (case, partial, amount, wildcards, quotes), filters and AND, ordering, paging (every row once), limits, edit fields/validation/side effects, per-user, 401 |
| Frontend tests, lint, build, `npm audit` | Verified (Node 24) | 29 tests pass; no vulnerabilities |
| Dependency audit (NuGet) | Clean | No new packages; no migration |
| Compose end-to-end, 10,000 rows | **Verified** | Counts for search, type, category+month and date range equal direct SQL counts; `%` and an injection-style string match nothing and delete nothing; page/pageSize/type/category/month/from/101-char search → 400; last page returns the remaining rows; edit applies and the category follows the new merchant; invalid edits → 400 with a message; other user → 404; no token → 401 |
| Timing, 10,000 rows | **Measured** | Each list query ~0.21–0.24 s over 5 runs (curl, dev laptop, Docker); default page is ~12 KB |
| Search box, paging buttons, edit form in a real browser | Not verified | Same browser limitation as before |

### Known limitations
- Editing clears old duplicate/refund guesses but does not look for new ones.
- Sorting is newest first only; there is no amount-range filter; searching a number matches the exact amount.
- `GET /api/transactions` now returns `{items,total,page,pageSize}` instead of a bare array (a breaking change for any other client; the web app was updated).

## M12 security gate (2026-11-03)

| Check | Status | Notes |
|---|---|---|
| Backend tests | 193 pass | Adds sessions (cookie flags, token not in body, logout kills a copied cookie, logout-all, other users untouched, unknown/expired session rejected), CSRF (header, fetch metadata, anonymous sign-in), audit log (recorded, owner-only, no secrets, retention, IP cap), AI consent, token protector (round trip, tamper, purpose, restart, lost key) |
| Frontend tests, lint, build, `npm audit` | Verified (Node 24) | 32 tests; no vulnerabilities |
| NuGet vulnerability scan | Clean | A critical advisory in the Data Protection version I first added (10.0.4) and a high one in a transitive package were found and fixed by moving to 10.0.14 |
| HTTPS stack | **Verified** | `docker compose up`: proxy, web, api, db healthy; only the proxy publishes a port (loopback 8443); 5000 and 8080 closed; plain HTTP to the TLS port refused |
| Cookie, CSRF, revocation, forwarded headers (curl against the real stack) | **Verified** | See security.md. Spoofed X-Forwarded-For ignored; audit IP is the gateway address 172.28.0.1 |
| Real browser (Edge via DevTools, `scripts/browser-e2e.mjs`) | **Verified** | 43 of 43, three runs in a row: the whole UI flow, hardened cookie, empty storage, CSP blocks injected inline script / blob script / cross-origin request, no violation from the app, every console error accounted for. Also closes the "not browser-tested" items for M1, M5, M6, M7, M9. Found and fixed one real flaw (page label ahead of rows) |
| Backup and restore | **Verified** | Encrypted dump + key ring; restore into a scratch DB equals live (counts, sum, content hash); wrong passphrase, live DB name, odd name, short passphrase refused; no plaintext in the encrypted files |
| Key ring persistence | **Verified** | The `dpkeys` volume holds a key the app created; directory writable by the non-root user |

### Known limitations
- Not tested: real-domain TLS (Let's Encrypt), load/rate-limit behaviour, behaviour after `docker compose down -v` (would lose the key ring and database), a real Gmail token round trip (no Gmail code yet).
- Browser checks ran in headless Edge only (no Firefox or Safari; SameSite=Strict and `__Host-` cookies behave the same in current Chrome-family browsers, unchecked elsewhere).
- Whole page has a blue-gradient light/dark theme (2026-11-04); text contrast was checked by eye in screenshots, not measured.
- The app moved to https://localhost:8443; the old ports no longer answer.

## M2 Gmail import (2026-11-04)
| Check | Result | Evidence |
|---|---|---|
| Backend tests | **Verified** | 255 pass (62 new): service flow with a fake Google, HTTP layer against a stub handler, endpoints over the real API |
| Frontend | **Verified** | 39 tests, lint, build, audit clean |
| Migration on real Postgres | **Verified** | `AddGmailConnection` applied; `GmailConnections` unique per user, FK cascade, token column is text holding ciphertext |
| Not configured | **Verified live** | status `configured:false`, connect/sync 409 with a clear message, UI explains |
| Consent address | **Verified live** (dummy client id) | scope `gmail.readonly` only, PKCE S256, offline access, state stored as a hash, no secret in the URL |
| Forged callback / no login / no CSRF header | **Verified live** | 302 to `?gmail=failed`, 401, 403 |
| Browser | **Verified** | 47/47 including the Gmail card |
| Single-use state, expiry, wrong user, extra scope, no refresh token, revoked grant, lost key ring, outage, concurrent sync, disconnect | **Verified (unit)** | `GmailServiceTests` |
| Idempotent sync | **Verified (unit)** | second sync reads nothing and adds nothing; known emails are not read again |
| Cross-source duplicate | **Verified (unit)** | SMS then email of the same payment is flagged as a possible duplicate |
| Email footers ("never share your OTP") | **Verified (unit)** | the cut keeps real alerts; a real OTP message is still ignored |
| **Real Google round trip** | **Not verified** | needs your OAuth client; nothing has talked to the real Google yet |
| **Real bank email formats** | **Not verified** | parser tested on formats I wrote from memory; accuracy on your real emails is unmeasured |

### Known limitations
- Testing mode: the grant expires after 7 days; reconnect.
- The sender list is by domain and may miss your bank; extend `GmailQuery.SenderDomains`.
- A transaction you delete can come back if its email is inside the one-day overlap of the next sync.
- Emails that look like alerts but cannot be read are re-read on every sync (they are not remembered).

## Hardening pass (2026-11-04)
| Check | Result | Evidence |
|---|---|---|
| Forged bank emails refused | **Verified (unit)** | 13 verifier cases (forged header, failed signature, look-alike domains, other bank signing) and a sync test with three forged mails |
| Backend tests | **Verified** | 269 pass |
| Frontend | **Verified** | 40 tests, lint, build, audit clean |
| Browser | **Verified** | 47/47 twice in a row (one earlier run stopped at the keyboard-focus tooltip step and passed on rerun; cause not investigated, treated as a timing flake) |
| Headers live | **Verified** | COOP and CORP present on the page; HSTS absent on localhost and present when the Host is a real domain (tested on a throwaway Caddy) |
| Logs hold no tokens | **Verified** | searched the API logs for token, code, state and secret values: only column names matched |
| Image scan | **Verified, with open items** | API, web, proxy system layers 0 fixable; Caddy binary 6 HIGH and gosu in Postgres 26 HIGH (Go runtime) remain |
| Real Google, real bank email formats, pen test | **Not verified** | unchanged |
| Whether your bank passes Gmail DKIM/DMARC | **Not verified** | skipped emails are counted in the sync result so this shows up on the first real sync |

## M10 account lifecycle (2026-11-04)
| Check | Result | Evidence |
|---|---|---|
| Backend tests | **Verified** | 348 pass (79 new): TOTP against RFC 6238 vectors, verification, reset, deletion, export, two-factor, SMTP guard, and the same flows over HTTP with verification switched on |
| Frontend | **Verified** | 54 tests, lint, typecheck, build, audit clean |
| Migration on real Postgres | **Verified** | `AddAccountLifecycle` applied; existing accounts show `EmailVerified = true`; no database default left on the column |
| Browser, real UI | **Verified** | 67/67: confirm-email screen holds a new account, link verifies and token leaves the address bar, link works once, forgot answers identically, reset form, old password dies, new works, two-factor setup (wrong code refused, right code gives 10 recovery codes), export, sign-in asks for the code, wrong code refused, deletion refused with a wrong password then done with a recovery code, deleted account cannot sign in |
| Deletion on real Postgres | **Verified** | after two browser deletions: both users gone; 0 orphan rows in Transactions, Sessions, AuditEvents, UserTokens, RecoveryCodes, GmailConnections; 2 content-free AccountDeleted events |
| Logs hold no tokens, codes or passwords | **Verified** | searched the API logs on the live stack: 0 hits |
| New image (Mailpit) | **Verified** | scanned: 0 fixable HIGH/CRITICAL |
| Real mail server | **Not verified** | only Mailpit tested |
| Two parallel requests using the same TOTP code | **Not verified / known gap** | no row lock |

### Known limitations
- Old e2e/test accounts from earlier runs remain in the local database (all marked verified). They are harmless test rows.
