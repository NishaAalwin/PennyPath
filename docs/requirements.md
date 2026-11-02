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
- M3. Parser extracts amount, debit/credit, merchant, date and account from emails.
- M4. Manual SMS import (paste or upload a file) that uses the same parser as emails.
- M5. Auto-categorize transactions (rules first, AI fallback), and let the user correct a category.
- M6. Monthly dashboard with total income, total expense, savings, category breakdown and trend.
- M7. Transaction list with search, filter and edit.
- M8. Deployed with Docker and a CI pipeline.

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
- Privacy: only transaction-like emails are read (filtered by sender and keywords).
- Performance: dashboard loads in under 2 seconds for 10k transactions.
- Quality: at least 70% unit test coverage on the parser and services.
- Parser accuracy: at least 90% on a sample set of real bank alerts.

## 7. Acceptance criteria (MVP examples)
- **M2:** Given a connected Gmail account, when I click Sync, then debit and credit alert emails from the last 90 days appear as transactions with no duplicates.
- **M6:** Given imported transactions, when I open the dashboard for a month, then income, expense, savings and category totals match the transaction list exactly.

## 8. Open decisions
1. Country and currency focus (assumed India, INR, UPI).
2. Android app (React Native or native Kotlin) for v0.3.
3. Hosting target (Azure, AWS or a VPS).
