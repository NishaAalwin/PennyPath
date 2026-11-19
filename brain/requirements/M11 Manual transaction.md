---
id: M11
release: MVP
status: not-started
---
# M11 Manual add of a transaction

**Requirement:** add a transaction by hand when the parser fails.

**Acceptance:** it appears in the list and the dashboard like any other.

## Code check (must hold when built)
- [ ] Endpoint is `[Authorize]`, owner taken from the token, never from the body
- [ ] Validation: amount > 0, `decimal`, direction, date not absurd, text length limits
- [ ] Marked as manual source; included in [[M6 Monthly dashboard]] totals
- [ ] Participates in dedupe ([[M9 Data quality]])
- [ ] UI form with validation messages

Related: [[M7 Transaction list]]
