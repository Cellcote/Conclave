# Conclave CLI / scripting API

**One-liner:** `conclave new`, `conclave list --json`, `conclave watch`. Scriptable from shell, CI, Raycast, Hammerspoon.

**Tier:** Power-user · **Status:** Backlog

## Why this matters

Power users automate. Today the only way to start a Conclave session is the GUI; that means it can't participate in their workflows (cron jobs, post-merge hooks, Raycast quick-actions, CI failure responders).

## Sketch

- A `conclave` binary that talks to the running desktop app over a local Unix socket / named pipe.
- Commands:
  - `conclave new --repo <path> --branch <name> --template <name> --prompt "..."`
  - `conclave list [--status running|idle|needs-attention] [--json]`
  - `conclave attach <session-id>` — opens the session in the desktop app.
  - `conclave kill <session-id>`
  - `conclave watch [--filter ...]` — streams session state changes (newline-delimited JSON).
  - `conclave run <playbook-name> --repo <path>` — run a playbook (file 06 enterprise / file 07 power-user).
- If the desktop app isn't running, `conclave new` starts it headless and returns the session ID.
- Output: JSON for scripts, pretty for humans (auto-detected from `isatty`).

## Open questions

- Headless mode: should the app run without a window if invoked via CLI on a CI machine? What does "needs attention" mean with no UI to surface it?
- Authentication: local-socket-only is fine on a personal machine; what about a remote dev box (overlap with QoL file 04)?
- Versioning: do we make this a stable public API early, or move fast and break it for a while?

## Notes

Foundation for orchestration (file 03). Should ship before or alongside it — DAGs without a CLI are useless from CI / cron.
