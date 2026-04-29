# Fork a session at any turn

**One-liner:** Branch the conversation at turn N to try a different approach, without losing the original thread.

**Tier:** Power-user · **Status:** Backlog

## Why this matters

The single most-requested *new-paradigm* feature in every AI-IDE community I've looked at. Strongly validated in t3code (#1404, "conversation branching — fork thread from a message") and the same instinct surfaces in Cursor threads.

The workflow it enables: "this run went off the rails at turn 7, but turns 1-6 were good — let me retry from there with a different prompt."

## Sketch

- Per-turn action: **"Fork from here."**
- Forking creates:
  - A new session with the transcript copied up to (and including) turn N.
  - A new git worktree at the same commit as the parent (cheap; shared object database).
  - A reference back to the parent in session metadata.
- Lineage UI: each session shows its parent and its forks. Tree view in the sidebar.
- Diverging worktrees: each fork can make its own commits independently.
- "Promote a fork" — adopt a fork's worktree state as the canonical one for the parent's branch.

## Open questions

- Do forks share the parent's transcript history (read-only) or get a true copy?
- Worktree explosion: 5 forks × 5 sub-forks = 25 worktrees. Auto-cleanup heuristic? Sticky for forks that produced a merged PR, prune aggressively otherwise.
- Costs: every fork starts fresh on context — tokens add up. Surface this clearly.
- Multi-fork race: if two forks both push to the same branch, who wins? (Probably each fork creates a new branch automatically.)

## Notes

High-conviction feature. Cheap to prototype (transcript copy + worktree-add is mostly already there). The lineage UI is the harder part — get it right and this becomes a signature Conclave feature.
