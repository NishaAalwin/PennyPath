---
id: M1
release: MVP
status: done
verified: API + unit tests; real browser (Edge) 2026-11-03: sign-up, log-in, wrong password, log-out, log-in again
---
# M1 Sign up and login (JWT)

**Requirement:** users sign up and log in; API protected by JWT.

## Code check
- [x] Register/login endpoints — `backend/src/PennyPath.Api/Controllers/AuthController.cs`
- [x] Passwords hashed (ASP.NET Identity PBKDF2), never returned/logged — `Infrastructure/Auth/AuthService.cs`
- [x] JWT: HS256 only, issuer/audience/expiry validated, key ≥ 32 bytes — `Infrastructure/Auth/JwtTokenService.cs`, `JwtOptions.cs`
- [x] Lockout after 5 failures / 15 min; same response for unknown email vs wrong password
- [x] Password policy 8–128, common-password list
- [x] Frontend form + session check — `frontend/src/AuthForm.tsx`, `App.tsx` (asks the server who is signed in; no client-side token)
- [x] Verified in a real browser (`scripts/browser-e2e.mjs`)
- [x] **Changed by [[M12 Security gate]]:** the token is no longer kept in `localStorage`; it lives only in an HttpOnly `__Host-pp_session` cookie, backed by server-side sessions (logout revokes), with CSRF protection

## Known gaps
Email verification, password reset and optional two-factor were added by [[M10 Account lifecycle]] (2026-11-04); no refresh tokens (fixed 60-minute sessions). Sessions can be revoked since [[M12 Security gate]]. See [[Security checklist]].

Related: [[Non-functional requirements]]
