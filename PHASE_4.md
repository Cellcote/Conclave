# Phase 4 — plan

Work deferred from Phases 2–3, plus items discovered while mining pingdotgg/t3code's
`ClaudeAdapter.ts`. Read alongside `NOTES.md` (which captures spike-era limits) and
the design handoff under `design_handoff_conclave/`.

---

## Phase 3 follow-up (shipped)

These were carry-overs from Phase 3 that have since been implemented. Listed here so
the original scoping is still discoverable.

### Transcript persistence — shipped
- `messages` table is written to on every append (`SessionManager.PersistMessage`).
- Schema: `(id, session_id, role, content, tools_json, created_at, seq, claude_uuid)`
  (migration v3, extended in v9).
- Loaded lazily on session activate via `LoadTranscriptIfNeeded`.

### Diff stats — shipped
- `WorktreeService.ComputeDiff` parses `--numstat` + `--name-status`.
- `SessionManager.RefreshDiff` is invoked after each `ResultEvent`.
- Cached in DB columns (`diff_files/add/del`) so the sidebar still has numbers when
  the worktree is unreachable.

### Cancel button — shipped
- `SessionVm.CancellationSource` set by `ClaudeService` for the duration of a turn.
- `ShellVm.CancelActiveTurn` cancels the linked CTS, which kills the child process.

### Per-assistant-message UUIDs — shipped
- `messages.claude_uuid` column (migration v9), populated from the `AssistantEvent`'s
  `uuid` field. Used by fork-at-message to map our row back to claude's JSONL chain.

### Token / cost display — shipped
- `Session.TotalCostUsd` accumulated from each `ResultEvent.TotalCostUsd`. Surfaced
  on `SessionVm.TotalCostFormatted` in the right panel.
- Per-event `usage` breakdown is *not* surfaced (input vs cache_read vs cache_create);
  could still be added if the difference becomes interesting.

### Partial-message streaming — shipped
- `--include-partial-messages` is passed unconditionally; `ClaudeService.HandleStreamDelta`
  appends `text_delta`s to the live `TranscriptMessageVm`.
- Tool-use input deltas are still ignored; we wait for the final `assistant` event for
  those.

### Tool-result meta: per-tool parsers — partial
- READ: line count, GLOB/GREP: match count, EDIT/WRITE: collapses success preamble to
  "ok". BASH: still falls back to first-line trim — exit-code + duration parsing is
  still TODO.

---

## Phase 4 — shipped

### Plan view — shipped
- `SessionVm.Plan` is an `ObservableCollection<PlanItemVm>`. `TodoWrite` invocations are
  parsed by `SessionManager.ParsePlanJson` and pushed via `SessionVm.ReplacePlan`.
- Persisted via `sessions.plan_json` (migration v4).
- Header / progress bar / row states render per `PlanView.axaml`.

### Logs view — shipped
- `SessionVm.Logs` is a 500-entry ring buffer of `LogLineVm` (`(ts, level, message)`).
- Lifecycle, informational stream events, and errors are routed through
  `ClaudeService.Log`. Not persisted (intentional).

### Permission handling — option 1 shipped
- Per-session `permission_mode` (migration v5) passed to claude via
  `--permission-mode`. UI selector: Prompt / Auto-accept edits / Full access.
- **Still open**: structured `AskUserQuestion` and `ExitPlanMode` flows. They render
  as ordinary tool calls today and the model will wait indefinitely for a response
  we can't give. Either (a) implement an MCP-based `canUseTool` callback, or
  (b) embed Node + Agent SDK. Both deferred. Sessions show a warning log line when
  these tools fire so the user knows why claude is hanging.

### PR card — shipped
- `GhService.TryGetPullRequest` runs `gh pr view`; refreshed on session load and after
  each turn. Hidden when `gh` isn't installed/authenticated.
- `Session.PrNumber/PrState/PrMergedAt` cached (migrations v2 + v7) so the card
  paints immediately on load.

### Empty state — shipped
- `EmptyState.axaml` renders when `!HasActiveSession`. Right panel auto-collapses
  via `MainWindow.ApplyResponsiveLayout`.

---

## Crosscutting nice-to-haves

### Shipped
- Filter pills filter (`ShellVm.ApplyFilter`).
- Project rename (`SessionManager.RenameProject` + sidebar context menu).
- Auto-expand project on filter/search match.
- Claude version gate (`ClaudeCapabilities.Detect` + `AtLeast`).
- Auto-cleanup of merged sessions (`AutoCleanupService`, settings v8).

### Still open
- **⌘⏎ submit** in the new-session modal — visual hint exists, key handler doesn't.
- **Auto-expand project on new session** — currently the sidebar shows the new
  session under its project but the chevron stays collapsed if the user had it
  collapsed.
- **Capability probe** — t3code's trick of starting a query, reading init, and
  aborting. Useful for an About dialog and to disable unavailable models in the
  selector.

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
   and structured I/O. Medium lift. Would unblock the `AskUserQuestion` /
   `ExitPlanMode` flows above.
3. **Embed Node + use Agent SDK**: heaviest; introduces a second runtime in the
   bundle. Cleanest feature parity.

Revisit this choice when either (a) we hit a CLI limitation we can't work around
(e.g. need true per-tool permission callbacks and MCP doesn't cut it), or (b)
platform bundling via a Node sidecar proves cheap on all three target OSes.
