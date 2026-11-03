# Check Log

Newest first. One entry per task: date · task · notes checked · result · gaps.

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
