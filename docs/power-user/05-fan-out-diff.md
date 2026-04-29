# Fan-out & diff

**One-liner:** Run the same prompt across N sessions (different models, prompts, worktrees) and view results side-by-side.

**Tier:** Power-user · **Status:** Backlog

## Why this matters

Two real workflows:

1. **A/B model comparison.** "Did Opus or Sonnet handle this refactor better?" Today you run them serially and lose the comparison.
2. **Prompt iteration.** "Three phrasings of the same ask — which got the cleanest plan?"

Power users will pay for this if the output is genuinely comparable.

## Sketch

- "Fan out" command from any session start: pick one prompt, choose N variants (model, system prompt, playbook).
- N parallel sessions launched, all in their own worktrees.
- A **comparison view** when they finish:
  - Side-by-side transcripts.
  - Diffs of the file changes each one made.
  - Side-by-side terminal/test output if any.
  - Cost + token counts + wall-clock per run.
- "Promote one" action — adopt one variant's worktree as the canonical answer; abandon the rest.

## Open questions

- Cost caps: spawning 5 Opus runs is expensive. Default cap, surface it before launch.
- Comparing non-textual outputs (e.g., one ran tests and they passed, another didn't) — how to render?
- "Stop the others when one succeeds" — useful or premature optimization?
- Storage: 5 worktrees per fan-out gets heavy fast. Tied to the worktree cleanup story (overlaps with file 04 fork).

## Notes

Specific use case of file 04 (fork) — fan-out is essentially "fork before turn 1, N times." Could be implemented on the same primitives. The comparison UI is the differentiating part.
