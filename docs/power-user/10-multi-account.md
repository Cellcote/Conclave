# Multi-account support

**One-liner:** Personal Pro + work Team account, switchable per project. No more juggling logins.

**Tier:** Power-user · **Status:** Backlog

## Why this matters

t3code #1444 (3 comments), #2111 (2 comments), and Cursor's "Seamless Account Switching" thread (93 replies) all validate this. Anyone with both a personal subscription and a work one hits this friction daily — and right now Conclave probably doesn't handle it cleanly either.

## Sketch

- Multiple credentials registered per user: personal Pro key, work Team account, BYO Anthropic API key, Bedrock IAM role, Vertex SA.
- **Binding levels:**
  - Per-project (default): "this repo always uses my work account."
  - Per-session override: "this one session uses personal Pro for an open-source contribution."
  - Global default for unbound projects.
- Quick switcher in the session header.
- Visual cue: project icon or session badge color reflects which account it's bound to (avoid sending work code through the personal subscription by accident).
- Credentials in OS keychain (Keychain on macOS, Credential Manager on Windows, Secret Service on Linux).

## Open questions

- Account-types we support v1: just Anthropic (Pro/Team/API)? Or also Bedrock/Vertex from day one? Bedrock/Vertex naturally falls out if file 08 (model routing) ships first.
- Can two sessions in the same repo use different accounts simultaneously? (Probably yes; useful for cost-routing and for testing.)
- Recovery flow when a credential expires mid-session.
- How does this interact with the enterprise SSO story (file 01 enterprise)? Org-managed credentials override personal ones? Or coexist?

## Notes

Sleeper feature — low individual vote counts but high engagement per voter, and a constant friction point. Cheap to ship and turns "I have to keep two apps open" into "I don't think about it." Pair naturally with model routing (file 08).
