---
id: M2
release: MVP
status: not-started
blocked-on: Google OAuth client (client id/secret) from the user
---
# M2 Connect Gmail and import bank emails

**Requirement:** connect Gmail via OAuth (read-only), import bank/UPI/card transaction emails.

**Acceptance:** with a connected account, clicking Sync shows debit/credit alert emails from the last 90 days as transactions, **no duplicates**.

## Code check (must hold when built)
- [ ] Only the read-only Gmail scope is requested
- [ ] Only transaction-like emails are fetched (filter by sender + keywords) — privacy NFR
- [ ] Refresh tokens **encrypted at rest** (ASP.NET Data Protection with persisted key ring) before this ships
- [ ] Raw email bodies are **not stored**, only parsed fields
- [ ] Reuses the parser from [[M3 Transaction parser]]
- [ ] Idempotent sync: re-running creates no duplicates (dedupe key)
- [ ] Per-user isolation; tokens never logged or sent to the frontend
- [ ] Disconnect Gmail deletes stored tokens

Constraint: Gmail read is a restricted scope; test-user mode only (≤ 100 users). See [[Constraints and decisions]], [[Security checklist]].
