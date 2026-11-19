# PennyPath Brain

The project's source of truth for **what the code must satisfy**. Open the `brain/` folder as a vault in Obsidian.

Every task is checked against this vault before it is called done. See [[Task Protocol]].

## Map
- [[Task Protocol]] — the check that runs on every task
- Requirements (one note per item, each with a *Code check* list)
  - MVP: [[M1 Sign up and login]] · [[M2 Gmail import]] · [[M3 Transaction parser]] · [[M4 SMS import]] · [[M5 Categorization]] · [[M6 Monthly dashboard]] · [[M7 Transaction list]] · [[M8 Docker and CI]] · [[M9 Data quality]] · [[M10 Account lifecycle]] · [[M11 Manual transaction]] · [[M12 Security gate]] · [[Data minimisation]]
  - Later: [[Later releases]]
  - Cross-cutting: [[Non-functional requirements]] · [[Security checklist]] · [[Constraints and decisions]]
- [[Check Log]] — result of every task check, newest first

## Status at a glance
| Item | Status |
|---|---|
| M1 | done, API verified, browser UI untested |
| M2 | built, not verified against real Google: OAuth+PKCE, encrypted token, sync, disconnect, UI, 62 new tests; needs your OAuth client for the first real connection |
| M3 | done, accuracy on real messages unmeasured |
| M4 | done (paste import) |
| M5 | rules + user correction done; AI fallback deferred; accuracy unmeasured |
| M6 | done with gaps: summary, trend, charts and 10k-row timing done; browser hover/focus untested, no texture fallback |
| M7 | done with gaps: search, filters, paging and edit built and live-verified on 10k rows; UI not browser-tested; manual add is M11 |
| M8 | in progress: full CI green on GitHub (51c349f, PR #1); Deploy written, not yet run; Azure not created |
| M9 | done with gaps: flags duplicates, links refunds, manual transfers; no email source yet, accuracy unmeasured |
| M10 | done with gaps: email verification, password reset, optional TOTP two-factor, export, delete-my-account (rows proven gone on real Postgres); real SMTP not tested, sign-up still reveals existing emails |
| M11 | not started (new) |
| DM1 | done: only amount, where/what, date, debit/credit stored (PostgreSQL) |
| M12 | done with gaps: HTTPS proxy, cookie sessions + CSRF, revocation, audit log, AI consent, key ring + encryption service, tested backups, real-browser check; user actions and disk encryption remain; M2 may now proceed |

Keep this table and each note's `status:` in sync when a task lands.

Source docs this vault summarises: `docs/requirements.md`, `docs/design.md`, `docs/security.md`, `docs/verification.md`. If they disagree with a brain note, fix both in the same task.
