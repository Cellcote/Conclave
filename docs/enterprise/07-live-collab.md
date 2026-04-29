# Live multi-user collaboration

**One-liner:** Two or more humans observing and steering the same Claude session, Google-Docs style.

**Tier:** Enterprise · **Status:** Backlog

## Why this matters

Pair-programming with an agent. Onboarding juniors by having them watch a senior + Claude work through a problem. Code review of a session in progress. Incident response where someone needs to take over a stuck agent. None of these workflows have a good story today.

Buyer: VP Eng — sells the team-collaboration angle. ICs love it.

## Sketch

- Session presence: who's watching, who has the cursor, who's typing.
- Shared transcript view, real-time updates.
- Steering controls: who can send the next prompt, who can approve a tool call, who can take over.
- Two modes:
  - **Observe** — read-only, sees everything live.
  - **Co-pilot** — can send prompts, approve gates, edit drafts. Turn-taking, not concurrent typing.
- Cursor / selection sharing on transcripts and the embedded terminal.
- Hand-off: explicit "pass the wheel" action; alerts the next person.

## Open questions

- CRDT-style concurrent edit, or simpler turn-taking? Turn-taking is plenty for v1.
- What's the transport — does this require the control plane (file 05), or can it work peer-to-peer for hosted users?
- Permissions model: any team member can join, or invite-only per session?
- How does this interact with approval gates (file 08)? Probably approvers from the session presence list by default.

## Notes

Big lift, but a clear differentiator — Cursor doesn't have it, t3code doesn't have it. The "session that more than one human can drive" is a unique angle for a session-manager product.
