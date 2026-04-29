# Shared playbooks & session templates

**One-liner:** Org-pinned prompt recipes and agent templates — captured once by senior engineers, one click for everyone.

**Tier:** Enterprise · **Status:** Backlog

## Why this matters

This is where the *productivity story* lives. SSO and audit logs unblock the deal; playbooks are why a team picks Conclave over the next vendor. Tribal knowledge ("how I usually triage a Sentry alert") becomes a reusable artifact.

Buyer: VP Eng — and every IC who uses it daily.

## Sketch

- A **playbook** is a named template containing: system prompt, default model + thinking budget, allowed tools, MCP servers, optional starter user message, optional checks/hooks.
- Authored in-app or via `.conclave.yml` (file 07).
- Pinned at three scopes: **org** (everyone sees), **team** (visible to a group), **personal**.
- Versioned with edit history. Reviewable like a doc — comments, approvals.
- Library view: search, filter by tag (triage, scaffold, refactor, migration), sort by use count.
- Telemetry: which playbooks get used, by whom, with what outcomes (PR merge rate).

## Open questions

- Where's the source of truth — control plane or git repo? Probably both, with the repo `.conclave.yml` overriding.
- How do we handle drift when a playbook references a tool/MCP server that doesn't exist for some users?
- Should playbooks be composable (one playbook references another) or kept flat in v1?
- Public/community library — eventually, but not v1.

## Notes

One of my recommended top-3 to ship first in MVP enterprise. Highest user-visible value of the tier — and the seam to power-user file 07 (config-as-code), which is the same machinery without the org-scoping.
