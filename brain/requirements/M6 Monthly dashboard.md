---
id: M6
release: MVP
status: partial
verified: 119 backend + 14 frontend tests; live on compose Postgres (2026-11-03); browser UI untested
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
- [ ] **Trend** (compare months / last N months) — not built
- [ ] **Performance:** < 2 s with 10k transactions — not measured (index on user+amount+date exists; summary loads the month's rows into memory)
- [ ] Charts (only tables so far; use the `dataviz` skill, not colour-only)
- [ ] Message with no date uses the import date (parser fallback), which can put it in the wrong month
- [ ] Browser check of the panel

Depends on [[M5 Categorization]]. See [[Non-functional requirements]].
