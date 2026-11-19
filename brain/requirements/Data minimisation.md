---
id: DM1
release: MVP
status: done-with-gaps
source: user request 2026-11-03
verified: 119 backend + 14 frontend tests; live on compose Postgres, schema inspected with psql (2026-11-03)
---
# Data minimisation: only amount and where/what

**Requirement (user, 2026-11-03):** the app reads only the transaction details that matter: the **amount** and **for what / where** it was spent, and summarises them. It is saved in **PostgreSQL**.

**Decisions with the user:**
- Keep per transaction: **amount, where/what (merchant), date, debit/credit** (date and direction are needed for the monthly income/expense summary). Category is derived from the merchant.
- **Do not read or store:** account digits, bank reference numbers, balances, or message text.
- **Stop storing unreadable messages** (removes the needs-review list; reverses the M9 exception). They are shown once after import only.
- "Summarised" = the monthly summary in [[M6 Monthly dashboard]]: income, expense, savings, totals by where/what and by category.

## Code check
- [x] `Transaction` has no `Account` or `Reference` column; parser no longer extracts them — `Domain/Entities/Transaction.cs`, `Application/Parsing/*`; test pins `ParsedTransaction` to 5 fields
- [x] `UnparsedMessage` table, endpoints and UI removed; migration `MinimiseStoredData` dropped the table and columns (verified with psql: only `Transactions`, `Users`, history remain)
- [x] No endpoint returns message text except the one-time unreadable list in the import response
- [x] Only a SHA-256 of the normalised message text is kept (re-import dedupe); see gap below
- [x] Database is PostgreSQL (EF Core Npgsql); all migrations applied on real Postgres, including the upgrade of a DB that held old data
- [x] [[M9 Data quality]] rules re-checked without account/reference (reconciler rewritten, tests updated)
- [x] Summary built from stored fields only ([[M6 Monthly dashboard]])
- [x] UI tells the user what is and is not saved

## Known gaps
- The hash of the message text is still stored. For short, structured SMS someone with the database could guess candidate messages and compare. An HMAC with a server secret would fix this (not done).
- Merchant text is free text from the message and could occasionally contain a name or UPI handle part.
- Old backup/volume copies made before the migration still contain the dropped data (none are made by the app).
- Automatic transfer detection was removed because it needed account numbers; users mark transfers by hand.

Related: [[Security checklist]], [[Non-functional requirements]], [[M4 SMS import]]
