# Check Log

Newest first. One entry per task: date · task · notes checked · result · gaps.

## 2026-11-03 · M5 categorization (rules only)
- Notes checked: [[M5 Categorization]], [[Security checklist]], [[Non-functional requirements]], [[M3 Transaction parser]] (parser unchanged), [[M4 SMS import]] (import now categorizes).
- Result: **pass for the rules part.** Backend 70 tests, frontend 12 tests, lint, build, `npm audit` (high) and NuGet vulnerability scan all clean. Live on compose Postgres: migration applied, import categorized Swiggy as Food & Dining, correction saved, bad category 400, no token 401, other user 404.
- Security walk: new endpoints `[Authorize]`, owner from token, ownership-scoped query, fixed category list, no secrets, no data sent out, no new dependencies.
- Gaps: AI fallback not built (by design); accuracy unmeasured; no backfill of old rows; dropdown not tested in a real browser; Docker `api`+`db` containers were started for the check.
- Not re-verified in this task: M1, M3, M4, M8 ticks (only exercised indirectly by the full test run).

## 2026-11-03 · Requirements review applied
- Added M9–M12, measurable NFR targets, acceptance criteria for all MVP items and open decisions 4–7 to `docs/requirements.md` and the brain.
- Docs/brain only; no code changed, nothing re-verified. Per-note acceptance criteria for M1/M3/M4/M5/M7 live in `docs/requirements.md` §7; the notes' Code check lists were not individually extended.
- Open: decisions 4–7 await the user.

## 2026-11-03 · Create the brain vault
- Notes checked: n/a (vault created from `docs/requirements.md`, `docs/security.md`, `docs/verification.md`).
- Initial Code check ticks reflect the state recorded in those docs at commit 98faa8b; they were **not** re-run when the vault was created. First real task should re-verify them.
