# Handoff: Conclave — main app shell

A cross-platform desktop GUI (Avalonia / .NET 10) that wraps the `claude` CLI and manages multiple Claude Code sessions, each running in its own git worktree.

---

## About the design files

The files in this bundle are **design references created in HTML/React** — prototypes showing intended look and behavior. They are **not production code to copy directly**.

The task is to **recreate these designs in the Conclave codebase's real environment**:

- **Target stack:** C# on .NET 10, Avalonia 12.0.1, Skia-rendered cross-platform native UI.
- **Do not** ship HTML, React, or any web runtime. Translate the visuals and behaviors into Avalonia XAML + C# view-models using Avalonia's existing primitives (`Grid`, `StackPanel`, `Border`, `SelectableTextBlock`, `ItemsRepeater`, etc.) and the existing `TerminalControl`.
- Follow Avalonia MVU / MVVM conventions already established in `src/Conclave.App/`.

## Fidelity

**High-fidelity.** Colors, typography, spacing, corner radii, row heights, icon treatment, and state transitions are all intentional. Recreate them pixel-accurately in XAML where Avalonia allows. Where Avalonia primitives don't map 1:1 (e.g. CSS `oklch()` colors, `box-shadow` with multiple layers), translate to the closest equivalent (`Color.FromRgb` using the converted sRGB value; `BoxShadow` for Avalonia 12) and document the choice in a code comment.

---

## Chosen direction

The user picked **Variant B — Linear/Raycast-clean**:
- Flat neutral surfaces, no glows or gradients
- Crisp sans-serif type (Inter)
- Restrained color — single accent hue used sparingly
- Monospace only inside the terminal, transcript metadata, tool pills, and keybind hints
- Segmented control for view-switching
- Sharp-but-not-brutal corner radii (3–7px scale)

The locked-in tweak values the user settled on:
```
theme        = dark
accent       = cool        (oklch(0.72 0.13 240))
font         = inter
density      = 1 (normal)
radius       = 1 (medium)  → xs:3, sm:5, md:7, lg:9
sidebarWidth = 264 px
rightPanel   = true
toolStyle    = pill         (compact tool calls)
```

Other tweak values (light mode, other accents/fonts, cozy/dense, sharp/soft radii, expanded tool cards) should remain **supported** as configuration — users should be able to change them in Preferences. Design them in from the start rather than retrofitting.

---

## Domain model

A **Project** is a local git repo. Each project has zero or more **Sessions**. A **Session** is a running `claude` CLI instance tied to a specific **Worktree** (a `git worktree` of the project). A worktree may be linked to a **Pull Request**.

```
Project 1..* Session 1..1 Worktree 0..1 PullRequest
```

Session status enum (exhaustive — design uses all of them):
- `working`        — Claude is generating tokens
- `waiting`        — needs user input (permission prompt, question)
- `idle`           — completed, awaiting next prompt
- `running-tool`   — bash command or file edit in progress
- `error`          — crashed or last action failed
- `queued`         — waiting for a free worker slot
- `completed`      — terminal state, session closed

Mock data in `data.jsx` is representative of the shapes needed; use it as the source of truth for field names.

---

## Screens / views

The design covers one top-level window with seven scenarios. All share the same three-column shell.

### Shell layout

```
┌────────────────────────────────────────────────────────────────────┐
│  Titlebar (38 px)                                                  │
├───────────┬──────────────────────────────┬─────────────────────────┤
│           │                              │                         │
│  Sidebar  │   Main pane                  │   Right metadata panel  │
│  264 px   │   flex                       │   320 px (collapsible)  │
│           │                              │                         │
│           │                              │                         │
└───────────┴──────────────────────────────┴─────────────────────────┘
```

