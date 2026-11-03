# Constraints and decisions

## Stack (decided)
React + TypeScript (Vite) · ASP.NET Core .NET 10 (Api / Application / Domain / Infrastructure) · PostgreSQL via EF Core · Claude API for AI · Docker Compose · GitHub Actions.
**Code check:** a task must not introduce a different framework/database without a decision recorded here.

## Constraints
1. SMS cannot be read from a web app → MVP uses paste/import; Android app is v0.3.
2. Gmail read is a restricted scope → test-user mode only in development.
3. Financial data is sensitive → see [[Security checklist]].
4. AI advice is not licensed financial advice → disclaimer required.

## Assumptions
India, INR, UPI (open decision).

## Open decisions
1. Country/currency focus confirmation
2. Android: React Native vs Kotlin
3. Hosting: Azure / AWS / VPS
4. MVP audience: author + ≤ 100 Gmail test users (recommended) vs public launch (needs Google verification and audit)
5. Move the Android SMS app (C1) earlier than v0.3? Pasting SMS weakens the "no manual logging" promise
6. Do investment ideas (S1) fall under SEBI investment-advice rules? Check before building S1
7. Is AI categorization acceptable? Recommended: opt-in, merchant text only

Resolve these with the user, record the answer here, then update `docs/requirements.md`. Requirements review of 2026-11-03 produced items 4–7 and M9–M12.

## User working preferences
SDLC phases; verify each layer before building on it; document anything unverified in `docs/verification.md`; never ask for DB superuser passwords.
