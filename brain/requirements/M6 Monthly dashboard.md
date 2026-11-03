---
id: M6
release: MVP
status: not-started
---
# M6 Monthly dashboard

**Requirement:** per month: total income, total expense, savings, category breakdown, trend.

**Acceptance:** income, expense, savings and category totals **match the transaction list exactly**.

## Code check (must hold when built)
- [ ] Totals computed server-side from the same query/filter as the list; test asserting dashboard == sum of list
- [ ] Money uses `decimal`, never float; currency INR
- [ ] Month boundaries defined and tested (timezone, first/last day)
- [ ] Per-user scoping
- [ ] **Performance:** < 2 s with 10k transactions (measure; add index on user+date if needed)
- [ ] Empty month handled
- [ ] Charts accessible (not colour-only) — use the `dataviz` skill

Depends on [[M5 Categorization]]. See [[Non-functional requirements]].
