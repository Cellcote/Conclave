# Conclave

**A desktop home for your Claude Code sessions — many at once, each in its own git worktree.**

Conclave is a cross-platform native app that wraps the `claude` CLI and gives
each agent its own isolated workspace. Spin up parallel sessions across
projects, follow what every Claude is doing, and never juggle branches by hand
again.

![Conclave](./screenshot.png)

---

## Why

Running `claude` in a terminal is great for one session. It stops scaling the
moment you want two — branches collide, prompts get lost between tabs, and
there's no shared view of what each agent is up to.

Conclave fixes that by giving every session:

- Its own **git worktree**, so branches never step on each other.
- A persisted **transcript, plan, and log stream**, browsable long after the
  process exits.
- A live **status** (working / waiting / running tool / idle / error) you can
  see from the sidebar without opening the session.
- An optional **pull request card** that picks up the linked PR from `gh` once
  the branch is pushed.

You drive the whole thing from a Linear/Raycast-flavored three-column shell.

---

## Highlights

- **Parallel sessions, one window.** Sidebar groups sessions by project; a
  filter row narrows to running / needs-attention / idle.
- **Embedded terminal.** Custom Avalonia `TerminalControl` with a hand-rolled
  VT parser and a PTY-backed read loop — colors, paging, and most TUIs work.
- **Plan view.** When Claude calls `TodoWrite`, the checklist renders as a
  proper progress view with done / in-progress / todo states.
- **Logs view.** Structured per-session log (lifecycle events, hook traffic,
  stderr) separate from the transcript.
- **Worktree-aware.** Creating a session runs `git worktree add` for you;
  diff stats (`+N −M`) and the PR card stay in sync with that worktree.
- **NativeAOT-ready.** `dotnet publish` produces a single native binary; the
  csproj is set up so reflection-based JSON is a build error rather than a
  silent fallback.
- **Themeable.** Dark/light, five accent hues, three densities, three radius
  scales — all exposed as preferences, designed in from the start.

---

## Requirements

- **.NET 10 SDK** — the project targets `net10.0`.
- **macOS, Linux, or Windows.**
- **`claude` CLI on `PATH`** for actual Claude sessions. (The embedded
  terminal will fall back to your `$SHELL` / `pwsh` / `cmd` if `claude` is
  missing, so you can still poke at it.)
- **`git`** on `PATH` — worktree management shells out.
- **`gh`** *(optional)* — used only to populate the PR card. If not
  installed/authenticated, the card hides itself.

## Build and run

```sh
dotnet restore
dotnet run --project src/Conclave.App
```

To produce a native binary:

```sh
dotnet publish src/Conclave.App -c Release
```

---

## Layout

```
src/Conclave.App/
  Program.cs, App.axaml(.cs), MainWindow.axaml(.cs)   Avalonia entry + shell host
  Claude/
    ClaudeClient.cs, ClaudeService.cs                 Spawns claude, streams JSON events
    StreamJsonParser.cs, StreamJsonEvent.cs           Stream-JSON event types
    ClaudeCapabilities.cs                             CLI version / capability probe
  Sessions/
    Database.cs, MessageRow.cs                        SQLite persistence
    SessionManager.cs, Session.cs, Project.cs         Domain + lifecycle
    WorktreeService.cs, GhService.cs                  git worktree + gh PR integration
    SlugGenerator.cs                                  Branch / worktree path slugs
  Terminal/
    TerminalControl.cs                                Custom Avalonia control — render + input
    TerminalBuffer.cs, TerminalCell.cs                Grid, cursor, scroll region
    VtParser.cs                                       Hand-rolled VT state machine
    PtySession.cs                                     Porta.Pty wrapper + read-loop channel
    GlyphCache.cs, Palette.cs                         Glyph cache + ANSI/256 colors
  ViewModels/
    ShellVm.cs, ProjectVm.cs, SessionVm.cs            Top-level + per-session state
    TranscriptMessageVm.cs, ToolCallVm.cs             Transcript + tool calls
    PlanItemVm.cs, LogLineVm.cs, DiffStatVm.cs        Plan / Logs / diff
    PullRequestVm.cs, NewSessionVm.cs, FilterVm.cs    PR card + modals + filters
  Views/Shell/
    Sidebar, MainPane, RightPanel, TitleBar           Three-column shell
    PlanView, LogsView, EmptyState                    Main-pane modes
    NewSessionModal, Toast, MarkdownView              Modals + transcript rendering
  Design/                                             Theme tokens (themes × accents × density)
```

Design references live under [`design_handoff_conclave/`](./design_handoff_conclave) —
HTML/React prototypes that the Avalonia views are recreated from.
[`NOTES.md`](./NOTES.md) covers the embedded-terminal spike notes (VT
coverage, known limits). [`PHASE_4.md`](./PHASE_4.md) is the current roadmap.

---

## Dependencies

- [`Avalonia`](https://avaloniaui.net) 12.0.1 — cross-platform native UI.
- [`Porta.Pty`](https://www.nuget.org/packages/Porta.Pty) 1.0.7 — PTY layer
  (ConPTY on Windows, forkpty on POSIX).
- `Microsoft.Data.Sqlite` — session / transcript persistence.

---

## Status

Active development. The shell, terminal, and per-session plumbing are wired
up; a number of items from [`PHASE_4.md`](./PHASE_4.md) — transcript
persistence on restart, live diff stats, the cancel button, richer tool
result parsers, and the permission modal — are still in flight. Expect rough
edges.

## License

MIT — see [`LICENSE`](./LICENSE).
