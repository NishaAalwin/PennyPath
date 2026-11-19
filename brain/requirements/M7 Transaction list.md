---
id: M7
release: MVP
status: done-with-gaps
verified: 159 backend + 29 frontend tests; live on compose Postgres with 10,000 rows (2026-11-03); UI verified in a real browser (Edge) 2026-11-03
---
# M7 Transaction list with search, filter and edit

**Acceptance (docs/requirements.md §7):** given 10k transactions, when I search, filter by date, category or type, or edit a transaction, then the list updates correctly and only my own transactions are affected.

## Code check
- [x] List endpoint scoped to the user — `GET /api/transactions`, `TransactionService.QueryAsync`
- [x] **Search** by where/what (case-insensitive, partial) or amount (a number matches the exact amount); `q`, max 100 chars (101 → 400, verified live); `%`, `_` and quotes are plain text (tested, and live on Postgres)
- [x] **Filters:** month, `from`/`to` date range, category (must be a known category), type Income/Expense; they combine with AND. Counts equal direct SQL counts on Postgres for search, type, category+month and date range
- [x] **Pagination server-side:** `page` ≥ 1, `pageSize` 1–100 (default 50), stable order (date, created, id), response `{items,total,page,pageSize}`; every row appears exactly once across pages (tested); bad page/size → 400
- [x] **Edit** `PUT /api/transactions/{id}`: merchant, amount, date, type (only fields sent change); owner-scoped (other user and missing → 404, row untouched, tested and live); no token → 401
- [x] **Validation:** merchant ≤ 200 chars and no control characters (may be cleared); amount > 0, ≤ 999,999,999.99, ≤ 2 decimals; date 2000-01-01 … tomorrow; type must be Income/Expense; empty request refused. Refused edits change nothing (tested)
- [x] Edit side effects: re-categorizes when the merchant/type changes unless the user chose the category; clears the row's duplicate/refund guesses and any rows that pointed at it; switching to Expense clears the refund flag; the import hash is kept so re-importing the same SMS does not bring the old row back
- [x] UI: search box, category, in/out and "All months" filters, previous/next with "x-y of n", inline edit form with server error shown — `frontend/src/TransactionList.tsx`; query building unit tested
- [x] List follows the month picked in the summary (so list and totals agree), unless "All months" is ticked
- [x] **Performance (10k rows, compose Postgres, 5 runs):** default page, a search+type+page query and a month+category query each ~0.21–0.24 s; a 25-row page is ~12 KB instead of every row
- [x] UI tested in a real browser: search (debounced), money-in filter, invalid edit shows the server message, valid edit updates row and totals, paging over 35 rows (Next, Previous, page reset on search). The browser found a real flaw (the "26-35 of 35" label ran ahead of the rows while loading); fixed by labelling the loaded page
- [ ] Editing does not re-run duplicate/refund detection for the edited row (it only clears old guesses)
- [ ] Sorting is fixed to newest first; no amount-range filter; amount search is exact only
- [ ] Manual **add** of a transaction is M11, not here
- [ ] Unpaged `ListAsync` remains in the service (for a later export and tests); the API no longer exposes it

Related: [[M5 Categorization]], [[M9 Data quality]], [[Security checklist]], [[Data minimisation]]
