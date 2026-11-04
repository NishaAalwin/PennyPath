---
id: M6
release: MVP
status: done-with-gaps
verified: 131 backend + 26 frontend tests; live on compose Postgres incl. 10,000-row timing (2026-11-03); charts rendered and viewed in light and dark; hover, keyboard focus and the numbers verified in a real browser (Edge)
---
# M6 Monthly dashboard

**Requirement:** per month: total income, total expense, savings, category breakdown, trend.

**Acceptance:** income, expense, savings and category totals **match the transaction list exactly**.

User decision 2026-11-03 ([[Data minimisation]]): "summarised" means totals by **where/what** plus income, expense and savings.

## Rules inherited from [[M9 Data quality]]
- Exclude rows with `DuplicateOfId != null` and rows with `IsTransfer` from income and expense totals.
- A refund (`IsRefund`) **reduces expense**; it is never income, whether or not it is linked to a purchase.
- The transaction list shows the same rows the totals use, plus an "excluded" count so a user can reconcile them.

## Code check
- [x] Totals apply the M9 exclusion and refund rules, with tests for each — `Application/Summary/SummaryCalculator.cs`
- [x] Computed server-side, `GET /api/summary?month=yyyy-MM` (default current month); test asserts summary == sum of the listed rows
- [x] By where/what (merchant) and by category; both sum to total expense (tested); unknown merchant shown as "Unknown"
- [x] Money uses `decimal`, INR
- [x] Per-user scoping (test); endpoint requires auth (401 test); bad month → 400
- [x] Empty month handled
- [x] Month = the date taken from the message (calendar month, no timezone conversion)
- [x] UI: month picker, totals, "Where it went", "By category" — `frontend/src/SummaryPanel.tsx`
- [x] **Trend:** `GET /api/trend?months=2-12&to=yyyy-MM` (default 6, ending this month), oldest first, empty months included; each month equals that month's summary (tested); duplicates/transfers excluded, per-user, bad params → 400, no token → 401
- [x] **Performance:** with 10,000 rows for one user in the compose Postgres, `/api/summary`, `/api/trend` (6 and 12 months) and `/api/transactions` each answered in about 0.22–0.32 s over 5 runs (curl, dev laptop, Docker). That is API time, not browser render. Totals matched a direct SQL sum exactly.
- [x] **Charts** built with the `dataviz` skill: grouped columns for income vs spent (categorical slots 1–2, validated in light and dark: CVD ΔE ≥ 24.7, contrast ≥ 3:1), ranked horizontal bars (one hue, largest first, top 8 + "Other") for where/category, legend, "Show as table" under each, dark mode, 24px bar cap, 4px rounded data end, 2px pair gap — `frontend/src/TrendChart.tsx`, `BarList.tsx`, `charts.css`, `chartMath.ts` (unit tested)
- [x] Rendered to HTML and viewed in Edge, light and dark: no overlap or clipping at 480px
- [x] Hover tooltip, keyboard focus and pointer behaviour tested in a real browser: tooltip shows income/spent/saved on pointer and on keyboard focus and hides on leave; summary numbers match (Edge, `scripts/browser-e2e.mjs`)
- [ ] No texture fallback (opt-in backup channel in the skill) and no direct labels on columns; identity rests on legend + tooltip + table
- [ ] Negative net spend in a month (refunds larger than spending) is drawn as no column; the tooltip and table show the real value
- [x] Page-wide light/dark blue-gradient theme added 2026-11-04 (`frontend/src/index.css`); chart cards sit on the theme surface. Browser 43/43 re-run; screenshots checked in light, dark and 390 px
- [x] Sections are tabs (2026-11-04): Summary, Income and spending by month, Transactions (import + list), Privacy and security; one shared month picker; panels stay mounted so filters and drafts survive switching; ARIA tabs with arrow/Home/End keys — `frontend/src/Tabs.tsx`, `TransactionsPanel.tsx`; browser 46/46
- [ ] Message with no date uses the import date (parser fallback), which can put it in the wrong month
- [x] Browser check of the panel (month picker, totals, bars, chart, tables)

Depends on [[M5 Categorization]]. See [[Non-functional requirements]].
