# Remote / devcontainer / SSH sessions

**One-liner:** Run sessions in a devcontainer, on a remote SSH host, or in a persistent dev box — UI stays local.

**Tier:** QoL (strategically large) · **Status:** Backlog

## Why this matters

Low individual vote count in t3code (#2310 devcontainer, #1795 devcontainer support, #1414 project-scoped SSH) but strategically the feature that decides whether Conclave is "tool for my laptop" or "tool that runs my fleet."

Three real workflows:

1. **Devcontainers** — agent runs in the project's devcontainer so it has the right toolchain, no host pollution.
2. **Remote SSH** — agent runs on a beefy dev box; user's laptop is just the UI.
3. **Always-on dev environment** — orchestration/scheduled sessions (file 03 power-user) keep running when the laptop sleeps.

## Sketch

- Session config gains a **"runtime"** field:
  - `local` (today's behavior).
  - `devcontainer` — `.devcontainer/devcontainer.json`-aware, builds and runs.
  - `ssh` — connect to host, set up worktree there, run agent there.
- All transports speak the same session protocol (the CLI/API from file 02 power-user is the natural seam).
- Worktree lives wherever the agent runs. UI replicates state for display.
- Embedded terminal connects to the remote host's shell, not local.

## Open questions

- Storage: where do logs/transcripts live? On the host (durable, but inaccessible if host is offline) or replicated locally?
- Authentication: SSH keys are easy. Devcontainers may need Docker context configuration.
- Conclave's existing worktree manipulation assumes local fs — needs an abstraction layer.
- File access in the UI: when the user clicks a file in a remote session's diff, what happens? Stream it? Mount over SSHFS?

## Notes

Heavy. Probably the second-largest single item on this list after enterprise file 05 (self-hosted). Good case to defer until the CLI/API (file 02) is stable — that becomes the "talk to a remote agent" protocol. Pair architecturally with orchestration (file 03) for the unattended-execution use case.
