---
id: M6
release: MVP
status: not-started
---
# M6 Monthly dashboard

**Requirement:** per month: total income, total expense, savings, category breakdown, trend.

**Acceptance:** income, expense, savings and category totals **match the transaction list exactly**.

## Rules inherited from [[M9 Data quality]]
- Exclude rows with `DuplicateOfId != null` and rows with `IsTransfer` from income and expense totals.
- A refund (`IsRefund`) **reduces expense**; it is never income, whether or not it is linked to a purchase.
- The transaction list must show the same rows the totals use, so a user can reconcile them.

## Code check (must hold when built)
- [ ] Totals apply the M9 exclusion and refund rules above, with tests for each
- [ ] Totals computed server-side from the same query/filter as the list; test asserting dashboard == sum of list
- [ ] Money uses `decimal`, never float; currency INR
- [ ] Month boundaries defined and tested (timezone, first/last day)
- [ ] Per-user scoping
- [ ] **Performance:** < 2 s with 10k transactions (measure; add index on user+date if needed)
- [ ] Empty month handled
- [ ] Charts accessible (not colour-only) — use the `dataviz` skill

Depends on [[M5 Categorization]]. See [[Non-functional requirements]].
