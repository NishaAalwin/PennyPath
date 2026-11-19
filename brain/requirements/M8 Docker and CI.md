---
id: M8
release: MVP
status: in-progress
verified: compose up verified; full CI green on GitHub on 51c349f (PR #1, 2026-11-19), including actionlint, shellcheck, Bicep build, compose config and image scan; Deploy and Azure not yet verified
---
# M8 Deployed with Docker and a CI pipeline

## Code check
- [x] `docker-compose.yml` runs db + api + web; refuses to start without `POSTGRES_PASSWORD` and `JWT_KEY` (from `.env`, git-ignored)
- [x] Ports bind to `127.0.0.1` only; containers non-root, `no-new-privileges`, caps dropped
- [x] `.github/workflows/ci.yml` builds + tests backend and frontend; fails on vulnerable packages
- [x] `.github/workflows/codeql.yml`, `dependabot.yml`; least-privilege `permissions`
- [x] CI re-checked green on the latest commit (51c349f, PR #1; the separate CodeQL alert check fails on old alerts in `scripts/browser-e2e.mjs`)
- [x] Docker image scanning (Trivy) locally: `scripts/scan-images.sh`
- [x] HTTPS / reverse proxy: Caddy ([[M12 Security gate]])
- [x] Hosting target decided: Azure VM + docker compose ([[Constraints and decisions]] #3)

## Azure deployment (decided 2026-11-19)
**Requirement:** every push to `main` that passes CI is built, scanned and deployed to one Azure VM running the compose stack over HTTPS, with no long-lived Azure credential in GitHub and no secret in the repo.

**Acceptance:** after a push to `main`, `https://<vm-fqdn>/api/health` answers from the new version with no manual step; a failed CI, scan or deploy leaves the previous version running and the workflow red.

### Code check
- [ ] Infrastructure as code: `infra/main.bicep` creates VNet, NSG, public IP with DNS name, VM, data disk, Key Vault, deploy identity; nothing made by hand except the subscription, the resource group and the first `az deployment` run
- [ ] Network: only 80 and 443 open to the internet; SSH closed unless an admin IP is given; Postgres, Mailpit and the API never exposed
- [ ] VM: SSH key only (no password), automatic OS patching, Docker data on a separate managed data disk (Azure encrypts managed disks at rest)
- [ ] Secrets: `POSTGRES_PASSWORD`, `JWT_KEY`, Google and SMTP values held in Key Vault (RBAC, soft delete, purge protection, firewall); the VM reads them with its own managed identity; `.env` on the VM is mode 600; no secret in Bicep, workflow, repo or logs
- [ ] GitHub → Azure via an OIDC federated credential bound to this repo's `production` environment; its role is limited to running commands on this one VM
- [x] Deploy runs only after CI passes on `main`; images tagged with the commit SHA; Trivy blocks CRITICAL fixable findings before deploy
- [x] The workflow fails when the remote script fails, and checks `/api/health` over the public HTTPS address afterwards
- [x] One deploy at a time (concurrency group); a CI run that finishes after a newer commit's never deploys the older commit over it
- [x] The VM keeps the previous `.env` and containers when an image pull fails, and the script checks each running container uses the deployed commit's image
- [x] CI (every PR and push) also lints the workflows (actionlint), shell scripts (shellcheck) and Bicep, validates the production compose file, and builds and Trivy-scans the three images with the same rule as deploy (fixable CRITICAL blocks, HIGH reported); one scan script (`scripts/scan-images.sh`) serves local, CI and deploy
- [x] Every job has a timeout; checkout does not keep the token (`persist-credentials: false`); superseded PR runs are cancelled
- [ ] Migrations still run on start (`Database__MigrateOnStartup`); database and key ring survive a redeploy
- [ ] `docs/operations.md` explains first-time Azure setup, secrets, the Google redirect URI and rollback

CI items above are verified on GitHub (51c349f). Deploy items are present in the code and linted, not yet seen working: Deploy has not run (it starts after merge to `main`), and the VM-side `.env`/image checks need a real VM.

### Known limits (accepted for now)
- Actions are pinned to major tags (`@v4`, `@v2`), not commit SHAs; Dependabot keeps them current.
- Local scan 2026-11-19 (FAIL_ON=CRITICAL): 0 CRITICAL; fixable HIGH reported in API (apt, ASP.NET Core 10.0.14 → 10.0.15), web (libxslt) and proxy (Caddy's Go libraries). Reported, not blocking. Rebuild with `--pull` to pick up fixes.
- Single VM: no high availability; a restart means about a minute of downtime.
- Off-machine backups (Azure Blob or Azure Backup) not built; `scripts/backup.sh` works on the VM.
- Sign-up email verification needs real SMTP on the server (Mailpit is reachable only from the VM itself).

Related: [[Security checklist]], [[M12 Security gate]], [[Non-functional requirements]]
