# Task Protocol

Run this for **every** task, however small. Claude is instructed to do so by `CLAUDE.md`.

## Before coding
1. Read [[Home]] and identify which requirement notes the task touches (use `requirements/` links).
2. Always include [[Security checklist]] and [[Non-functional requirements]] if the task adds or changes an endpoint, data, dependency, or config.
3. Note any requirement the task would violate or leave unmet. Raise it with the user instead of silently deviating.

## After coding (gate before saying "done")
1. Walk each touched note's **Code check** list against the actual code. Cite file paths; do not tick from memory.
2. Run the relevant tests/build and record the real result. Unrun = "not verified", never "passes".
3. Walk the [[Security checklist]] items relevant to the change.
4. Update the touched notes: `status:`, ticks, and **Known gaps**. Update the table in [[Home]].
5. Add an entry to [[Check Log]] (date, task, notes checked, pass/fail/gaps).
6. If docs/ disagree with the brain, update both (`docs/verification.md` for verification state, `docs/security.md` for security).
7. Report to the user: which requirements were checked, what satisfied them, and what is still open.

## Rules
- A failed or unverifiable check is recorded as a gap, not hidden.
- New requirement from the user → add/modify a note here first, then build.
- Never store secrets or tokens in the vault.
