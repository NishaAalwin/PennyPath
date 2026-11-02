# Foundation verification status

Last updated: 2026-11-02

| Check | Status | Notes |
|---|---|---|
| Backend restore, build (Release), test | Verified | Same commands as `ci.yml`; 0 warnings, 1 test passes |
| Frontend `npm ci`, lint, test, build | Verified on Node 24 | Default machine Node is 22.1.0, which is too old for Vite 8 (needs >= 22.12) |
| `docker compose up --build` | **Verified** | db (healthy), api and web all start; both Dockerfiles build (the frontend image runs `npm ci` and `npm run build` on Node 24) |
| API connects to PostgreSQL | **Verified** (compose Postgres 17) | `GET http://localhost:5000/api/health` returns `{"status":"healthy","database":true}` |
| Web served | **Verified** | `http://localhost:8080` returns 200, serving the built bundle |
| CORS for web origin | **Verified** | `Access-Control-Allow-Origin: http://localhost:8080` on API responses |
| React page rendering the API result in a real browser | Not verified | Headless Edge returned no DOM output; verified indirectly (bundle built with `VITE_API_URL=http://localhost:5000`, CORS header present) |
| API against the local (non-Docker) PostgreSQL 17 service | Not verified | The `pennypath` login fails because the DB/user hasn't been created yet; optional now that compose Postgres works |
| GitHub Actions CI | **Verified** | Run on push of 14be2e2 (2026-11-02): backend, frontend and docker jobs all succeeded on GitHub. Repo: https://github.com/NishaAalwin/PennyPath |
| CD (`cd.yml`) | Not configured | Needs repo variable `API_URL` and a deploy target |

## Fixes made during verification
- Docker Desktop could not start because WSL was the old inbox version. Fixed with `wsl --update` (WSL 3.0.1.0 installed), then restarted Docker Desktop.
- Compose Postgres host port changed from 5432 to **5433**, because the local PostgreSQL service already uses 5432.

## Not configured
- Hosting/deploy target.
- Secrets: Google OAuth client, Claude API key (needed for later milestones).
- Remote: GitHub `NishaAalwin/PennyPath` (private). Pushes from this machine need a credential with the `workflow` scope.
- Database migrations: the `Transactions` table does not exist yet.

## M1: sign-up and login (2026-11-02)

| Check | Status | Notes |
|---|---|---|
| Backend unit and API tests | Verified | 12 pass: register, validation, duplicate (case-insensitive), login, identical error for wrong password and unknown user, JWT accepted/rejected |
| Frontend tests, lint, build | Verified (Node 24) | 6 tests pass |
| Register, login, `/me` against the compose stack and Postgres | Verified with curl | 201 on register, 409 duplicate, 400 weak password, 401 bad login, `/me` 200 with token and 401 without. Password stored as an ASP.NET Identity salted hash |
| EF migration applied on startup in compose | Verified | Creates `Users` and `Transactions` tables. The one-time `fail:` log line about `__EFMigrationsHistory` is EF's normal first-run check |
| Login/sign-up forms in a real browser | Not verified | No working headless browser here; the form logic was only exercised through the API and the unit tests |
| Rate limiting (20 requests/min/IP on auth) | Not verified | Configured but not load-tested. Behind a reverse proxy the client IP needs forwarded-headers configuration |

### Known limitations / follow-ups
- The JWT is kept in `localStorage`. It is simple, but readable by any XSS. Consider httpOnly cookies later.
- No refresh tokens, email verification or password reset yet. Tokens last 60 minutes.
- The compose `Jwt__Key` default is dev-only. Set `JWT_KEY` for any real deployment.
- The first migration is named `AddUsers` but also creates `Transactions`.
