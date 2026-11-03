# PennyPath: Requirements (Phase 1)

## 1. Vision
PennyPath is a personal finance app. It reads a person's bank/payment emails (Gmail) and SMS, builds a monthly income and expense picture automatically, and uses AI to suggest ways to save and invest for long-term financial growth.

## 2. Persona
**Asha, 28, salaried, India.** Pays by UPI and card, gets bank alerts by SMS and email, has no time to log expenses by hand, and wants to know where her money goes and how to grow it.

## 3. Tech stack (decided)
| Layer | Choice |
|---|---|
| Frontend | React (TypeScript, Vite) |
| Backend | ASP.NET Core Web API (.NET 10 installed locally) |
| Database | PostgreSQL (EF Core) |
| AI | Claude API (categorization fallback and advice) |
| Packaging | Docker and docker-compose |
| CI/CD | GitHub Actions |

## 4. Key constraints and risks
1. **SMS cannot be read from a web app.** Browsers have no SMS access. Reading SMS needs an Android app with the READ_SMS permission. iOS does not allow it at all.
2. **Gmail read access is a Google "restricted scope".** For public users it needs Google OAuth verification and a security audit. In development, test-user mode (up to 100 users) is enough.
3. **Financial data is sensitive.** It needs encryption, minimal data retention and explicit user consent.
4. **AI advice is not licensed financial advice.** The app must show a disclaimer.

## 5. Scope by release

### MVP (v0.1): Must have
- M1. Sign up and log in (JWT).
- M2. Connect Gmail (OAuth, read-only) and import bank/UPI/card transaction emails.
- M3. Parser extracts amount, debit/credit, merchant (where/what) and date from emails. It does not read account numbers, reference numbers or balances.
- M4. Manual SMS import (paste or upload a file) that uses the same parser as emails.
- M5. Auto-categorize transactions (rules first, AI fallback), and let the user correct a category.
- M6. Monthly dashboard with total income, total expense, savings, category breakdown and trend.
- M7. Transaction list with search, filter and edit.
- M8. Deployed with Docker and a CI pipeline (also a continuous requirement: CI stays green on every commit).
- M9. Data quality: cross-source duplicate detection (SMS vs email), own-account transfers (marked by the user) excluded from income and expense, refunds and reversals handled, and unparseable messages reported to the user at import time (their text is not stored).
- M10. Account lifecycle: email verification, password reset, and "delete my account and all my data".
- M11. Manual add of a transaction (fallback when the parser fails).
- M12. Security gate before M2: HTTPS, safer token storage, consent screen for AI use, and the open items in `docs/security.md` that are marked as blocking Gmail.

### v0.2: Should have
- S1. AI insights: where you overspend, 3–5 saving ideas, investment ideas based on your surplus and risk profile.
- S2. Budgets per category, with alerts.
- S3. Recurring payment and subscription detection.

### v0.3: Could have
- C1. Android companion app that forwards SMS automatically.
- C2. Financial goals (emergency fund, house), net worth and a growth score.
- C3. Export to CSV/PDF.

### Won't have (for now)
Direct bank API integration, actually executing investments, iOS SMS reading, multi-currency.

## 6. Non-functional requirements
- Security: encrypted tokens at rest, HTTPS, no raw email bodies stored (store only parsed fields), and "delete my data" support.
- Data minimisation (user decision, 2026-11-03): only amount, where/what (merchant), date and debit/credit are read and stored, in PostgreSQL. No account numbers, reference numbers, balances or message text are stored; only a hash of the message is kept to skip re-imports.
- Privacy: only transaction-like emails are read (filtered by sender and keywords).
- Performance: dashboard loads in under 2 seconds for 10k transactions, measured on the Docker Compose stack on a developer laptop (state the hardware in `docs/verification.md`).
- Quality: at least 70% unit test coverage on the parser and services, measured in CI.
- Parser accuracy: at least 90% on a labelled sample of at least 100 real, redacted bank alerts from at least 3 banks, labelled by the user. Report accuracy per field (amount, direction, merchant, date, account).
- Categorization accuracy: at least 85% correct on a labelled sample of at least 100 transactions.
- Observability: structured logs without personal data, error tracking, and database backups before any real user data is held.
- Time: amounts are stored in INR as decimals; dates are stored in UTC and months are computed in the user's time zone (default Asia/Kolkata).
- AI privacy: AI categorization and insights are opt-in, send only merchant text or aggregates (never account numbers, balances or raw messages), and can be switched off.

## 7. Acceptance criteria (MVP examples)
- **M2:** Given a connected Gmail account, when I click Sync, then debit and credit alert emails from the last 90 days appear as transactions with no duplicates.
- **M1:** Given a new email and a valid password, when I sign up and log in, then I get a session; after 5 wrong passwords the account is locked for 15 minutes; one user can never see another user's data.
- **M3:** Given the labelled sample set, when the parser runs, then it meets the accuracy target in section 6, and any message it cannot parse is reported, not dropped.
- **M4:** Given pasted SMS text, when I import it, then each valid message becomes one transaction, invalid ones are listed with a reason, and importing the same text again creates no duplicates.
- **M5:** Given a transaction, when it is imported, then it gets a category (rule first, AI only if I opted in); when I change the category, then the change is kept and not overwritten on later imports.
- **M6:** Given imported transactions, when I open the dashboard for a month, then income, expense, savings and category totals match the transaction list exactly.
- **M7:** Given 10k transactions, when I search, filter by date, category or type, or edit a transaction, then the list updates correctly and only my own transactions are affected.
- **M9:** Given the same payment arrives by SMS and by email, then it appears once; given a payment I mark as a transfer between my own accounts, then it is not counted as income or expense; given a refund, then it reduces the matching expense.
- **M10:** Given I request deletion, then my account, transactions and stored tokens are removed and cannot be recovered through the app.
- **M11:** Given I add a transaction by hand, then it appears in the list and the dashboard like any other.

## 8. Open decisions
1. Country and currency focus (assumed India, INR, UPI).
2. Android app (React Native or native Kotlin) for v0.3.
3. Hosting target (Azure, AWS or a VPS).
4. Audience for the MVP: the author plus up to 100 Gmail test users (recommended, avoids Google's audit), or a public launch. Decides whether Gmail verification is needed.
5. Whether the Android SMS app (C1) moves earlier than v0.3, since pasting SMS by hand weakens the "no manual logging" promise.
6. Whether investment ideas (S1) fall under SEBI investment-advice rules in India; check before building S1.
7. Whether AI categorization is acceptable at all (recommended: opt-in, merchant text only).

## 9. Revision history
- 2026-11-03: data minimisation: only amount, where/what, date and debit/credit are stored; account and reference numbers and unreadable-message text are no longer kept; automatic transfer detection removed (needed account numbers); monthly summary (M6) started.
- 2026-11-03: added M9-M12, measurable accuracy and performance targets, acceptance criteria for every MVP item, open decisions 4-7 (from a requirements review).
