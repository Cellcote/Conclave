# Conversation control while a turn is running

**One-liner:** Queue a follow-up, edit the next prompt, freeze the session, or fork it — all without stopping the current turn.

**Tier:** QoL · **Status:** Backlog

## Why this matters

t3code #1462 ("configurable follow-up behavior while turn running") and #2182 ("read-only mode") capture this directly. Today, while Claude is mid-tool-loop, the user has no good options: send-and-interrupt, or wait. Power users want to keep typing, queue up the next thought, or freeze the session for safety while they read.

Surprisingly little prior art — most AI tools force you into "wait" or "interrupt."

## Sketch

- **Queued follow-up:** while a turn is running, the user types in the prompt box. The text sits queued and submits automatically when the turn ends. Visible badge: "queued."
- **Edit-while-queued:** keep editing right up to send.
- **Read-only mode:** lock the session — no new prompts, no auto-resume, no tool calls beyond the current one. Useful when you want to inspect a long turn without accidentally derailing it.
- **Fork-from-here:** while the turn is running, fork at the latest committed turn (overlap with power-user file 04). Lets you start an alternate path while the original keeps running.

## Open questions

- What if the queued follow-up wants to reference state that the running turn changed? (E.g., "now also fix the test.") Probably fine — the model sees the updated state when it processes the queued prompt.
- Multiple queued prompts — one only, or a queue?
- Read-only mode + tool calls already in flight: cancel them, or let them finish?
- UI for the queued state — sticky banner? Inline preview at the bottom of the transcript?

## Notes

Small, polished, surprisingly differentiating. Cheap to build but the UI is fiddly to get right. Pair with file 04 fork (the underlying "branch from here" primitive is the same).
