# Phase 4 — plan

Work deferred from Phases 2–3, plus items discovered while mining pingdotgg/t3code's
`ClaudeAdapter.ts`. Read alongside `NOTES.md` (which captures spike-era limits) and
the design handoff under `design_handoff_conclave/`.

---

## Phase 3 follow-up (still claude-integration work)

These are carry-overs from Phase 3 that we deliberately skipped to keep the first cut
shippable. Best done before anything else in Phase 4 so the transcript actually lives
across restarts.

### Transcript persistence
- `messages` table exists (migration v3) but nothing writes to it. Persist every
  `TranscriptMessageVm` (and its tool-call blobs) as they're appended.
- Schema: `(id, session_id, role, content, tools_json, created_at, seq)`. Serialise
  `Tools` via `System.Text.Json` — one row per message; the row owns its full tool
  list.
- On session activate, load `messages WHERE session_id = @id ORDER BY seq` into
  `SessionVm.Transcript`. Skip eagerly on app startup — load lazily on first activate.
- Edge case: if a turn was in flight when the app was killed, the last message may be
  half-written (no tool results). Load as-is and treat the session as Idle.

### Diff stats
- After each turn completes (`ResultEvent` with `!IsError`), run
  `git -C <worktree> diff --shortstat <base_branch>...HEAD` and parse the
  `N files changed, X insertions(+), Y deletions(-)` line.
- Update `Session.DiffFiles/Add/Del` in the DB + push to `SessionVm.Diff` so the
  sidebar `+N −M` updates live.
- Also compute per-file `FileChangeVm` list for the right-panel diff section via
  `git diff --numstat <base>` + `git diff --name-status <base>` (or a single
  `--stat --numstat`).

### Cancel button
- Expose `Stop` action on `SessionVm` / `ClaudeService`. Cancels the
  `CancellationToken` passed to `ClaudeClient.StreamAsync`, which kills the child
  process. A clean exit still needs to drain the event stream's final bits.
- Surfaces as a button replacing `Send` while `IsBusy`. Keybind: Esc while composer
  has focus and session is busy.

### Per-assistant-message UUIDs + branching resume
- Each assistant event carries its own `uuid`. Today we only track the session-level
  one. Store `lastAssistantUuid` and either pass `--resume-at` if the CLI supports it
  (verify), or record it as a bookmark on each `TranscriptMessageVm` so we can
  "branch from here" later (re-run a turn from a specific point).

### Token / cost display
- Sum `input_tokens + cache_creation_input_tokens + cache_read_input_tokens` for the
  real input cost. Current `ResultEvent.TotalCostUsd` is sufficient for display, but
  we throw away the breakdown. Expose on `SessionVm` and show in the right panel
  SESSION section alongside `Model`, `Started`, `PID`.
- `task_progress` / `task_notification` events carry `usage` blocks too — emit
  updates from those, not just end-of-turn. Requires parsing `task_*` events
  (currently lumped into `InformationalEvent`).

### Partial-message streaming (`--include-partial-messages`)
- Verify flag existence with `claude -p --help`. If present, pass it on spawn.
- On each `StreamDeltaEvent` with subtype `content_block_delta` + `delta.text_delta`,
  append the incremental text to the current assistant message's `Content`. Requires
  keeping a "live" `TranscriptMessageVm` reference inside `ClaudeService` across
  `stream_event`s within the same assistant turn.
- UX win: text appears word-by-word in the transcript instead of whole-message at a
  time.

### Tool-result meta: per-tool parsers
- Current heuristic: READ shows line count, others show first line. Upgrade to:
  - BASH: pull `exit code` + wallclock from the result envelope
    (`exit 0 · 1.4s` style)
  - EDIT / WRITE: parse the `+N −M` line count from the result summary
  - GLOB / GREP: `N matches` from the result content
- Still fine to fall back to the generic first-line trim for unknown tools.

---

## Phase 4 proper — the design's still-missing views

### Plan view (Terminal / **Plan** / Logs tab)
- Source of truth: Claude's `TodoWrite` tool input. Input shape:
  `{ todos: [{ content, status, activeForm }, ...] }` where `status ∈
  {pending, in_progress, completed}`.
- Wire: when we see a `tool_use` with name `TodoWrite`, parse `input.todos`,
  replace the session's `Plan` state (or merge by `content`).
- Render per design (`NOTES.md` from design_handoff):
  - Header: "Current plan · X of Y complete" + 4px progress bar.
  - Rows: done = green check, strikethrough; doing = accent ring + "in progress"
    label, panel-bg row highlight; todo = outlined ring, normal text.
- Plan lives on `SessionVm.Plan` — nullable ObservableCollection of PlanItemVm.
  Persisted via a new JSON blob column on `sessions` or a dedicated `plans` table.

