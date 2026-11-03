---
id: M5
release: MVP
status: partial-rules-done
verified: 70 backend + 12 frontend tests; live on compose Postgres (2026-11-03); browser UI untested
---
# M5 Auto-categorize transactions

**Requirement:** rules first, AI fallback; user can correct a category.

**Acceptance:** a transaction gets a category on import (rule first, AI only if the user opted in); a user's change is kept and not overwritten by later imports.

## Code check
- [x] Rule-based categorizer runs first, unit tested — `Application/Categorization/RuleCategorizer.cs`, `tests/.../CategorizationTests.cs`
- [x] Word-boundary matching (e.g. "Coca Cola" is not "ola"/Transport) — tested
- [x] Categorizer sees the merchant text only, runs locally, nothing sent out
- [x] User correction persists and is not overwritten — `Transaction.CategoryIsManual`, `TransactionService.UpdateCategoryAsync`; re-import test
- [x] Category must be one of a fixed list (400 otherwise) — `Categories.All`
- [x] Update scoped to the owning user; other user gets 404 (unit test + live)
- [x] Endpoints require auth (401 test for `GET /api/categories` and `PUT .../category`)
- [x] Migration `AddCategoryIsManual` applied on real Postgres
- [x] UI: category dropdown per row — `frontend/src/TransactionsPanel.tsx`
- [ ] **AI (Claude API) fallback** — deliberately not built; waits for opt-in/consent design in [[M12 Security gate]] (default assumption: opt-in, merchant text only)
- [ ] **≥ 85% accuracy** on 100 labelled transactions ([[Non-functional requirements]]) — unmeasured; keyword lists are my guesses for Indian merchants
- [ ] Re-categorize existing rows when rules change (only new imports are categorized)
- [ ] Browser check of the dropdown

## Known gaps
- Existing rows from before this change keep "Uncategorized" (no backfill).
- Keyword list is small and unvalidated against real data; needs real messages.
- No "Transfers" detection yet — the category exists but nothing assigns it ([[M9 Data quality]]).

Feeds [[M6 Monthly dashboard]] and [[M7 Transaction list]].
