---
id: M1
release: MVP
status: done
verified: API + unit tests (12 backend, 6 frontend); browser UI untested
---
# M1 Sign up and login (JWT)

**Requirement:** users sign up and log in; API protected by JWT.

## Code check
- [x] Register/login endpoints — `backend/src/PennyPath.Api/Controllers/AuthController.cs`
- [x] Passwords hashed (ASP.NET Identity PBKDF2), never returned/logged — `Infrastructure/Auth/AuthService.cs`
- [x] JWT: HS256 only, issuer/audience/expiry validated, key ≥ 32 bytes — `Infrastructure/Auth/JwtTokenService.cs`, `JwtOptions.cs`
- [x] Lockout after 5 failures / 15 min; same response for unknown email vs wrong password
- [x] Password policy 8–128, common-password list
- [x] Frontend form + session — `frontend/src/AuthForm.tsx`, `session.ts`
- [ ] Verified in a real browser

## Known gaps
No email verification, password reset, MFA, token revocation. See [[Security checklist]].

Related: [[Non-functional requirements]]
