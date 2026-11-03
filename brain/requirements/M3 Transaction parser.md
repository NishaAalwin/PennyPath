---
id: M3
release: MVP
status: done-unmeasured
verified: 37 backend tests on samples; real-message accuracy NOT measured
---
# M3 Parser extracts transaction fields

**Requirement:** extract amount, debit/credit, merchant, date, account from bank messages (emails and SMS).

## Code check
- [x] Rule-based parser — `backend/src/PennyPath.Application/Parsing/SmsTransactionParser.cs`, result type `ParsedTransaction.cs`
- [x] Unit tests — `backend/tests/PennyPath.Tests/SmsTransactionParserTests.cs`
- [ ] **≥ 90% accuracy on real bank alerts** (NFR) — needs redacted real samples from the user
- [ ] **≥ 70% unit-test coverage** on parser and services — coverage not measured
- [ ] Handles email text as well as SMS (needed for [[M2 Gmail import]])
- [x] Input length bounded (2000 chars per message)

## Known gaps
Accuracy unmeasured. Ask the user for redacted real samples.

Related: [[M4 SMS import]], [[Non-functional requirements]]
