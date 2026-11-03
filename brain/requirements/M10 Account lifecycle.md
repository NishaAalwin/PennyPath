---
id: M10
release: MVP
status: not-started
---
# M10 Account lifecycle

**Requirement:** email verification, password reset, and delete-my-account with all data.

**Acceptance:** after a deletion request, the account, transactions and stored tokens are gone and unrecoverable through the app.

## Code check (must hold when built)
- [ ] Email verification before full access; tokens single-use and expiring
- [ ] Password reset: single-use, short-lived token; no user enumeration in responses; rate limited
- [ ] Reset/verify tokens stored hashed, never logged
- [ ] Delete account removes users, transactions, Gmail tokens, and any AI consent records; test proves rows are gone
- [ ] Deletion requires re-authentication
- [ ] Existing JWTs stop working after deletion/password change (needs revocation, see [[Security checklist]] item 4)
- [ ] Optional data export (GDPR/DPDP-style)

Extends [[M1 Sign up and login]].
