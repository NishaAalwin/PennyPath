# PennyPath

Personal finance tracker: reads bank emails/SMS, shows monthly income and expenses, and suggests ways to save and invest.

- Requirements: [docs/requirements.md](docs/requirements.md)
- Design: [docs/design.md](docs/design.md)

## Run everything (Docker)
```
docker compose up --build
```
- Web: http://localhost:8080
- API: http://localhost:5000/api/health
- Postgres (host): localhost:5433

## Configuration
- `JWT_KEY` (env var, at least 32 bytes): JWT signing secret. Compose has a dev-only default; set your own for real use.

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
