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
| M2 | not started (needs Google OAuth client from user) |
| M3 | done, accuracy on real messages unmeasured |
| M4 | done (paste import) |
| M5 | rules + user correction done; AI fallback deferred; accuracy unmeasured |
| M6 | partial: monthly summary (totals, by place, by category) done; trend, charts, performance missing |
| M7 | partial (list only) |
| M8 | done (Docker + CI green) |
| M9 | done with gaps: flags duplicates, links refunds, manual transfers; no email source yet, accuracy unmeasured |
| M10 | not started (new) |
| M11 | not started (new) |
| DM1 | done: only amount, where/what, date, debit/credit stored (PostgreSQL) |
| M12 | not started (new); must finish before M2 ships |

Keep this table and each note's `status:` in sync when a task lands.

Source docs this vault summarises: `docs/requirements.md`, `docs/design.md`, `docs/security.md`, `docs/verification.md`. If they disagree with a brain note, fix both in the same task.
