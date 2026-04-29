# Skills auto-discovery + extension hooks

**One-liner:** Auto-detect Claude skills installed on the user's system; lightweight extension API for third-party UI surfaces.

**Tier:** QoL · **Status:** Backlog

## Why this matters

t3code #1480 ("auto-discover Claude skills", 3 comments) and #1582 ("Extension System / Marketplace for custom integrations") both validate the appetite. Skills are part of Claude Code's runtime — Conclave should surface them, not hide them.

## Sketch

### Skills auto-discovery

- Scan `~/.claude/skills/`, project-local `.claude/skills/`, and any path declared in `.conclave.yml` (power-user file 07).
- For each, surface in the session UI: which skills are loaded, which got triggered this turn.
- "Run skill manually" action — useful for validation.
- Skill telemetry: which skills get used, how often, by which sessions (overlap with cost analytics — enterprise file 02).

### Extension hooks (lighter than full marketplace)

- Three integration surfaces, in increasing scope:
  1. **Hooks** (already in power-user file 06) — script-level, fire-and-forget.
  2. **Sidebar panels** — register a custom panel (HTML/web component) that shows alongside the transcript. Read-only access to session state.
  3. **MCP servers** — already supported via `.conclave.yml`. Document the seam clearly.
- A real extension marketplace is a much bigger commitment — not v1.

## Open questions

- For sidebar panels: webview sandbox? Permissions model? This gets expensive fast — probably worth deferring.
- Skill discovery: do we own the skill format, or just adopt Claude Code's? (Latter — don't fork the ecosystem.)
- How does skill discovery interact with policy (enterprise file 03)? Org probably wants an allowlist for which skills are loaded in work sessions.

## Notes

Auto-discovery is small and self-contained — ship that. Extension panels are tempting but dangerously open-ended; defer until there's a clear concrete use case driving the design (and concrete users asking for it, not just speculative "users might want plugins").
