# Full-text search + session replay

**One-liner:** Grep across every transcript you've ever run; replay any past session against a new model or new prompt.

**Tier:** Power-user · **Status:** Backlog

## Why this matters

"When did I last debug that flaky test?" "What was the prompt I used to scaffold the auth service?" Power users accumulate hundreds of sessions; they're a knowledge base nobody can search. This makes the history into a real tool.

t3code #1486 ("search within thread") explicitly asks for the simple version; both communities ask for export (which solves the same problem worse).

## Sketch

### Search

- Full-text index across all session transcripts (prompts, responses, tool outputs, file diffs).
- Filters: by repo, by date, by status (errored, successful, abandoned), by model, by tag.
- Inline preview of match context.
- "Open session" deep-links into the historical session view.

### Replay

- Replay = take a past session's prompts, run them again with:
  - A different model.
  - A different system prompt / playbook.
  - A different repo state (current `main` instead of when it was originally run).
- Useful for: "did the new model handle this better?" / "is this still a problem on today's main?"
- Replays are new sessions with a parent ref to the original (overlaps with file 04 fork lineage).

## Open questions

- Index where? Local-only (SQLite FTS), or syncable across machines (overlap with QoL file 02 settings sync)?
- Storage: long transcripts add up. Tiered (recent N hot, older compressed)?
- Privacy/redaction: searchable history may have secrets pasted in. Easy way to scrub a turn after the fact.
- Replay determinism: model output is non-deterministic; how do we frame "this replay produced different files than the original" — bug or feature?

## Notes

Strongly validated in both competitor backlogs. The replay angle is unique — nobody offers it well. Search alone is a strong feature; search + replay is signature.
