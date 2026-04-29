# Conclave feature backlog

Brainstormed feature ideas, grouped by tier. Each file is a small spec: the problem, who feels it, a rough sketch, and open questions. Pick one up and turn it into a plan when you're ready to build.

The bar for inclusion was *interesting and at least loosely validated* — most are echoed in competitor backlogs (Cursor forum, t3code GitHub issues).

## Enterprise tier

Features that unlock paying organizations. The first three (SSO, audit, policy) are *defensive* — they unblock deals but don't differentiate. The rest are where the actual product story lives.

- [01 — SSO + SCIM](./enterprise/01-sso-scim.md)
- [02 — Cost & usage analytics](./enterprise/02-cost-analytics.md)
- [03 — Org-level policy guardrails](./enterprise/03-policy-guardrails.md)
- [04 — Immutable audit log + SIEM export](./enterprise/04-audit-log-siem.md)
- [05 — Self-hosted / VPC deployment with BYO key](./enterprise/05-self-hosted-byok.md)
- [06 — Shared playbooks & session templates](./enterprise/06-shared-playbooks.md)
- [07 — Live multi-user collaboration](./enterprise/07-live-collab.md)
- [08 — Approval gates for risky actions](./enterprise/08-approval-gates.md)
- [09 — First-class enterprise integrations](./enterprise/09-integrations.md)
- [10 — Fleet view for engineering managers](./enterprise/10-fleet-view.md)

## Power-user tier

Features for the individual developer running 5+ sessions and willing to invest in their setup. Items 1, 4, 9 turn up across both Cursor and t3code as top asks — strong validation.

- [01 — Command palette + keyboard control](./power-user/01-command-palette.md)
- [02 — Conclave CLI / scripting API](./power-user/02-cli-api.md)
- [03 — Session orchestration (DAGs, scheduled, triggers)](./power-user/03-orchestration.md)
- [04 — Fork a session at any turn](./power-user/04-fork-session.md)
- [05 — Fan-out & diff](./power-user/05-fan-out-diff.md)
- [06 — Custom hooks](./power-user/06-hooks.md)
- [07 — Project config-as-code (`.conclave.yml`)](./power-user/07-config-as-code.md)
- [08 — Per-phase / per-session model routing](./power-user/08-model-routing.md)
- [09 — Full-text search + session replay](./power-user/09-search-replay.md)
- [10 — Multi-account support](./power-user/10-multi-account.md)

## Quality-of-life

Smaller wins, often surprisingly high-engagement in competitor forums. Cheap to ship, build goodwill.

- [01 — Export & share transcripts](./qol/01-export-and-share.md)
- [02 — Notifications & settings sync](./qol/02-notifications-and-sync.md)
- [03 — Conversation control while a turn is running](./qol/03-conversation-control.md)
- [04 — Remote / devcontainer / SSH sessions](./qol/04-remote-dev.md)
- [05 — Skills auto-discovery + extension hooks](./qol/05-skills-and-extensions.md)

## How these were sourced

- **Last brainstorm turn** (this conversation) — the original 10 enterprise + 10 power-user lists.
- **t3code GitHub issues** (`pingdotgg/t3code`) — closest direct competitor, ~90 open feature issues sorted by engagement.
- **Cursor forum** (`forum.cursor.com`) — top feature-request threads by replies/views.

When a feature here is echoed in competitor backlogs, the file's **Notes** section calls it out.
