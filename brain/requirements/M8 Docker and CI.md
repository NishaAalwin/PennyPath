---
id: M8
release: MVP
status: done
verified: compose up verified; GitHub CI green on 14be2e2
---
# M8 Deployed with Docker and a CI pipeline

## Code check
- [x] `docker-compose.yml` runs db + api + web; refuses to start without `POSTGRES_PASSWORD` and `JWT_KEY` (from `.env`, git-ignored)
- [x] Ports bind to `127.0.0.1` only; containers non-root, `no-new-privileges`, caps dropped
- [x] `.github/workflows/ci.yml` builds + tests backend and frontend; fails on vulnerable packages
- [x] `.github/workflows/codeql.yml`, `dependabot.yml`; least-privilege `permissions`
- [ ] CI re-checked green on the latest commit (98faa8b)
- [ ] Docker image scanning (Trivy)
- [ ] HTTPS / reverse proxy (not set up)
- [ ] Hosting target decided ([[Constraints and decisions]])

Related: [[Security checklist]]
