# Project config-as-code (`.conclave.yml`)

**One-liner:** Per-repo defaults checked into git: model, allowed tools, MCP servers, playbooks, hooks, default prompts.

**Tier:** Power-user · **Status:** Backlog

## Why this matters

Two problems solved at once:

1. **Team alignment** — "my Conclave is set up differently than my teammate's." Drop in `.conclave.yml`, everyone's defaults match.
2. **The seam between power-user and enterprise** — same config format, with org-level policy overriding repo-level config. Build once, use in both tiers.

## Sketch

```yaml
# .conclave.yml
version: 1

defaults:
  model: claude-opus-4-7
  thinking_budget: high
  allowed_tools: [bash, edit, read, write, grep, glob]

mcp_servers:
  - name: postgres
    command: npx -y @modelcontextprotocol/server-postgres
    args: [$DATABASE_URL]

playbooks:
  - name: triage
    system_prompt_file: .conclave/triage-prompt.md
    model: claude-sonnet-4-6
  - name: implement
    system_prompt_file: .conclave/implement-prompt.md

hooks:
  on_session_end: scripts/notify-slack.sh
```

- Read on session start. Schema-versioned. Validation errors surfaced clearly (don't silently fall back).
- Precedence: org policy (file 03 enterprise, hard) > repo `.conclave.yml` > user defaults > built-in defaults.
- Reload on file change without restarting the app.

## Open questions

- YAML or TOML? YAML wins on familiarity; TOML wins on no-surprises.
- How do we handle secrets in the file? Env var references only — never inline.
- Should `.conclave.yml` support nesting (sub-configs per directory) for monorepos?
- Migration story: when we bump schema versions, what happens to old files?

## Notes

Strategically the most important power-user file in this list. Shipping this opens the door to almost everything else (playbooks, hooks, model routing, orchestration). I'd ship a minimal v1 (defaults + mcp_servers + playbooks) before any of those, and grow it as features land.
