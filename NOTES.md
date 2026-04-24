# Conclave terminal spike — notes

## Dependency decisions

- **`Porta.Pty`** for the PTY layer. The obvious pick (`Pty.Net` via
  `microsoft/vs-pty.net`) turned out to be unlisted on NuGet with no stable
  release, so I moved to `Porta.Pty` — explicit Windows (ConPTY) + POSIX
  (forkpty) support, MIT, still being updated as of April 2026.
- **Hand-rolled VT parser** instead of `VtNetCore` / `VtNetCorePatched`.
  The original `VtNetCore` is unlisted on NuGet and its maintainer has
  publicly shifted the project toward being an academic test platform. The
  BastionZero fork exists but is GitHub-only, which means a git submodule
  or in-tree source copy for a one-off spike — not worth it. A minimal
  hand-rolled parser covering only what a shell + `claude` actually emit
  is ~300 lines, has zero dependency risk, and lets us control the hot
  path ourselves.

## VT coverage

**Implemented:**
- UTF-8 decode in Ground state (1–4 byte sequences)
- C0 controls: BS, HT, LF/VT/FF, CR, ESC. BEL swallowed.
- ESC D / ESC M (IND / RI), ESC c (RIS)
- CSI cursor motion: CUU/CUD/CUF/CUB (A/B/C/D), CUP/HVP (H/f), CHA (G), VPA (d)
- CSI erase: ED (J), EL (K)
- CSI scroll: SU (S), SD (T), DECSTBM (r)
- CSI SGR (m): reset, bold/dim/italic/underline/inverse, 30–37/39, 40–47/49,
  90–97, 100–107, 38;5;n / 48;5;n (indexed 256), 38;2;r;g;b / 48;2;r;g;b (24-bit)
- DECSET/DECRST (?h/?l): ?25 (cursor visibility), ?1049 (alt screen — degraded
  to "clear the current buffer", no separate alt buffer)
- OSC / DCS / SOS / PM / APC: swallowed (terminated by BEL or ST)

**Deliberately stubbed or skipped:**
- **No scrollback.** When output hits the bottom it scrolls off the top of
  the visible buffer, gone.
- **No selection / copy / mouse.** Clicking grabs focus only.
- **No separate alt-screen buffer.** `?1049h` just clears; full-screen TUIs
  (vim, less) will mostly work visually, but they'll scribble over the
  primary buffer instead of saving/restoring it.
- **No bracketed paste** (`?2004`). Multiline paste will be interpreted
  character-by-character; if that runs into a shell's line editor it can
  produce surprises.
- **No hyperlink / OSC 8** — OSC strings are swallowed.
- **No application-cursor-keys mode** (`?1`). Arrow keys always emit
  `ESC[A/B/C/D`, never `ESCOA/B/C/D`. Fine for most shells; breaks readline
  rebinding in a few TUIs.
- **No DSR / device-attribute responses.** Apps that query the terminal
  (e.g. `ESC[6n` for cursor position) will time out waiting.
- **No insert/delete line or character** (CSI L/M/@/P).
- **No charset switching** (ESC ( etc.) — consumed and ignored; we stay UTF-8.
- **No blink, no strikethrough** rendering (attrs parsed but not drawn).
- **No DECSC / DECRC** (cursor save / restore).

## Rendering approach

- Custom `Control` subclass, `Render(DrawingContext)` override.
- Cell metrics from `GlyphTypeface.Metrics` (fixed-width font).
- For each row: walk cells, group runs with identical `(fg, bg, attrs)`,
  fill the run's background rect, build one `GlyphRun` for the run's glyphs
  and `DrawGlyphRun` in the foreground color. One `GlyphRun` per style-run
  per row means the worst-case draw call count is bounded by the number of
  style switches on a line (typically 1–5), not by cell count.
- PTY read loop runs on a background `Task` and pushes chunks onto an
  unbounded `Channel<byte[]>`. The control owns a `DispatcherTimer` at 8 ms
  (~120 Hz) that drains the channel on the UI thread, feeds the parser, and
  calls `InvalidateVisual` at most once per tick. This coalesces bursts so
  `yes` doesn't cause 10 000 invalidations per second.

## Next steps for a real terminal

In rough priority order:

1. **Scrollback buffer** — ring buffer of past lines, scroll wheel / PageUp,
   clip visible viewport to a window over the ring.
2. **Separate alt-screen buffer** for `?1049h/l` so full-screen TUIs
   (vim, less, htop) don't corrupt the primary buffer.
3. **Selection + clipboard** — track pointer drag → cell range, render
   inverted fill over selected cells, `Cmd/Ctrl+C` to copy plain text
   (strip styles).
4. **Bracketed paste** (`?2004`) — emit `ESC[200~ … ESC[201~` around
   `TextInput` events when the mode is on.
5. **Mouse reporting** (`?1000 / ?1002 / ?1006`) — translate pointer events
   into SGR-encoded mouse reports.
6. **Application cursor keys** (`?1`) so readline-based shells' history
   navigation behaves.
7. **DSR / DA responses** so apps querying the terminal don't stall.
8. **OSC 8 hyperlinks** — parse, draw underlined, handle click to open.
9. **Insert/delete line & character** (CSI L/M/@/P) — required by some TUIs.
10. **Glyph-run caching per (text, style)** or per-row bitmap caching once
    profiling shows per-frame `GlyphRun` construction is the bottleneck.
11. **Wide characters / grapheme clusters / emoji** — currently each
    codepoint occupies one cell; CJK and most emoji will mis-align.
12. **Ligature handling** — disable for the terminal by default; most
    terminal users don't want them.
13. **IME composition preview** — render in-progress composition in the cell
    area before commit.

## Known bugs / rough edges to look at first

- `Focus()` is called from `OnAttachedToVisualTree`; depending on layout
  timing it may not grab focus on first show. `OnPointerPressed` also calls
  `Focus()`, so clicking once fixes it. Watch for this.
- On first window show the `ArrangeOverride` might resize the buffer *after*
  the PTY has spawned with 80×24. We call `PtySession.Resize` on any delta
  so the shell should catch up, but if the first prompt lands before the
  resize it may render at the wrong width.
- `?1049` alt screen is faked (just clears the current buffer). Exiting vim
  will not restore what was below.
