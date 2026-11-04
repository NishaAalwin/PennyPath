# PennyPath

Personal finance tracker: reads bank emails/SMS, shows monthly income and expenses, and suggests ways to save and invest.

- Requirements: [docs/requirements.md](docs/requirements.md)
- Design: [docs/design.md](docs/design.md)

## Run everything (Docker)
First create your secrets file (never commit it): copy `.env.example` to `.env` and set
`POSTGRES_PASSWORD` and `JWT_KEY` (for example with `openssl rand -base64 48`).
```
docker compose up --build
```
- App (web and API on one HTTPS origin): https://localhost:8443 (API health: https://localhost:8443/api/health)
- Postgres (host): localhost:5433

The certificate comes from Caddy's local CA, so the browser warns until you trust it (see [docs/operations.md](docs/operations.md)).
All ports bind to 127.0.0.1 only; the API and web containers are reachable only through the proxy. Security notes and open items: [docs/security.md](docs/security.md)

## Configuration
- `JWT_KEY` and `POSTGRES_PASSWORD` (in `.env`): required, there are no default secrets. `JWT_KEY` must be at least 32 bytes.
- Email (verification and password reset): by default sent to the local **Mailpit** inbox at http://127.0.0.1:8025, nothing leaves the machine. For a real mail server set `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM` and `SMTP_USE_TLS=true` in `.env` (see [docs/operations.md](docs/operations.md) section 10).
- `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, optional `GOOGLE_REDIRECT_URI` (in `.env`): turn on the Gmail import. Off when empty. Setup steps: [docs/operations.md](docs/operations.md) section 8.

## Run locally
```
# backend (needs PostgreSQL, e.g. `docker compose up db`)
cd backend && dotnet run --project src/PennyPath.Api

# frontend (needs Node >= 22.12); proxies /api to the backend on :5000 so the session cookie works
cd frontend && npm install && npm run dev
```

## Tests
```
dotnet test backend/PennyPath.sln
cd frontend && npm test
```
