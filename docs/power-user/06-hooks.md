# Custom hooks

**One-liner:** User-defined scripts on lifecycle events: `on_session_start`, `on_status_change`, `on_tool_use`, `on_pr_opened`.

**Tier:** Power-user · **Status:** Backlog

## Why this matters

Power users want to wire Conclave into their existing tooling — Slack pings on completion, log shipping for their own audit trail, post-session test runs, custom notifications, integration with Things/Linear/etc. Hooks are the unopinionated escape hatch that lets them do all of it without us building each integration.

## Sketch

- Hook events:
  - `on_session_start`, `on_session_end`
  - `on_status_change` (running, idle, needs-attention, errored)
  - `on_tool_use` (each tool call — useful for logging or rate-limiting)
  - `on_turn_end`
  - `on_pr_opened`, `on_pr_merged`
  - `on_cost_threshold` (configurable)
- Hook = shell command or script. Receives JSON event payload on stdin (or env vars).
- Configured at three scopes:
  - Per-user: `~/.config/conclave/hooks.json`
  - Per-repo: `.conclave.yml` (file 07)
  - Per-org: control plane (overlap with enterprise file 03 policy)
- Sandboxed: hooks run with the user's permissions, but timeout-bounded. Failures logged, never block the session.

## Open questions

- Shell script only, or also support a JS/Python in-process hook for low-latency cases?
- How does this interact with Claude Code's existing settings.json hooks? Probably layered: Conclave hooks fire on Conclave events; Claude Code hooks fire on Claude Code events. No conflict.
- Should `on_tool_use` be able to *block* a tool call (becomes a policy mechanism), or only observe?

## Notes

Cheap to ship if scoped to "fire-and-forget shell commands." Becomes much more involved if we let hooks block or mutate. Start with observe-only.
