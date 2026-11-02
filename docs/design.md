# PennyPath: Design (Phase 2)

## 1. Architecture
```
 React SPA ──HTTPS──> ASP.NET Core Web API ──> PostgreSQL
                         │   │
                         │   └──> Gmail API (OAuth, read-only)
                         └──────> Claude API (categorize, insights)
 (v0.3) Android app ──POST /api/sms/ingest──> API
```
Backend is a modular monolith with these layers: `Api` (controllers), `Application` (services), `Domain` (entities), `Infrastructure` (EF Core, Gmail, Claude).

## 2. Repo layout
```
PennyPath/
  docs/
  backend/
    src/PennyPath.Api, .Application, .Domain, .Infrastructure
    tests/PennyPath.Tests
  frontend/            (Vite + React + TS)
  docker-compose.yml
  .github/workflows/ci.yml, cd.yml
```

## 3. Data model
- **User**(Id, Email, PasswordHash, CreatedAt)
- **ConnectedSource**(Id, UserId, Type[Gmail|Sms], EncryptedRefreshToken, LastSyncAt)
- **Transaction**(Id, UserId, Date, Amount, Type[Income|Expense], Merchant, CategoryId, Account, Source[Email|Sms|Manual], ExternalId, RawHash)
  - unique on (UserId, RawHash) to prevent duplicates
- **Category**(Id, UserId nullable, Name, Icon)
- **MerchantRule**(Id, UserId, MerchantPattern, CategoryId) for learned corrections
- **Insight**(Id, UserId, Month, Type[Saving|Investment], Text, CreatedAt)
- **Budget**(Id, UserId, CategoryId, MonthlyLimit) from v0.2

## 4. Core flow: ingest to dashboard
1. Sync: fetch emails (filtered by bank/UPI senders), or receive SMS text.
2. Parse: regex parser per bank template pulls amount, debit/credit, merchant and date. If it can't parse, fall back to Claude extraction.
3. Dedupe: compute RawHash and skip existing rows.
4. Categorize: check MerchantRule, then keyword rules, then Claude.
5. Store the transaction. The raw body is not kept.
6. Dashboard queries aggregate by month and category.
7. Insights (v0.2): send monthly aggregates (not raw data) to Claude and store the returned saving and investment ideas.

## 5. API (MVP)
| Method | Route | Purpose |
|---|---|---|
| POST | /api/auth/register, /login | Auth |
| GET | /api/sources/gmail/connect, /callback | OAuth |
| POST | /api/sources/gmail/sync | Import emails |
| POST | /api/sms/import | Paste or upload SMS text |
| GET | /api/transactions?month=&category= | List |
| PATCH | /api/transactions/{id} | Edit category |
| GET | /api/dashboard/summary?month= | Income, expense, savings, breakdown, trend |
| GET | /api/insights?month= | (v0.2) AI ideas |

## 6. Screens
Login/Register, Connect Sources, Dashboard (month picker, KPI cards, category donut, trend line), Transactions (table with filters and inline category edit), Insights (v0.2), Settings (disconnect and delete data).

## 7. CI/CD and Docker
- **Dockerfiles:** `backend/Dockerfile` (multi-stage, SDK to aspnet runtime), `frontend/Dockerfile` (node build to nginx).
- **docker-compose:** services `db` (postgres), `api`, `web`.
- **CI** (on PR and push): backend `dotnet restore, build, test`; frontend `npm ci, lint, test, build`; build Docker images.
- **CD** (on tag or push to main): push images to a registry (GHCR) and deploy to the chosen host.
- **Secrets:** Google client secret, Claude API key and JWT key go in GitHub Secrets and env vars, never in the repo.

## 8. Build order (Phase 3 and 4)
1. Scaffold the repo, solution, Vite app, Dockerfiles and compose (walking skeleton: React calls API, API reads DB).
2. CI pipeline, green on the empty skeleton.
3. Auth (M1).
4. Transactions CRUD and the dashboard on manual data (M6, M7).
5. Parser with unit tests on sample bank messages, plus SMS import (M3, M4).
6. Gmail OAuth and sync (M2).
7. Categorization (M5).
8. CD and deploy (M8), release v0.1.
