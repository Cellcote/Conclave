# Per-phase / per-session model routing

**One-liner:** Different model for plan vs implement vs tidy. Per-session rules by tag, repo, time, or token-budget.

**Tier:** Power-user · **Status:** Backlog

## Why this matters

The single highest-leverage power-user feature for cost control. t3code #1879 ("Choose different model / harness during planning and implementation") and the Cursor "DeepSeek R1 + Claude Sonnet combo" thread both validate it. Users intuit the right tradeoff (Opus to plan, Sonnet to grind, Haiku to format) but today have no way to encode it.

## Sketch

### Per-phase routing

- Playbook (file 06 enterprise / file 07 power-user) declares a per-phase model:
  ```yaml
  playbooks:
    - name: feature
      phases:
        plan: { model: claude-opus-4-7, thinking: high }
        implement: { model: claude-sonnet-4-6 }
        review: { model: claude-haiku-4-5 }
  ```
- Phase transitions: explicit (user clicks "start implementing") or inferred (TodoWrite plan moves into "in progress").

### Per-session rules

- Match by tag, repo, branch pattern, time of day, current token spend.
- First match wins; default fallback.
- Surfaced in the UI: small badge on the session showing which rule matched and why.

### Escape hatch: OpenAI-compatible endpoint

- Single config slot for an OpenAI-compatible base URL (covers Ollama, OpenRouter, vLLM, Anthropic-compatible proxies).
- Lets users route to local/cheaper/exotic models without us shipping connectors for each.
- Cursor's top feature requests are dominated by "add model X" — this neutralizes the entire category in one feature.

## Open questions

- How aware are users of which rule fired? Quiet by default (badge), or always-explain mode?
- Per-phase: is "phase" a first-class concept or just a tag on a turn? First-class is cleaner but bigger scope.
- Cost guardrails: an Opus-routed session in a tight loop is dangerous — pair with `on_cost_threshold` hook (file 06).

## Notes

Reframed from the original power-user list (which described "routing rules" generically). The competitor-validated angle is *per-phase* — that's the one users explicitly ask for.
