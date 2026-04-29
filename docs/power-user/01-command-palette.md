# Command palette + keyboard control

**One-liner:** Cmd-K palette, vim-style nav, vim/shell ergonomics in the chat input. Mouse-optional.

**Tier:** Power-user · **Status:** Backlog

## Why this matters

Once you're running 10+ sessions, the mouse is a tax. Power users keep dropping to the terminal because the GUI is slower than tmux + a wrapper script — fix that and they stop leaving.

## Sketch

- **Palette** (Cmd/Ctrl-K): new session, switch session, kill session, rename, jump-to-PR, toggle status filter, run a playbook, fork session, open repo in editor.
- **Nav:** `j/k` between sessions, `gg/G`, `/` to search session names, `:` for command mode (vim users will know).
- **Configurable bindings:** ship sane defaults; allow override via settings JSON.
- **Chat input ergonomics:**
  - Vim mode toggle (t3code #1783 — known ask).
  - Shell-style history with up-arrow recall (t3code #1777).
  - `@` for symbols/files (lightweight version; LSP-backed indexing is its own scope, see notes).

## Open questions

- Do we ship our own keymap config format, or piggyback on VS Code's keybinding JSON for familiarity?
- Vim mode inside the chat input vs full-app vim navigation — both, or just one?
- How much fuzziness in the palette — ranked, prefix, or fzf-style subsequence?

## Notes

Cheap, beloved, low-risk win. Both t3code (#1783, #1777) and Cursor power-user threads validate the chat-input ergonomics specifically. Symbol indexing (t3code #2386) is a much bigger scope and probably belongs in a different file if we ever do it — call out as out-of-scope here.
