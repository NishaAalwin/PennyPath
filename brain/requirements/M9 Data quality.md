---
id: M9
release: MVP
status: done-with-gaps
verified: 119 backend + 14 frontend tests; live on compose Postgres (2026-11-03); flag/refund/transfer actions verified in a real browser (2026-11-03)
---
# M9 Data quality

**Requirement:** duplicates, own-account transfers, refunds and unparseable messages are handled so totals are trustworthy.

**Acceptance:** same payment via SMS and email appears once; a payment marked as an own-account transfer is not income or expense; a refund reduces the matching expense.

**Revised 2026-11-03 by [[Data minimisation]]:** account and reference numbers are no longer read, so the rules below use only date, amount, direction and merchant.

## How it works (code: `Application/Reconciliation/TransactionReconciler.cs`, `Infrastructure/Transactions/TransactionService.cs`)
Flags only; **nothing is deleted automatically**, and every decision can be undone by the user.
- **Duplicates:** same direction, amount, day and a known, identical merchant is flagged as a possible duplicate (`DuplicateOfId`). Unknown merchants are never flagged. The user can say "Not a duplicate" or delete the row.
- **Refunds:** the parser flags refund/reversal wording (`IsRefund`, forced to Income). It links (`RefundOfId`) to an earlier expense with the same merchant and exact amount within 60 days, one refund per expense, and takes that expense's category.
- **Transfers:** marked **by the user** ("Mark as transfer" / "Not a transfer"). Automatic pairing was removed because it needed account numbers.
- **Unreadable messages:** reported once in the import response; **not stored**.

## Rules the summary follows (see [[M6 Monthly dashboard]])
Exclude `DuplicateOfId != null` and `IsTransfer`; a refund reduces expense and is never income.

## Code check
- [x] Duplicate rule unit tested incl. near misses (other amount/merchant/day/direction, unknown merchant)
- [x] Re-importing the same text creates no duplicates ([[M4 SMS import]]); "not a duplicate" decision survives re-import
- [x] Refund links to the original expense; one refund per expense; window and amount rules tested
- [x] Transfers marked/unmarked by the user; manual category kept; never marked automatically (tested)
- [x] Unreadable messages are reported to the user at import and not stored ([[M3 Transaction parser]])
- [x] Per-user scoping on all of it; cross-user tests (unit and live: 404); all endpoints `[Authorize]` (401 tests)
- [x] Migrations applied on real Postgres
- [x] UI: status badges + actions — `frontend/src/TransactionsPanel.tsx`
- [x] Exclusion rules applied in the summary, with tests ([[M6 Monthly dashboard]])
- [ ] Cross-**source** dedupe tested with real email text — email import (M2) doesn't exist; rules are source-agnostic, tested on SMS only
- [ ] Automatic own-account transfer detection — **dropped** by [[Data minimisation]]; the acceptance wording now says "marked by the user"
- [ ] Accounts modelled per user — **dropped** (no account data kept)
- [ ] Rule accuracy on real messages (false-positive duplicates) — unmeasured
- [ ] Partial refunds and a refund imported *before* its purchase are not linked (they still reduce expense in the summary)
- [x] Browser check of the UI: the duplicate row shows its two actions; "Not a duplicate" moves spending 650 → 900; "Mark as transfer" takes it out again (900 → 650) and shows "Not a transfer"; the refund row is labelled

## Known gaps and risks
- Two real purchases at the same merchant, same amount, same day are flagged as possible duplicates (the user confirms). Without reference numbers this cannot be told apart.
- Duplicates are missed when one message has no readable merchant.
- Rows imported before the minimisation change lost their account/reference data (dropped by migration).