- Window radius: 12px, native macOS traffic lights (Avalonia's `ExtendClientAreaToDecorationsHint` + custom drag region).
- Column dividers: single 1px line in `--border`.

### 1. Titlebar

Content (left → right):
1. 80px padding for traffic lights.
2. Brand mark: 16×16 rounded square (`radii.sm`) filled with `--text`, containing white "C" — 9px, 800 weight.
3. "Conclave" — 12.5px, 600 weight, `--text`, letter-spacing `-0.01em`.
4. Breadcrumb separator `›` in `--text-mute`.
5. Active project name in `--text-dim`.
6. `›` separator, then active session title in `--text` (500 weight, truncated with ellipsis at 420px).
7. Right side: `⌘K` tag (subtle pill) and **New session** button (primary — `--text` bg, `--bg` fg).

Pills (`BTag`): 3×9 padding, 11.5px/500, 1px border `--border`, radius `radii.sm`.

### 2. Sidebar

Top: search pill — full-width, 1px border `--border`, radius `radii.md`, 6×10 padding, "Search" placeholder, `⌘P` hint right-aligned.

Filter pills (static list, not collapsible):
- `◱ All sessions` with count (selected by default — background `--panel`)
- `● Running` (accent dot, pulse halo)
- `● Needs attention` (warn dot)
- `● Idle` (mute dot)

Each pill: 7×10 padding, radius `radii.sm`, 12.5px. Trailing count in monospace 11px, `--text-mute`.

Project groups, each rendered as:
- Header row: `▾` caret, project name 12px/600 `--text`, session count trailing in `--text-mute`.
- Session rows: 7×10 padding, 22px left indent, `radii.sm` corner.
  - 6px status dot (with 3px pulse halo for working / running-tool) — see status color map below.
  - Title: 12.5px, 500 if selected else 400, color `--text` if selected else `--text-dim`. Truncate with ellipsis.
  - Meta line (monospace, 10.5px, `--text-mute`): `branch · +add −del · #pr`.
  - Unread badge: pill, 14×14 min, accent-tinted background (`--accent-mid`), accent text, 10px/700, monospace.
  - Selected row: `--panel` background.

### 3. Main pane

**Header (12 × `--main-pad`):**
- Session title — 15px/600, `-0.015em`, `--text`.
- Sub-line: status label + branch (mono 11.5px) + model + `#pr` — 12px `--text-dim`, `·` separators in `--text-mute`.
- Right: segmented control for **Terminal / Plan / Logs** — 2px inner padding, 1px border `--border`, background `--panel`. Active segment: `--bg` fill, 1px `--border`, `--text`; inactive: transparent, `--text-dim`.

**Body** — depends on the active segment (see scenarios).

**Composer (bottom):**
- 1px `--border-hi` border, radius `radii.md`, bg `--panel`, 10×12 padding.
- Top row: placeholder "Continue the session…" — 13.5px `--text-mute`.
- Bottom row: `@ file`, `/plan`, `Sonnet 4.5` pills; right side `⏎` hint (11px mute) and **Send** button — 5×12, 12/600, `--text` bg on `--bg` fg.

### 4. Right metadata panel (320 px, togglable)

Three sections, each with uppercase label 11px/600 `--text-dim` letter-spacing `0.08em`:

1. **Session** — key/value grid. Properties: Status, Branch (mono), Worktree (mono), Base (mono), Model, Started (mono), PID (mono). Labels `--text-dim`, values `--text` right-aligned with ellipsis.
2. **Pull request** — card with state badge (uppercase tag), `#number` (mono), branch-arrow-base line, metadata tail.
3. **Diff · N files** — `+add / −del` totals (mono, 12), then file list: status letter (A/M/D colored) + path (ellipsis) + `+/-` tail. All mono 11.5px.

---

## Scenarios (main-pane body)

### `terminal` — transcript

Messages stack with 24px vertical rhythm. Each message:
- Label row: 11px, 500, `--text-dim`. For user: "You · HH:MM". For assistant: 6px accent dot + "Claude · HH:MM".
- Body: 13.5px/1.55 line-height, `--text`.
- Tool calls (assistant only) — one per line, 3px gap:
  - **Compact pill** (default): 6×10 padding, 1px `--border`, radius `radii.sm`, monospace 11.5px. Columns: uppercase kind (36px min, `--text-dim` 10px), target (ellipsis, `--text`), status glyph + meta (ok=`--ok` "✓", fail=`--err` "✕", pending=`--warn` "…").
  - **Expanded card** (alt tool style): double-row card — header strip (`--panel-2` bg) with status dot + kind + target + meta; body strip showing preview (for bash: `$ cmd…` with dim follow-up lines; for edit/write: green `+` diff lines).

### `plan`

Header: "Current plan · X of Y complete" + 4px progress bar (accent fill on `--panel` track).

Checklist:
- `done`: green filled circle with white ✓, text `--text-dim`, strikethrough.
- `doing`: accent ring with filled center, 1px `--border-hi` bordered row on `--panel` bg, trailing "in progress" label.
- `todo`: 1.5px `--border-hi` ring, text `--text`.

10×12 padding per row, `radii.sm`, 13.5px text.

### `logs`

Pure monospace, 11.5px/1.75 on `--bg`. Three columns: timestamp (96px `--text-mute`), level badge (30px, color by level: INF=`--info`, WRN=`--warn`, ERR=`--err`, DBG=`--text-dim`), message (`--text`). Left pad 14px.

### `error`

Transcript as normal, but the assistant's failing tool-call renders "✕ exit 1". Following the message, an **error card**:
- 1px `--err` border, background `color-mix(in oklch, var(--err) 8%, transparent)`.
- Header: "✕ Build error" 12/600 `--err` + error code (mono 11, `--text-dim`).
- Body: 13px `--text`, then file:line (mono 11.5, `--text-dim`).
- Actions: **Ask Claude to fix** (err-filled primary), **Open file**, **Copy error** (secondary outlines).

### `permission` — modal overlay

Centered modal (480px, `radii.lg`, `--bg` with `--border-hi`, big shadow). Backdrop: `color-mix(in oklch, black 40%, transparent)`.

Content:
- Warn `!` chip (22px circle, warn tint bg) + title "Allow Claude to run this command?"
- Attribution: "From session "<title>""
- Command card: mono body showing `$ cmd` and `cwd: …` — `--panel-2` bg.
- Explanation paragraph, `--text-dim`.
- Footer: "Don't ask again for `git worktree *`" checkbox, right-aligned **Deny** / **Allow once** (accent primary).

### `new-session` — modal overlay

540px modal. Fields (each: uppercase 11px label + input):
- Project (dropdown-style row: name + `~/path` mono + ▾)
- Branch (mono input showing `feat/...`) with source pills: "branch from main / existing branch / detached".
- Worktree path (readonly mono, auto-computed).
- Model: three segmented buttons (Haiku/Sonnet/Opus), middle selected.
- Initial prompt: multi-line textarea (72 min-h) with `--text-mute` placeholder.

Footer: `⌘⏎ create` hint, **Cancel** / **Create session** (primary).

### `empty`

Main pane centers a stack:
- 64×64 dashed `--border-hi` square `radii.lg` containing ◱ glyph.
- "No sessions yet" — 18/600 `-0.02em`.
- Explanation paragraph (380px max, 13/1.55, `--text-dim`).
- Actions: **New session** (primary) + **Import from running claude** (outline).
- Keybind hint: `⌘N new session · ⌘K command palette` — mono 11.5, `--text-mute`.

Right panel is hidden in this scenario.

---

## Design tokens

Tokens are driven by three axes: `theme × accent × density/radius`. Full generator in `variant-b-full.jsx` → `makeTokens()`. The locked defaults (`dark` / `cool` / normal / medium) produce:

### Colors — dark
```
--bg         #0D0E10
--panel      #131418
--panel-2    #181A1F
--panel-hi   #1E2026
--border     #222429
--border-hi  #2E3139
--text       #E8E9EC
--text-dim   #8A8D96
--text-mute  #4E5058
--accent     oklch(0.72 0.13 240)      /* cool */
--accent-fg  #0D0E10
--accent-dim oklch(0.72 0.13 240 / 0.14)
--accent-mid oklch(0.72 0.13 240 / 0.28)
--ok         oklch(0.74 0.14 158)
--warn       oklch(0.80 0.15 80)
--err        oklch(0.68 0.18 25)
--info       oklch(0.72 0.12 240)
```

### Colors — light (equivalent generator)
```
--bg         #FDFDFC
--panel      #F7F7F6
--panel-2    #F1F1EF
--panel-hi   #ECECEA
--border     #E6E6E3
--border-hi  #D4D4D0
--text       #0F1012
--text-dim   #666870
--text-mute  #9A9CA3
--accent     oklch(0.55 0.14 240)
--ok/warn/err/info  — same hue at lightness 0.50–0.58
```

### Accent palette (swap hue, keep chroma/lightness)
```
orange  h=32    cream h=72    cool h=240 (default)    green h=158    magenta h=320
```

### Radius scale (medium)
```
xs 3   sm 5   md 7   lg 9
```
Sharp: 2/3/4/6 — Soft: 5/8/11/14.

### Density scale (normal)
```
row-pad    7 px (y)     10 px (x)
main-pad   24 px        12 px top
```
Cozy: 9 / 12 / 32 / 16. Dense: 5 / 9 / 18 / 9.

### Typography
- **UI sans**: Inter 400/500/600/700. Swap-able: Geist, IBM Plex Sans, SF Pro.
- **Mono**: JetBrains Mono, fallback `ui-monospace, SFMono-Regular, Menlo`.
- Scale used: 10, 10.5, 11, 11.5, 12, 12.5, 13, 13.5, 15, 18. Line-height 1.45–1.65 depending on density.
- Letter-spacing: body neutral; display −0.01em to −0.02em; uppercase labels +0.08em.

### Spacing
Driven by density block above. Consistent use of 2/4/6/8/10/12/14/16/18/22/24/28/32 throughout.

### Shadow
Modals only:
```
0 30px 60px -10px rgba(0,0,0,0.5), 0 10px 20px -5px rgba(0,0,0,0.25)
```

---

## Interactions & behavior

- **Sidebar row click** → select session, load transcript in main pane, update right panel.
- **Segmented control** → switch main-pane view between Terminal / Plan / Logs. Transcript state must be preserved when switching away.
- **Status dot pulse** → `0 0 0 3px <color>22` halo, breathing 1.2 s ease-in-out, applied only to `working` and `running-tool`.
- **Composer**: `⏎` sends, `⇧⏎` newline. `@` opens file picker, `/` opens slash-command menu. Send button disabled when empty.
- **Permission modal**: ESC = Deny. ⏎ = Allow once. Checkbox persists the rule to the project's `.conclave/permissions.json`.
- **New-session modal**: `⌘⏎` creates. Validate branch name + that worktree path doesn't exist. On create, run `git worktree add <path> <branch>` via LibGit2Sharp, spawn `claude` in that cwd, navigate to the new session.
- **Unread counter** resets when the session becomes active (user opens it).
- **Truncation**: all session titles and file paths ellipsize at container width. No wrapping in sidebar rows or tool pills.

## State management

Suggested VM shape (one per session, observable):

```csharp
class SessionVm {
  Guid Id;
  string Title;
  string Branch;
  string Worktree;
  string Model;
  SessionStatus Status;       // enum listed above
  DiffStat Diff;              // {Files, Add, Del}
  PullRequestRef? Pr;         // {Number, State}
  int UnreadCount;
  DateTime StartedUtc;
  int Pid;
  ObservableCollection<TranscriptMessage> Transcript;
  ToolCall? InFlightTool;
  Plan? Plan;                 // for the Plan view
  ObservableCollection<LogLine> Logs;
}
```

Top-level `ShellVm` owns `ObservableCollection<ProjectVm>`, `SelectedSessionId`, `ActiveView` (Terminal/Plan/Logs), `RightPanelVisible`, `ThemePreference`, and the current modal (None / Permission / NewSession / CommandPalette).

Plan state is derived server-side from Claude's tool calls (the existing `TodoWrite` tool). Logs are the raw PTY byte stream, already captured by `PtySession`.

## Files in this bundle

- `Conclave.html` — entry point; wires tweaks and renders `<VariantBFull>` inside a scaled `ConclaveWindow`.
- `variant-b-full.jsx` — all the Variant B components (titlebar, sidebar, main pane, right panel, all seven scenarios, modal shell, design-token generator).
- `data.jsx` — mock `CONCLAVE_DATA` (projects → sessions → worktrees) and `TRANSCRIPT` (realistic assistant transcript with tool calls).
- `window.jsx` — macOS traffic-light chrome.
- `tweaks-panel.jsx` — floating tweaks panel (not part of the shipped app — purely a design-time knob).

To view the designs locally: open `Conclave.html` in a browser. Use the Tweaks panel (toggle from the corner) to flip scenarios, theme, accent, font, density, radius, sidebar width, right-panel visibility, and tool-call style.