### Logs view
- Structured session log, separate from the transcript. Captures:
  - Session lifecycle events (created, selected, branch switch, process spawn/exit)
  - Claude non-durable system events (hook_started/progress/response,
    compact_boundary, auth_status)
  - Stderr lines from the claude process
  - Our own errors (git failures, parse failures)
- Shape: `(ts, level, message)` where level ∈ INF / WRN / ERR / DBG.
- Storage: ring buffer per session, in memory. Don't persist for this iteration —
  logs get noisy fast; can add persistence later if useful.
- Design: mono 11.5/1.75, 3-column layout (96px ts / 30px level badge / message).

### Permission handling
The blocking question from the t3code research: our CLI-based integration doesn't
give us a `canUseTool` callback. Options:

1. **`--permission-mode` flag** (blunt): pass `bypass` for full-trust sessions,
   `default` for prompting. `default` mode means claude will emit a permission
   request we'd need to respond to on stdin — unclear shape, needs investigation.
2. **MCP-based permission prompt tool**: register a local MCP tool that claude
   calls instead of the built-in permission dialog; we handle it in-process and
   return the decision. More infrastructure but gives us the callback.
3. **Embed Node + use Agent SDK**: biggest lift, cleanest result. Architectural
   decision — treat as a separate spike.

Decision for Phase 4: ship option 1 as the quick path (per-session "mode" setting:
Prompt / Auto-accept edits / Full access), and park options 2–3 for a later spike.

Special tools t3code surfaces specially — worth replicating even under option 1:

- **`AskUserQuestion`**: claude invokes this to ask the user structured questions.
  Intercept and render as a modal (matches the design's permission-modal shape),
  return answers as the tool result.
- **`ExitPlanMode`**: claude invokes this when leaving plan mode. Intercept,
  capture the plan markdown for the Plan view, then deny the tool with a message
  instructing claude to wait for the user's feedback (otherwise it just keeps
  going).

These are separate from the generic permission modal — they need their own flows.

### PR card (right panel PULL REQUEST section)
- Trigger: sessions whose branch is pushed (check via `git rev-parse
  --symbolic-full-name <branch>@{upstream}`) and has an associated PR.
- Data source: `gh pr view --json number,state,headRefName,baseRefName,commits,isDraft,url,title`.
  Refresh on session activate (or with a small TTL cache).
- Fallback: if `gh` isn't installed / not authenticated, hide the PR card entirely.
- Populate `Session.PrNumber` + `PrState` + a PR title field (may need schema v4).
- State badge: `DRAFT` / `OPEN` / `MERGED` / `CLOSED` with the design's uppercase
  tag pill.

### Empty state
- When `Projects.Count == 0` OR no active session, main pane shows:
  - Big 64×64 dashed outline square with ◱ glyph.
  - "No sessions yet" — 18/600.
  - Explanation paragraph.
  - `New session` (primary) + `Import from running claude` (outline) buttons.
  - Keybind hint: `⌘N new session · ⌘K command palette`.
- Hide the right panel in this state.
- Right panel hiding logic is already stubbed (`IsVisible="{Binding RightPanelVisible}"`);
  wire it to `HasActiveSession` and the new empty-state trigger.

---

## Crosscutting nice-to-haves

These aren't a single "view" but improve the app meaningfully.

- **Filter pills actually filter** — currently the sidebar filter selection is
  visual only. Hook `SelectedFilter` on `ShellVm` → derive a filtered view of
  projects' sessions for the sidebar binding.
- **Project rename** — sessions have it, projects don't. Same inline-edit pattern.
- **⌘⏎ submit** in the new-session modal (visual hint exists, key handler doesn't).
- **Auto-expand project** when a new session is added (currently have to click the
  chevron).
- **Claude version gate** — Opus 4.7 requires CLI ≥ 2.1.111. On startup, check
  `claude --version`; surface a warning banner if the user picks an Opus 4 model on
  an older CLI.
- **Capability probe**: t3code's trick — start a query, read init, abort. No tokens
  consumed, gives us model availability / subscription info. Useful for a future
  About dialog and for the model-selector to disable unavailable options.

---

## Architectural open question (deferred indefinitely)

**Subprocess spawn vs Agent SDK.** t3code gets a lot for free from the Node SDK:
multi-turn streaming via a single long-lived `query()` fed by an async iterable,
permission callbacks, per-message lifecycle events. Our per-turn CLI-spawn approach
works but costs a process-start per turn and gives us less granular control.

Three paths:
1. **Stay with CLI** (current). Replicate features manually. Simplest packaging;
   no Node runtime dependency. What we're doing now.
2. **MCP-based augmentation**: CLI base + local MCP server for permission prompts
   and structured I/O. Medium lift.
3. **Embed Node + use Agent SDK**: heaviest; introduces a second runtime in the
   bundle. Cleanest feature parity.

Revisit this choice when either (a) we hit a CLI limitation we can't work around
(e.g. need true per-tool permission callbacks and MCP doesn't cut it), or (b)
platform bundling via a Node sidecar proves cheap on all three target OSes.
