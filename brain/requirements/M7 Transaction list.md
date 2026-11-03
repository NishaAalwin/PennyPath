---
id: M7
release: MVP
status: partial
---
# M7 Transaction list with search, filter and edit

## Code check
- [x] List endpoint scoped to the user — `Api/Controllers/TransactionsController.cs`
- [x] List UI — `frontend/src/TransactionsPanel.tsx`
- [ ] Search
- [ ] Filter (date, category, type)
- [ ] Edit (category, amount, merchant) with ownership check
- [ ] Pagination/limits server-side (10k-row performance NFR)
- [ ] Input validation + length limits on edit

Related: [[M5 Categorization]], [[Security checklist]]
