---
id: M9
release: MVP
status: done-with-gaps
verified: 117 backend + 14 frontend tests; live on compose Postgres (2026-11-03); browser UI untested
---
# M9 Data quality

**Requirement:** cross-source duplicates, own-account transfers, refunds and unparseable messages are handled so totals are trustworthy.

**Acceptance:** same payment via SMS and email appears once; own-account transfer is not income or expense; a refund reduces the matching expense.

## How it works (code: `Application/Reconciliation/TransactionReconciler.cs`, `Infrastructure/Transactions/TransactionService.cs`)
Flags only; **nothing is deleted automatically**, and every decision can be undone by the user.
- **Duplicates:** same type + amount, then either the same bank reference (any date) or, if a message has no reference, the same day + compatible account + compatible merchant. Two *different* references = two real payments. A flagged row gets `DuplicateOfId`.
- **Refunds:** the parser flags refund/reversal wording (`IsRefund`, forced to Income). It links (`RefundOfId`) to an earlier expense with the same merchant and exact amount within 60 days, one refund per expense, and takes that expense's category.
- **Transfers:** a debit and a credit of the same amount within 1 day on two *different known* accounts, and only when each is the other's **only** candidate. Both get `IsTransfer` and category Transfers. The user can mark/unmark.
- **Needs review:** unreadable messages are stored (≤ 500 chars, ≤ 200 per user, deduped) in `UnparsedMessages`, removed when the user dismisses them or the message later parses.

## Rule for M6 totals (must be followed there)
Exclude rows with `DuplicateOfId != null` and `IsTransfer`. Treat a refund as a **reduction of expense** (not income). Unlinked refunds also reduce expense in their own month. See [[M6 Monthly dashboard]].

## Code check
- [x] Dedupe key works across sources (reference or amount+date+account+merchant), unit tested (reconciler tests)
- [x] Re-importing the same text creates no duplicates ([[M4 SMS import]]); "not a duplicate" decision survives re-import
- [x] Transfer detection between own accounts; ambiguous / same-account / unknown-account cases not marked
- [x] Refund links to or offsets the original expense; one refund per expense; window and amount rules tested
- [x] Unparseable messages land in a needs-review list; nothing silently dropped ([[M3 Transaction parser]]); capped and deletable
- [x] Per-user scoping on all of it; cross-user tests (unit and live: 404)
- [x] All new endpoints `[Authorize]` (401 tests)
- [x] Migration `AddDataQuality` applied on real Postgres
- [x] UI: status badges + actions, needs-review panel — `frontend/src/TransactionsPanel.tsx`
- [ ] Exclusion rules actually applied in totals — **belongs to M6, not built yet**
- [ ] Accounts modelled as a proper per-user entity (only the last-digits string per transaction exists; user cannot name accounts or declare "this is mine")
- [ ] Cross-**source** dedupe tested with real email text — email import (M2) doesn't exist; the rules are source-agnostic and tested on SMS only
- [ ] Rule accuracy on real messages (false-positive duplicates/transfers) — unmeasured
- [ ] Partial refunds (amount differs) and a refund imported *before* its purchase are not linked
- [ ] Browser check of the new UI

## Known gaps and risks
- Same-day, same-merchant, same-amount purchases with **no reference** will be flagged as possible duplicates (user clicks "Not a duplicate").
- A coincidental debit/credit of equal amounts on two different accounts could be marked a transfer (undoable; only when unambiguous).
- Rows imported before this change have no `Reference`, so they only match by the fuzzy rule.
- **Deliberate exception to the NFR "no raw bodies stored":** unparsed text of messages the user pasted is kept (capped, deletable, removed with the account) so the user can review them. Does not apply to email bodies; M2 must not reuse it. See [[Security checklist]].
