# PennyPath

Personal finance app: Gmail/SMS bank alerts → monthly income/expense dashboard → AI saving/investing ideas. Stack and constraints: `brain/requirements/Constraints and decisions.md`.

## The brain (Obsidian vault) — mandatory on every task
`brain/` is an Obsidian vault holding the requirements the code must satisfy. Start at `brain/Home.md`.

For **every** task, follow `brain/Task Protocol.md`:
1. **Before coding:** read the requirement notes the task touches, plus `Security checklist` and `Non-functional requirements` when endpoints, data, dependencies or config change.
2. **After coding, before saying done:** check the actual code against each touched note's *Code check* list (cite files, run the tests), update that note's ticks/`status:`/gaps and the table in `brain/Home.md`, and append an entry to `brain/Check Log.md`.
3. **Report** to the user which requirements were checked, what satisfied them, and what remains open. Unverified means "not verified", never "passes".

If the user gives a new requirement, add or edit the brain note first, then build.

## Other rules
- Keep `docs/verification.md` and `docs/security.md` in sync with the brain.
- Never store tokens or secrets in the repo or the vault.
