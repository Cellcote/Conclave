# Export & share transcripts

**One-liner:** Export a session to markdown / HTML; optionally generate a shareable read-only link.

**Tier:** QoL · **Status:** Backlog

## Why this matters

Universal ask: t3code #1496 ("thread-to-markdown button"), Cursor "Export Chat" (54 replies). Probably a 1-day feature. Useful for: writing post-mortems, sharing a session with a teammate for review, attaching to PRs, archiving knowledge.

## Sketch

- **Export:** session → markdown (default) or HTML. Includes prompts, responses, tool calls (collapsed by default), file diffs, plan/todo state.
- **Configurable redaction:** scrub specific turns, scrub regex matches (API keys, tokens), or whitelist-only mode.
- **Share link** (optional, hosted-only feature):
  - Read-only URL.
  - Expiry, password, or SSO-required (overlap with enterprise file 01).
  - Public links disabled by default for orgs (policy from enterprise file 03).

## Open questions

- Do we ship hosted sharing, or only file export? File export is dead simple. Hosted sharing requires backend + threat model.
- Markdown structure: do we render tool calls inline, or as collapsible sections at the bottom?
- Image / file attachments — embed as base64, link to local files, or upload?

## Notes

Pair with full-text search (power-user file 09) — exporting the result of a search query is a natural extension.
