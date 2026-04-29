# Session orchestration: DAGs, scheduled, triggers

**One-liner:** Chain sessions with dependencies, schedule them, or fire them on file/git/webhook events.

**Tier:** Power-user · **Status:** Backlog

## Why this matters

Right now Conclave is interactive — a human starts each session. Power users want a personal agent fleet: "when CI fails on this branch, spawn a triage session." "When *plan* finishes, kick off *implement* with the plan as input." "Every Monday at 8am, run a dependency-update session on these three repos."

t3code #2162 ("make a prompt dependent on another thread ending") and #1390 ("schedule prompts, recurring") explicitly validate the demand.

## Sketch

Three composable building blocks:

### DAGs (session pipelines)

- Define stages with dependencies: `plan → implement → review`.
- Output of each stage piped as context to the next (transcript summary, plan doc, list of files changed).
- Stage-level config: model, playbook, allowed tools.
- Live in `.conclave.yml` (file 07) or authored ad-hoc in the UI.

### Triggers (event-driven)

- File watcher on a path glob.
- Git events (push to branch, PR opened, PR comment matches `/conclave fix this`).
- CI failure webhook.
- PagerDuty incident (overlap with enterprise file 09).
- Cron / scheduled times.

### Lifecycle

- Triggered sessions show up in a "scheduled / triggered" sidebar section, distinct from interactive sessions.
- Run unattended — but always behind approval gates (enterprise file 08) for risky actions.
- Cost caps per trigger (don't let a runaway loop spend $500 of tokens overnight).

## Open questions

- Where do DAG/trigger definitions live? `.conclave.yml` for repo-scoped, user config for personal cron jobs.
- Concurrency: if a file-watch trigger fires twice in 30 seconds, do we queue, debounce, or run both?
- Failure handling: stage 2 fails — retry, abort, notify?
- Where does the agent live during unattended runs? Always on the user's laptop (problems when they close the lid), or can it run on a server?

## Notes

Strongly validated in both communities. Dangerous without guardrails — pair tightly with cost caps and approval gates. Probably *the* most impactful feature on this list, but also the heaviest to design safely.
