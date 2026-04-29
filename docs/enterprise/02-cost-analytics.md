# Cost & usage analytics

**One-liner:** Per-user, per-team, per-repo token spend, model mix, and cost-per-PR.

**Tier:** Enterprise · **Status:** Backlog

## Why this matters

A VP Eng or FinOps lead has to answer "is this $X/seat actually paying for itself?" — and that's the question that drives renewal. Today they're looking at an Anthropic console bill with no attribution to teams or outcomes.

Buyer: VP Eng, Finance. The renewal-justification feature.

## Sketch

- Capture token usage per session (input/output, cache hits, model used) — already partly available from the SDK.
- Roll up by user, team, repo, project, time window, model.
- Cost-per-PR: tie token spend to merged PRs from agent sessions.
- Dashboard: spend trend, top users, top repos, model mix, cache-hit %.
- Export: CSV, scheduled email, webhook to Looker/Snowflake.

## Open questions

- Cost-attribution model: per-session is easy, per-repo needs the worktree-to-repo mapping, per-team needs SSO+group sync first.
- Do we surface cache-hit % as a primary metric? It's the #1 cost lever and most users don't know it exists.
- "Cost-per-PR" is meaningful but tricky — count abandoned sessions? Failed PRs? Need to define the denominator clearly.
- BYOK customers (file 05) won't go through our billing — do we still ingest their usage events for the dashboard?

## Notes

One of the three I'd ship first in an MVP enterprise tier — high leverage, modest effort, sells itself in a demo.
