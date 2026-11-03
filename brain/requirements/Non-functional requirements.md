# Non-functional requirements

From `docs/requirements.md` §6.

| NFR | Target | Current |
|---|---|---|
| Security | encrypted tokens at rest, HTTPS, no raw email bodies stored, "delete my data" | partial — see [[Security checklist]] |
| Privacy | only transaction-like emails read (sender + keyword filter) | n/a until [[M2 Gmail import]] |
| Performance | dashboard < 2 s for 10k transactions | unmeasured |
| Quality | ≥ 70% unit-test coverage on parser and services | unmeasured |
| Parser accuracy | ≥ 90% on real bank alerts | unmeasured, see [[M3 Transaction parser]] |
| Categorization accuracy | ≥ 85% on ≥ 100 labelled transactions | unmeasured |
| Parser sample set | ≥ 100 real redacted alerts, ≥ 3 banks, user-labelled; accuracy reported per field | no real samples yet |
| Performance method | measured on the Docker Compose stack on a dev laptop; hardware noted in `docs/verification.md` | unmeasured |
| Observability | structured logs without personal data, error tracking, DB backups before real user data | not done |
| Time | amounts INR `decimal`; dates stored UTC; months computed in user time zone (default Asia/Kolkata) | not defined in code |
| AI privacy | opt-in, merchant text or aggregates only, can be turned off | n/a until AI features |
| AI | disclaimer: not licensed financial advice | n/a until AI features |

## Code check (every task)
- [ ] New logic in parser/services has unit tests
- [ ] Backend `dotnet test` and frontend `npm test` / lint / build pass (record actual output)
- [ ] No new feature stores more data than needed
- [ ] Money as `decimal`, INR assumed
