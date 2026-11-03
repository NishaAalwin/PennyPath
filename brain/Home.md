# PennyPath Brain

The project's source of truth for **what the code must satisfy**. Open the `brain/` folder as a vault in Obsidian.

Every task is checked against this vault before it is called done. See [[Task Protocol]].

## Map
- [[Task Protocol]] — the check that runs on every task
- Requirements (one note per item, each with a *Code check* list)
  - MVP: [[M1 Sign up and login]] · [[M2 Gmail import]] · [[M3 Transaction parser]] · [[M4 SMS import]] · [[M5 Categorization]] · [[M6 Monthly dashboard]] · [[M7 Transaction list]] · [[M8 Docker and CI]]
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
| M5 | not started |
| M6 | not started |
| M7 | partial (list only) |
| M8 | done (Docker + CI green) |

Keep this table and each note's `status:` in sync when a task lands.

Source docs this vault summarises: `docs/requirements.md`, `docs/design.md`, `docs/security.md`, `docs/verification.md`. If they disagree with a brain note, fix both in the same task.
