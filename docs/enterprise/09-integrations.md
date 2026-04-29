# First-class enterprise integrations

**One-liner:** Native connectors for Jira/Linear/ServiceNow/PagerDuty/Confluence/GitHub Enterprise/GitLab/Bitbucket — agent pulls ticket and incident context automatically.

**Tier:** Enterprise · **Status:** Backlog

## Why this matters

The agent that already has the ticket context, the incident, the Confluence runbook — that's the agent that gets adopted. The one where the user pastes the ticket URL every time gets uninstalled.

Buyer: VP Eng. Adopters: every IC.

## Sketch

- Per-org connector config (OAuth or service-account credentials, stored in control plane).
- On session start, infer relevant context from:
  - Branch name (`fix/JIRA-1234` → fetch JIRA-1234 + linked PRs).
  - Issue/ticket URL pasted into the prompt.
  - PagerDuty alert payload (for triage playbooks, file 06).
- Inject as auto-context (file references, ticket bodies, recent comments).
- Bidirectional where it makes sense: agent can comment on the ticket, transition states, link the PR.

## Connectors to build, in priority order

1. Jira / Linear (most-requested in any team B2B tool).
2. GitHub Enterprise + GitLab self-managed (existing `gh` integration covers cloud, but enterprise versions need separate auth + URL).
3. PagerDuty (incident-response playbooks).
4. Confluence / Notion (docs context).
5. ServiceNow (regulated industries).
6. Bitbucket Server (Cursor's "Bugbot for Bitbucket" thread had 73 replies — real demand).

## Open questions

- MCP-based or native? MCP is composable and the trend, but native gives us the polish (auto-context inference, branch-name parsing) that wins demos.
- Where does credential storage live? Control plane only; never in the desktop client.
- Can users write their own connectors? Probably yes via MCP — but we maintain the core set.

## Notes

Cursor's Bugbot-for-X threads validate the demand: Bitbucket (73r), JetBrains plugin (99r), GitLab (43r), Azure DevOps (41r). The pattern repeats — wherever a team already works, they want the agent to meet them there.
