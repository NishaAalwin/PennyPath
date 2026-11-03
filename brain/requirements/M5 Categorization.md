---
id: M5
release: MVP
status: not-started
---
# M5 Auto-categorize transactions

**Requirement:** rules first, AI fallback; user can correct a category.

## Code check (must hold when built)
- [ ] Rule-based categorizer runs first; unit tested
- [ ] AI (Claude API) fallback only for unmatched items
- [ ] **Data minimisation:** send only merchant/description needed, never account numbers, balances or raw messages; AI use needs user consent ([[Security checklist]] item on AI)
- [ ] API key read from config/env, never committed, never sent to the browser
- [ ] User correction persists and is not overwritten by re-categorization
- [ ] Category updates scoped to the owning user
- [ ] Failure of the AI call degrades gracefully (uncategorized, no crash)
- [ ] Tests incl. cross-user update attempt returns 404/403

Feeds [[M6 Monthly dashboard]] and [[M7 Transaction list]].
