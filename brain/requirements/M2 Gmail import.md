---
id: M2
release: MVP
status: built-not-verified-against-google
verified: 269 backend + 40 frontend tests; migration and endpoints live on compose Postgres; consent address inspected with a dummy client; 47 real-browser checks (2026-11-04). NOT verified: a real Google connection, real bank email formats
needs-from-user: Google OAuth client id/secret in `.env` (docs/operations.md §8)
---
# M2 Connect Gmail and import bank emails

**Requirement:** connect Gmail via OAuth (read-only), import bank/UPI/card transaction emails.

**Acceptance:** with a connected account, clicking Sync shows debit/credit alert emails from the last 90 days as transactions, **no duplicates**.

## Code check
- [x] Only the read-only Gmail scope is requested; a grant with any other scope is refused and revoked — `GmailQuery.ReadOnlyScope`, `GoogleHttpApi.BuildAuthorizationUrl`, `GmailService.CompleteConnectAsync` (consent address inspected live; extra-scope test)
- [x] Only transaction-like emails are fetched: fixed bank/payment-app sender domains + alert words, not spam/trash, 90 days then since last sync, max 300 per sync — `Application/Gmail/GmailQuery.cs` (tested)
- [x] Refresh tokens **encrypted at rest** with `ITokenProtector` (round trip, ciphertext in the column, lost key ring → reconnect) — `Infrastructure/Gmail/GmailService.cs`; key ring is on the `dpkeys` volume and in `scripts/backup.sh`
- [x] Raw email bodies are **not stored**: parsed in memory and dropped; stored key is SHA-256 of the Gmail message id (unit test checks no account/reference/body text lands in the row)
- [x] Reuses the parser from [[M3 Transaction parser]]; emails are cut at footers like "never share your OTP" first (`EmailTextExtractor`, tested) so real alerts are not discarded
- [x] Idempotent sync: second sync reads and adds nothing; already imported ids are not fetched again; SMS + email of the same payment is flagged as a possible duplicate ([[M9 Data quality]])
- [x] Per-user isolation; tokens never logged or sent to the frontend (no secret in any response body in the API tests; UI never receives a token)
- [x] Disconnect deletes the stored token and revokes the grant at Google (best effort); works when Google is unreachable
- [x] OAuth hardening: PKCE S256, random single-use `state` stored hashed, 10-minute expiry, callback anonymous (SameSite=Strict cookie is not sent on Google's redirect) yet bound to the user who started it; fixed local redirect; CSRF header on connect/sync/disconnect; rate limit; one sync at a time per user
- [x] Audit events: connect, disconnect, sync, reconnect-needed (type/time/IP only)
- [x] UI: card in the Transactions tab (connect, sync, disconnect, result, subjects of unreadable emails, plain message when not set up) — `GmailPanel.tsx`, `gmail.ts`
- [ ] **Real Google round trip** — not done; needs the user's OAuth client. First real connection is the real test
- [ ] **Accuracy on real bank emails** — unmeasured; parser tested on formats written from memory; unreadable emails are listed (subject only) so rules can be tuned
- [x] Forged senders refused: only emails whose sender Gmail verified (DKIM or DMARC pass, written by mx.google.com, domain on the bank list and matching From) are imported; skipped ones are counted in the result — `SenderVerifier`, 13 test cases plus a sync test
- [ ] Sender list is by domain and may miss the user's bank; a bank that does not pass DKIM/DMARC will be skipped (visible in the sync result); not yet seen on real mail
- [ ] Testing mode: grant expires after 7 days; Google's restricted-scope verification (needed beyond ≤ 100 test users) not started
- [ ] Unreadable alert-looking emails are re-read on every sync; deleted rows can return if inside the one-day overlap

Constraint: Gmail read is a restricted scope; test-user mode only (≤ 100 users). See [[Constraints and decisions]], [[Security checklist]], [[M12 Security gate]].
