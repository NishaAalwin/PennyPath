---
id: M12
release: MVP
status: not-started
blocks: M2
---
# M12 Security gate before Gmail

**Requirement:** the items that make Gmail tokens safe to hold must be done before [[M2 Gmail import]] ships.

## Code check (all must be ticked before M2 is released)
- [ ] HTTPS in front of the app (reverse proxy/host); `ForwardedHeaders:KnownProxies` set
- [ ] JWT moved from `localStorage` to httpOnly SameSite cookie with CSRF protection, or a documented decision to accept the risk
- [ ] Token revocation / refresh strategy
- [ ] Data Protection key ring persisted; Gmail refresh tokens encrypted at rest
- [ ] AI consent screen and opt-out; data minimisation enforced in code ([[M5 Categorization]])
- [ ] Database encryption at rest + backups plan
- [ ] Audit log for sign-in, token use and deletion
- [ ] CSP and forms checked in a real browser
- [ ] GitHub settings done by the user (branch protection, secret scanning)

See [[Security checklist]] for the full open list.
