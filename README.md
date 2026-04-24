# Conclave — terminal spike

A minimal Avalonia 12 app with a custom `TerminalControl` that hosts a child
process through a PTY, parses its VT output, and renders a terminal grid.
This is the de-risking spike for the embedded terminal, not the full app.

## Requirements

- .NET 10 SDK (the csproj targets `net10.0`)
- macOS, Linux, or Windows
- Optionally, the `claude` CLI on `PATH` — if absent, the app falls back to
  your `$SHELL` on Unix or `pwsh`/`cmd` on Windows.

## Build and run

```sh
dotnet restore
dotnet run --project src/Conclave.App
```

A window opens hosting one `TerminalControl`. Click it to take focus,
then type. `ls`, `echo`, paging, colors, and basic TUI apps should work.

## Layout

```
src/Conclave.App/
  Program.cs, App.axaml(.cs)        Avalonia entry
  MainWindow.axaml(.cs)              Hosts a single TerminalControl
  Terminal/
    TerminalCell.cs                  Packed cell struct (codepoint + fg/bg/attrs)
    Palette.cs                       ANSI 16 + xterm 256 color table
    TerminalBuffer.cs                Grid, cursor, scroll region, dirty-row set
    VtParser.cs                      Hand-rolled VT state machine
    PtySession.cs                    Porta.Pty wrapper + read-loop channel
    GlyphCache.cs                    Codepoint→glyph index cache + cell metrics
    TerminalControl.cs               Custom Control — rendering and input
```

## Dependencies

- `Avalonia` 12.0.1 — UI
- `Porta.Pty` 1.0.7 — cross-platform pseudo-terminal (ConPTY + POSIX)

See `NOTES.md` for VT feature coverage, known limits, and next steps.
