---
id: M9
release: MVP
status: not-started
---
# M9 Data quality

**Requirement:** cross-source duplicates, own-account transfers, refunds and unparseable messages are handled so totals are trustworthy.

**Acceptance:** same payment via SMS and email appears once; own-account transfer is not income or expense; a refund reduces the matching expense.

## Code check (must hold when built)
- [ ] Dedupe key works across sources (amount + date window + account + merchant), unit tested
- [ ] Re-importing the same text creates no duplicates ([[M4 SMS import]])
- [ ] Transfer detection between the user's own accounts; excluded from [[M6 Monthly dashboard]] totals
- [ ] Refund/reversal links to or offsets the original expense
- [ ] Unparseable messages land in a "needs review" list with a reason; nothing silently dropped ([[M3 Transaction parser]])
- [ ] Accounts modelled per user (multiple accounts/cards)
- [ ] Per-user scoping on all of it

Open question: how to detect transfers reliably (user marks an account as "mine"?).
