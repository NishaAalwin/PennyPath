---
id: M10
release: MVP
status: done-with-gaps
verified: 348 backend + 54 frontend tests; migration, deletion and logs checked on real Postgres; 67 real-browser checks including verify, reset, two-factor, export and delete (2026-11-04). NOT verified: a real (non-Mailpit) mail server
note: scope widened 2026-11-04 on the user's request (password reset, email verification, optional two-factor login, delete account, export)
---
# M10 Account lifecycle

**Requirement:** email verification, password reset, delete-my-account with all data, data export, and optional two-factor login (TOTP).

**Acceptance:** after a deletion request, the account, transactions and stored tokens are gone and unrecoverable through the app.

## Decisions (made 2026-11-04, change them by telling me)
- **Email delivery:** the app talks SMTP. Local default is **Mailpit**, a mail catcher in Docker bound to `127.0.0.1:8025` (nothing leaves the machine; open the page to read the verification and reset emails). A real SMTP server is optional through `SMTP_*` settings in `.env`; the app refuses to send a mail password without TLS.
- **Links carry the token in the URL fragment** (`/#verify=...`, `/#reset=...`), which browsers never send to servers, logs or other sites; the page posts it in a request body and removes it from the address bar at once.
- **Existing accounts are treated as verified** by the migration (they predate verification); new accounts must verify. `Auth:RequireVerifiedEmail=false` switches the rule off.
- **Unverified accounts** can sign in only to see the "confirm your email" screen and ask for another link; every data endpoint refuses them (403). The check reads the database on every request, not the token.
- **Two-factor:** TOTP (RFC 6238, 6 digits, 30 s, ±1 step), secret encrypted with `ITokenProtector`, 10 single-use recovery codes stored hashed. Optional per account. Entered as a typed key (no QR code).

## Code check
- [x] Email verification before full access; tokens single-use and expiring (24 h); unverified users blocked from every data endpoint (403, tested over HTTP) — `AccountService`, default authorization policy in `Program.cs`
- [x] Password reset: single-use, 30-minute token; unknown and known addresses get the identical answer; rate limited (per IP) and throttled to 3 links per hour per account; a newer link voids the older; a weak password keeps the link usable
- [x] Reset/verify tokens stored hashed (SHA-256), never logged, never in a URL path or query; logs searched on the live stack: nothing
- [x] A password reset ends **all** sessions, clears lockout, does not sign anyone in, and does not switch two-factor off
- [x] Delete account removes the user, transactions, Gmail connection and pending OAuth state (and revokes the Google grant), AI consent, sessions, audit rows, link tokens, recovery codes; **proved on real Postgres** (two browser-deleted accounts: 0 users, 0 orphan rows in 6 tables); other users' rows untouched (unit and HTTP tests); a content-free "AccountDeleted" event is kept
- [x] Deletion requires re-authentication (password, and a code when two-factor is on); wrong answers count toward the sign-in lockout
- [x] Existing sessions stop working after deletion (cookie → 401, tested)
- [x] Data export: profile, settings, transactions, security activity as a JSON download; no password hash, token or secret (checked in unit, HTTP and browser tests); only the caller's own data
- [x] Two-factor: secret shown once at setup and stored encrypted; enabling needs a valid code; sign-in needs password **and** code; a used code cannot be replayed; recovery codes work once and are stored hashed; disabling needs password and code; wrong codes count toward lockout and the counter is not reset by the "code needed" step
- [x] Every new endpoint: `[Authorize]` where needed, CSRF header (including the anonymous reset/verify/forgot posts), rate limit, audit event with no personal data (tested)
- [x] UI: verify screens, forgot/reset forms, code step in sign-in, Account section (two-factor, export, delete) — 67-check real-browser run
- [x] SMTP password is never sent without TLS (tested); email is queued and sent in the background so a slow mail server cannot slow or reveal anything about a request
- [ ] **Real email delivery** (non-Mailpit SMTP) verified against a real mail server — user action
- [ ] Sign-up still answers "an account with this email already exists" (409), so addresses can be probed there (sign-in and reset do not leak)
- [ ] Change-password while signed in; change-email; regenerate recovery codes without turning two-factor off; QR code for setup
- [ ] A code accepted twice in the same instant by two parallel requests is not prevented (no row lock)
- [ ] No self-service route back in after losing both the phone and the recovery codes
- [ ] Backups made before a deletion still contain the account (see docs/operations.md)

Extends [[M1 Sign up and login]]. See [[Security checklist]].
