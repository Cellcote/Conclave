# Org-level policy guardrails

**One-liner:** Admin-defined allow/deny lists for models, repos, tools, file patterns, and shell commands — not user-overridable.

**Tier:** Enterprise · **Status:** Backlog

## Why this matters

Security and platform teams need to be able to say "yes, you can use Conclave, but the agent must never touch `/infra/prod/**`, must never run destructive shell commands, must never call out to non-Anthropic models." Without this, deployment stalls at security review.

Buyer: Security / Platform. User-invisible until it blocks something.

## Sketch

- Org policy document (versioned, signed). Stored in the control plane.
- Policy dimensions:
  - **Models:** allowed providers, allowed models, max thinking budget.
  - **Repos:** allow/deny list, branch patterns (no direct commits to `main`).
  - **Tools:** allowed MCP servers, allowed shell commands (regex), denied file patterns.
  - **External I/O:** can the agent open external PRs? Push to public remotes? Hit non-allowlisted URLs?
- Enforced in the harness, not the UI — bypassing the UI doesn't bypass the policy.
- Policy violations: surfaced inline in the session, optionally logged to audit (file 04).

## Adjacent power-user feature

A lighter "tool-call auto-review" mode (validated in t3code #2384) lets *individual* users opt into the same diff-then-execute behavior. Same machinery, different default. Worth shipping in tandem.

## Open questions

- Where do policies live? Single org-wide doc, or layered (org → team → repo → user)?
- How are exceptions handled — can a team admin grant a one-off override, or is it strictly central?
- Do policies travel with `.conclave.yml` (file 07) for offline machines, or always require live control-plane access?
- Schema: declarative YAML, or something richer like Rego/CEL?

## Notes

Defensive but with a power-user twin. The "auto-review tool calls" hook hits a real friction point that t3code users explicitly ask for.
