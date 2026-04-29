# Fleet view for engineering managers

**One-liner:** Cross-team dashboard: active sessions, stuck sessions, throughput per dev, % of agent PRs merged, time-to-first-review.

**Tier:** Enterprise · **Status:** Backlog

## Why this matters

Gives managers a reason to roll Conclave out top-down rather than waiting for organic adoption. Also: the visibility makes the cost analytics (file 02) defensible — "we ran 1,200 sessions, 340 became merged PRs, that's 28% conversion at $X per merged PR."

Buyer: Engineering managers, directors. The "I want to roll this out, not just allow it" feature.

## Sketch

- Dashboard rows: per-team and per-individual.
- Columns:
  - Active sessions, idle sessions, sessions needing attention.
  - Sessions started (week / month).
  - PRs opened from sessions, PRs merged, merge rate.
  - Median session-to-PR time.
  - Token spend.
  - Stuck-session count (idle + needs-attention > N hours).
- Drill-downs: click a user → their sessions → individual session view.
- Alerts: "X has 4 stuck sessions" or "Team Y's merge rate dropped 30% week-over-week."

## Open questions

- How does this differ from cost analytics (file 02)? Overlap is significant — could be one dashboard with two views (cost lens vs throughput lens). Probably should be.
- Privacy / surveillance concerns: how granular is "per-individual"? Aggregate-only mode for orgs that don't want manager-level inspection of ICs?
- What's the "stuck session" SLA — define defaults, let admins tune?
- Comparison view (this team vs that team) — useful, or invitation to rivalry?

## Notes

Good candidate to merge architecturally with cost analytics (file 02) — same data pipeline, different lenses. Probably ship file 02 first, then add the throughput view.
