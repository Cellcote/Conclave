# Approval gates for risky actions

**One-liner:** Configurable human-in-the-loop checkpoints before risky agent actions (push to protected branch, external PR, destructive shell command).

**Tier:** Enterprise · **Status:** Backlog

## Why this matters

Lets security teams say *yes* to autonomous agent work by drawing a clear line: agents can do everything, but the last step before something irreversible needs a human signoff.

Buyer: Security. Adopters: any team running unattended/triggered sessions (see power-user file 03).

## Sketch

- Gate dimensions:
  - **Git:** push to a protected branch, open a PR to a public/external repo, force-push, branch delete.
  - **Shell:** commands matching configured regex (`rm -rf`, `kubectl delete`, `terraform apply`).
  - **Network:** outbound calls to non-allowlisted hosts.
  - **Cost:** session token spend exceeds threshold.
- Approval channel:
  - In-app banner with approve/deny.
  - Slack/Teams DM with deep-link.
  - Email fallback.
- Approver assignment: explicit per-gate, OR the team's on-call, OR session co-pilot list (from file 07).
- Timeouts: default-deny after N minutes for unattended sessions.
- Audit: every approval/denial logged (file 04).

## Open questions

- Bypass for senior engineers in their own sessions? Or always-on for everyone?
- What about a "soft" gate (warn but proceed) vs. hard gate (block)?
- How do gates interact with policy (file 03)? Policy denies = no negotiation; gates = negotiable with approval.
- Can a gate's approver be the agent itself in a different session? (Probably no, but the question will come up.)

## Notes

Critical companion feature for unattended/scheduled sessions (file 03 power-user). Without gates, the "watch mode" feature is too risky to enable in any serious environment.
