using System.Collections;

namespace Conclave.App.Terminal;

// Grid of cells, cursor, scroll region, dirty-row tracking.
// Single-threaded — mutated only by VtParser, read only by TerminalControl.Render, both on UI thread.
public sealed class TerminalBuffer
{
    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public int CursorCol { get; private set; }
    public int CursorRow { get; private set; }
    public bool CursorVisible { get; set; } = true;

    public int ScrollTop { get; private set; }
    public int ScrollBottom { get; private set; }

    public uint CurrentFg = Palette.DefaultFg;
    public uint CurrentBg = Palette.DefaultBg;
    public CellAttrs CurrentAttrs = CellAttrs.None;

    private TerminalCell[] _cells = Array.Empty<TerminalCell>();
    private BitArray _dirtyRows = new(0);

    public TerminalBuffer(int cols, int rows) => Resize(cols, rows);

    public ref TerminalCell CellAt(int col, int row) => ref _cells[row * Cols + col];
    public BitArray DirtyRows => _dirtyRows;

    public void MarkAllDirty() => _dirtyRows.SetAll(true);
    public void ClearDirty() => _dirtyRows.SetAll(false);
    public void MarkDirty(int row)
    {
        if ((uint)row < (uint)Rows) _dirtyRows[row] = true;
    }

    public void Resize(int cols, int rows)
    {
        if (cols < 1) cols = 1;
        if (rows < 1) rows = 1;
        var newCells = new TerminalCell[cols * rows];
        var blank = new TerminalCell { Codepoint = ' ', Fg = CurrentFg, Bg = CurrentBg };
        for (int i = 0; i < newCells.Length; i++) newCells[i] = blank;

        int copyCols = Math.Min(cols, Cols);
        int copyRows = Math.Min(rows, Rows);
        for (int r = 0; r < copyRows; r++)
            for (int c = 0; c < copyCols; c++)
                newCells[r * cols + c] = _cells[r * Cols + c];

        _cells = newCells;
        Cols = cols;
        Rows = rows;
        _dirtyRows = new BitArray(rows, true);
        CursorCol = Math.Min(CursorCol, cols - 1);
        CursorRow = Math.Min(CursorRow, rows - 1);
        ScrollTop = 0;
        ScrollBottom = rows - 1;
    }

    public void SetScrollRegion(int top, int bottom)
    {
        ScrollTop = Math.Clamp(top, 0, Rows - 1);
        ScrollBottom = Math.Clamp(bottom, ScrollTop, Rows - 1);
    }

    public void SetCursor(int col, int row)
    {
        CursorCol = Math.Clamp(col, 0, Cols - 1);
        CursorRow = Math.Clamp(row, 0, Rows - 1);
    }

    public void MoveCursor(int dCol, int dRow) => SetCursor(CursorCol + dCol, CursorRow + dRow);

    public void Write(uint codepoint)
    {
        if (CursorCol >= Cols)
        {
            CursorCol = 0;
            LineFeed();
        }
        ref var cell = ref CellAt(CursorCol, CursorRow);
        cell.Codepoint = codepoint;
        cell.Fg = CurrentFg;
        cell.Bg = CurrentBg;
        cell.Attrs = CurrentAttrs;
        _dirtyRows[CursorRow] = true;
        CursorCol++;
    }

    public void Backspace()
    {
        if (CursorCol > 0) CursorCol--;
    }

    public void CarriageReturn() => CursorCol = 0;

    public void LineFeed()
    {
        if (CursorRow == ScrollBottom) ScrollUp(1);
        else if (CursorRow < Rows - 1) CursorRow++;
    }

    public void Tab()
    {
        int next = (CursorCol / 8 + 1) * 8;
        CursorCol = Math.Min(next, Cols - 1);
    }

    public void ScrollUp(int n)
    {
        n = Math.Min(n, ScrollBottom - ScrollTop + 1);
        int width = Cols;
        for (int r = ScrollTop; r <= ScrollBottom - n; r++)
        {
            Array.Copy(_cells, (r + n) * width, _cells, r * width, width);
            _dirtyRows[r] = true;
        }
        var blank = new TerminalCell { Codepoint = ' ', Fg = CurrentFg, Bg = CurrentBg };
        for (int r = ScrollBottom - n + 1; r <= ScrollBottom; r++)
        {
            for (int c = 0; c < width; c++) _cells[r * width + c] = blank;
            _dirtyRows[r] = true;
        }
    }

    public void ScrollDown(int n)
    {
        n = Math.Min(n, ScrollBottom - ScrollTop + 1);
        int width = Cols;
        for (int r = ScrollBottom; r >= ScrollTop + n; r--)
        {
            Array.Copy(_cells, (r - n) * width, _cells, r * width, width);
            _dirtyRows[r] = true;
        }
        var blank = new TerminalCell { Codepoint = ' ', Fg = CurrentFg, Bg = CurrentBg };
        for (int r = ScrollTop; r < ScrollTop + n; r++)
        {
            for (int c = 0; c < width; c++) _cells[r * width + c] = blank;
            _dirtyRows[r] = true;
        }
    }

    // ED (Erase in Display): 0=cursor→end, 1=start→cursor, 2=entire screen.
    public void EraseInDisplay(int mode)
    {
        var blank = new TerminalCell { Codepoint = ' ', Fg = CurrentFg, Bg = CurrentBg };
        int start, end;
        switch (mode)
        {
            case 0: start = CursorRow * Cols + CursorCol; end = Cols * Rows; break;
            case 1: start = 0; end = CursorRow * Cols + CursorCol + 1; break;
            default: start = 0; end = Cols * Rows; break;
        }
        for (int i = start; i < end; i++) _cells[i] = blank;
        MarkAllDirty();
    }

    // EL (Erase in Line): 0=cursor→end, 1=start→cursor, 2=entire line.
    public void EraseInLine(int mode)
    {
        var blank = new TerminalCell { Codepoint = ' ', Fg = CurrentFg, Bg = CurrentBg };
        int rowStart = CursorRow * Cols;
        int from, to;
        switch (mode)
        {
            case 0: from = CursorCol; to = Cols; break;
            case 1: from = 0; to = CursorCol + 1; break;
            default: from = 0; to = Cols; break;
        }
        for (int c = from; c < to; c++) _cells[rowStart + c] = blank;
        _dirtyRows[CursorRow] = true;
    }
}
