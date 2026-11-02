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
- Web: http://localhost:8080
- API: http://localhost:5000/api/health
- Postgres (host): localhost:5433

All ports bind to 127.0.0.1 only. Security notes and open items: [docs/security.md](docs/security.md)

## Configuration
- `JWT_KEY` and `POSTGRES_PASSWORD` (in `.env`): required, there are no default secrets. `JWT_KEY` must be at least 32 bytes.

## Run locally
```
# backend (needs PostgreSQL, e.g. `docker compose up db`)
cd backend && dotnet run --project src/PennyPath.Api

# frontend (needs Node >= 22.12)
cd frontend && npm install && npm run dev
```

## Tests
```
dotnet test backend/PennyPath.sln
cd frontend && npm test
```
