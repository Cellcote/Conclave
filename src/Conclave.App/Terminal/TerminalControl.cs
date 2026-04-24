using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

namespace Conclave.App.Terminal;

// Custom terminal control: owns the buffer, parser, PTY session, and does its own drawing.
// Single-threaded: all buffer mutations and rendering happen on the UI thread.
// PTY read loop runs on a background Task and posts chunks onto a channel that we drain at ~120Hz.
public sealed class TerminalControl : Control
{
    private readonly Typeface _typeface = new("Menlo");
    private const double FontSize = 14;

    private TerminalBuffer _buffer = new(80, 24);
    private VtParser _parser;
    private GlyphCache _glyphCache;
    private PtySession? _session;

    // Set before the control is attached to the visual tree to pin the child process's cwd.
    public string? WorkingDirectory { get; set; }

    private DispatcherTimer? _pumpTimer;
    private bool _dirtyPending;

    // Transient buffers reused per Render frame to avoid per-frame allocations.
    private readonly StringBuilder _runChars = new(256);
    private ushort[] _runGlyphs = new ushort[256];

    public TerminalControl()
    {
        Focusable = true;
        _glyphCache = new GlyphCache(_typeface, FontSize);
        _parser = new VtParser(_buffer);
        ClipToBounds = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focus();
        _ = StartSessionAsync();

        _pumpTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Render, Pump);
        _pumpTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _pumpTimer?.Stop();
        _pumpTimer = null;
        if (_session is { } s) _ = s.DisposeAsync();
        _session = null;
    }

    private async Task StartSessionAsync()
    {
        try
        {
            _session = await PtySession.SpawnAsync(_buffer.Cols, _buffer.Rows, WorkingDirectory);
        }
        catch (Exception ex)
        {
            // Paint the error into the buffer so we don't silently fail.
            var msg = $"[conclave] failed to spawn PTY: {ex.Message}";
            foreach (var ch in msg) _buffer.Write(ch);
            _buffer.LineFeed();
            _buffer.CarriageReturn();
            InvalidateVisual();
        }
    }

    private void Pump(object? sender, EventArgs e)
    {
        if (_session is null) return;
        var reader = _session.Output;

        bool any = false;
        while (reader.TryRead(out var chunk))
        {
            _parser.Feed(chunk);
            any = true;
        }
        if (any || _dirtyPending)
        {
            _dirtyPending = false;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        ResizeForPixelBounds(result);
        return result;
    }

    private void ResizeForPixelBounds(Size pixels)
    {
        int cols = Math.Max(1, (int)(pixels.Width / _glyphCache.CellWidth));
        int rows = Math.Max(1, (int)(pixels.Height / _glyphCache.CellHeight));
        if (cols == _buffer.Cols && rows == _buffer.Rows) return;
        _buffer.Resize(cols, rows);
        _session?.Resize(cols, rows);
        _dirtyPending = true;
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        double cw = _glyphCache.CellWidth;
        double ch = _glyphCache.CellHeight;

        // Background.
        var bgBrush = new ImmutableSolidColorBrush(UintToColor(Palette.DefaultBg));
        ctx.FillRectangle(bgBrush, new Rect(Bounds.Size));

        var buf = _buffer;
        int cols = buf.Cols;
        int rows = buf.Rows;
        EnsureRunCapacity(cols);

        for (int r = 0; r < rows; r++)
        {
            double y = r * ch;
            int c = 0;
            while (c < cols)
            {
                ref var first = ref buf.CellAt(c, r);
                uint fg = ResolveFg(first);
                uint bg = ResolveBg(first);
                var attrs = first.Attrs;

                int runStart = c;
                _runChars.Clear();
                int runLen = 0;

                while (c < cols)
                {
                    ref var cell = ref buf.CellAt(c, r);
                    if (ResolveFg(cell) != fg || ResolveBg(cell) != bg || cell.Attrs != attrs)
                        break;
                    uint cp = cell.Codepoint == 0 ? (uint)' ' : cell.Codepoint;
                    if (cp <= 0xFFFF)
                    {
                        _runChars.Append((char)cp);
                        _runGlyphs[runLen++] = _glyphCache.GetGlyph(cp);
                    }
                    else
                    {
                        // Non-BMP: encode as surrogate pair and map glyph on the high codepoint only.
                        _runChars.Append(char.ConvertFromUtf32((int)cp));
                        _runGlyphs[runLen++] = _glyphCache.GetGlyph(cp);
                        _runGlyphs[runLen++] = 0;
                    }
                    c++;
                }

                double runWidth = (c - runStart) * cw;

                // Fill background for this run (skip if it matches default bg already painted above).
                if (bg != Palette.DefaultBg)
                    ctx.FillRectangle(
                        new ImmutableSolidColorBrush(UintToColor(bg)),
                        new Rect(runStart * cw, y, runWidth, ch));

                // Foreground glyphs.
                if (_runChars.Length > 0)
                {
                    var chars = _runChars.ToString().AsMemory();
                    var origin = new Point(runStart * cw, y + _glyphCache.Baseline);
                    using var run = _glyphCache.BuildRun(chars, _runGlyphs, runLen, origin);
                    var fgBrush = new ImmutableSolidColorBrush(UintToColor(fg));
                    ctx.DrawGlyphRun(fgBrush, run);
                }
            }
        }

        // Cursor overlay.
        if (buf.CursorVisible && IsFocused)
        {
            double x = buf.CursorCol * cw;
            double y = buf.CursorRow * ch;
            var cursorBrush = new ImmutableSolidColorBrush(UintToColor(Palette.DefaultFg), 0.5);
            ctx.FillRectangle(cursorBrush, new Rect(x, y, cw, ch));
        }

        buf.ClearDirty();
    }

    private void EnsureRunCapacity(int cols)
    {
        // Surrogate-pair padding: worst case two entries per cell.
        if (_runGlyphs.Length < cols * 2) _runGlyphs = new ushort[cols * 2];
    }

    private static uint ResolveFg(in TerminalCell cell)
        => (cell.Attrs & CellAttrs.Inverse) != 0 ? cell.Bg : cell.Fg;

    private static uint ResolveBg(in TerminalCell cell)
        => (cell.Attrs & CellAttrs.Inverse) != 0 ? cell.Fg : cell.Bg;

    private static Color UintToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    // ---------------- Input ----------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_session is null || string.IsNullOrEmpty(e.Text)) return;
        var bytes = Encoding.UTF8.GetBytes(e.Text);
        _session.Write(bytes);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_session is null) return;

        // Translate control keys and named keys; let TextInput handle typed characters.
        byte[]? seq = e.Key switch
        {
            Key.Enter => "\r"u8.ToArray(),
            Key.Back => "\x7F"u8.ToArray(),
            Key.Tab => "\t"u8.ToArray(),
            Key.Escape => "\x1B"u8.ToArray(),
            Key.Up => "\x1B[A"u8.ToArray(),
            Key.Down => "\x1B[B"u8.ToArray(),
            Key.Right => "\x1B[C"u8.ToArray(),
            Key.Left => "\x1B[D"u8.ToArray(),
            Key.Home => "\x1B[H"u8.ToArray(),
            Key.End => "\x1B[F"u8.ToArray(),
            Key.PageUp => "\x1B[5~"u8.ToArray(),
            Key.PageDown => "\x1B[6~"u8.ToArray(),
            Key.Delete => "\x1B[3~"u8.ToArray(),
            _ => null,
        };

        if (seq is null
            && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && e.Key >= Key.A && e.Key <= Key.Z)
        {
            seq = new[] { (byte)(e.Key - Key.A + 1) };
        }

        if (seq is not null)
        {
            _session.Write(seq);
            e.Handled = true;
        }
    }
}
