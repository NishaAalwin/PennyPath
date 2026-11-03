---
id: M4
release: MVP
status: done
verified: e2e on samples
---
# M4 Manual SMS import

**Requirement:** paste or upload SMS text, parsed with the same parser as email.

## Code check
- [x] Import endpoint — `Api/Controllers/TransactionsController.cs`, `Infrastructure/Transactions/TransactionService.cs`
- [x] Uses [[M3 Transaction parser]]
- [x] Limits: 500 messages, 2000 chars each, 1 MB body
- [x] Scoped to the token's user id; second user cannot see the data (tested)
- [x] Paste UI — `frontend/src/TransactionsPanel.tsx`
- [ ] File upload (only paste is built)
- [ ] Duplicate re-import is skipped (confirm in `TransactionService.cs` + tests)
- [ ] Raw SMS text not retained beyond parsed fields (check what `Transaction` stores — `Domain/Entities/Transaction.cs`)

Related: [[Security checklist]]
