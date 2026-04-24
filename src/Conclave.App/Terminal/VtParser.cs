namespace Conclave.App.Terminal;

// Minimal VT100/xterm-ish parser. Covers what a typical shell + claude CLI emit:
//   CSI SGR, cursor motion (CUU/CUD/CUF/CUB/CUP/HVP/CHA/VPA), erase (ED/EL),
//   scroll (SU/SD), scroll region (DECSTBM), DECSET ?25/?1049,
//   UTF-8 in Ground state. OSC and DCS strings are swallowed, not parsed.
// Deliberately ignores: charset switching (stays UTF-8), DCS, device attributes,
// mouse reporting, insert/delete line/char, tab stops.
public sealed class VtParser(TerminalBuffer buffer)
{
    private enum State
    {
        Ground,
        Escape,
        IgnoreCharset,    // one-byte skip after ESC ( ) * +
        Csi,
        String,           // OSC / DCS / SOS / PM / APC payload
        StringPendingSt,  // saw ESC inside String, awaiting '\' to finish
    }

    private readonly TerminalBuffer _buf = buffer;
    private State _state = State.Ground;

    // UTF-8 decode state.
    private uint _cp;
    private int _utf8Remaining;

    // CSI collection.
    private readonly List<int> _params = new(8);
    private int _currentParam;
    private bool _hasCurrent;
    private bool _csiPrivate;

    public void Feed(ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++) Step(bytes[i]);
    }

    private void Step(byte b)
    {
        switch (_state)
        {
            case State.Ground: Ground(b); break;
            case State.Escape: Escape(b); break;
            case State.IgnoreCharset: _state = State.Ground; break;
            case State.Csi: Csi(b); break;
            case State.String: SwallowString(b); break;
            case State.StringPendingSt:
                _state = b == (byte)'\\' ? State.Ground : State.String;
                break;
        }
    }

    private void Ground(byte b)
    {
        if (_utf8Remaining > 0)
        {
            if ((b & 0xC0) == 0x80)
            {
                _cp = (_cp << 6) | (uint)(b & 0x3F);
                _utf8Remaining--;
                if (_utf8Remaining == 0) _buf.Write(_cp);
                return;
            }
            _utf8Remaining = 0; // malformed; fall through and reinterpret this byte
        }

        if (b < 0x20)
        {
            switch (b)
            {
                case 0x08: _buf.Backspace(); break;
                case 0x09: _buf.Tab(); break;
                case 0x0A or 0x0B or 0x0C: _buf.LineFeed(); break;
                case 0x0D: _buf.CarriageReturn(); break;
                case 0x1B: _state = State.Escape; break;
            }
            return;
        }

        if (b < 0x80) { _buf.Write(b); return; }

        if ((b & 0xE0) == 0xC0) { _cp = (uint)(b & 0x1F); _utf8Remaining = 1; return; }
        if ((b & 0xF0) == 0xE0) { _cp = (uint)(b & 0x0F); _utf8Remaining = 2; return; }
        if ((b & 0xF8) == 0xF0) { _cp = (uint)(b & 0x07); _utf8Remaining = 3; return; }
        // Bare continuation or invalid leading byte: drop.
    }

    private void Escape(byte b)
    {
        switch (b)
        {
            case (byte)'[': EnterCsi(); break;
            case (byte)']' or (byte)'P' or (byte)'X' or (byte)'^' or (byte)'_':
                _state = State.String;
                break;
            case (byte)'(' or (byte)')' or (byte)'*' or (byte)'+':
                _state = State.IgnoreCharset;
                break;
            case (byte)'D': _buf.LineFeed(); _state = State.Ground; break;
            case (byte)'M':
                if (_buf.CursorRow == _buf.ScrollTop) _buf.ScrollDown(1);
                else _buf.MoveCursor(0, -1);
                _state = State.Ground;
                break;
            case (byte)'c':
                _buf.CurrentFg = Palette.DefaultFg;
                _buf.CurrentBg = Palette.DefaultBg;
                _buf.CurrentAttrs = CellAttrs.None;
                _buf.SetCursor(0, 0);
                _buf.EraseInDisplay(2);
                _state = State.Ground;
                break;
            default: _state = State.Ground; break;
        }
    }

    private void EnterCsi()
    {
        _params.Clear();
        _currentParam = 0;
        _hasCurrent = false;
        _csiPrivate = false;
        _state = State.Csi;
    }

    private void Csi(byte b)
    {
        if (b == (byte)'?' && _params.Count == 0 && !_hasCurrent)
        {
            _csiPrivate = true;
            return;
        }
        if (b is >= (byte)'0' and <= (byte)'9')
        {
            _currentParam = _currentParam * 10 + (b - (byte)'0');
            _hasCurrent = true;
            return;
        }
        if (b == (byte)';')
        {
            _params.Add(_hasCurrent ? _currentParam : 0);
            _currentParam = 0;
            _hasCurrent = false;
            return;
        }
        if (b >= 0x40 && b <= 0x7E)
        {
            if (_hasCurrent) _params.Add(_currentParam);
            DispatchCsi((char)b);
            _state = State.Ground;
            return;
        }
        // Intermediates (0x20–0x2F): ignore.
    }

    // Param with default when missing or zero.
    private int P(int idx, int dflt)
    {
        if (idx >= _params.Count) return dflt;
        return _params[idx] == 0 ? dflt : _params[idx];
    }
    // Param with explicit default (0 preserved).
    private int P0(int idx, int dflt) => idx < _params.Count ? _params[idx] : dflt;

    private void DispatchCsi(char final)
    {
        switch (final)
        {
            case 'A': _buf.MoveCursor(0, -P(0, 1)); break;
            case 'B': _buf.MoveCursor(0, P(0, 1)); break;
            case 'C': _buf.MoveCursor(P(0, 1), 0); break;
            case 'D': _buf.MoveCursor(-P(0, 1), 0); break;
            case 'H' or 'f': _buf.SetCursor(P(1, 1) - 1, P(0, 1) - 1); break;
            case 'G': _buf.SetCursor(P(0, 1) - 1, _buf.CursorRow); break;
            case 'd': _buf.SetCursor(_buf.CursorCol, P(0, 1) - 1); break;
            case 'J': _buf.EraseInDisplay(P0(0, 0)); break;
            case 'K': _buf.EraseInLine(P0(0, 0)); break;
            case 'S': _buf.ScrollUp(P(0, 1)); break;
            case 'T': _buf.ScrollDown(P(0, 1)); break;
            case 'r':
                _buf.SetScrollRegion(P(0, 1) - 1, P(1, _buf.Rows) - 1);
                _buf.SetCursor(0, 0);
                break;
            case 'm': ApplySgr(); break;
            case 'h' when _csiPrivate: ApplyPrivateMode(true); break;
            case 'l' when _csiPrivate: ApplyPrivateMode(false); break;
        }
    }

    private void ApplyPrivateMode(bool set)
    {
        foreach (var p in _params)
        {
            switch (p)
            {
                case 25: _buf.CursorVisible = set; break;
                case 1049:
                    if (set) { _buf.SetCursor(0, 0); _buf.EraseInDisplay(2); }
                    break;
            }
        }
    }

    private void ApplySgr()
    {
        if (_params.Count == 0) { ResetSgr(); return; }
        for (int i = 0; i < _params.Count; i++)
        {
            int p = _params[i];
            switch (p)
            {
                case 0: ResetSgr(); break;
                case 1: _buf.CurrentAttrs |= CellAttrs.Bold; break;
                case 2: _buf.CurrentAttrs |= CellAttrs.Dim; break;
                case 3: _buf.CurrentAttrs |= CellAttrs.Italic; break;
                case 4: _buf.CurrentAttrs |= CellAttrs.Underline; break;
                case 7: _buf.CurrentAttrs |= CellAttrs.Inverse; break;
                case 22: _buf.CurrentAttrs &= ~(CellAttrs.Bold | CellAttrs.Dim); break;
                case 23: _buf.CurrentAttrs &= ~CellAttrs.Italic; break;
                case 24: _buf.CurrentAttrs &= ~CellAttrs.Underline; break;
                case 27: _buf.CurrentAttrs &= ~CellAttrs.Inverse; break;
                case >= 30 and <= 37: _buf.CurrentFg = Palette.Ansi16[p - 30]; break;
                case 39: _buf.CurrentFg = Palette.DefaultFg; break;
                case >= 40 and <= 47: _buf.CurrentBg = Palette.Ansi16[p - 40]; break;
                case 49: _buf.CurrentBg = Palette.DefaultBg; break;
                case >= 90 and <= 97: _buf.CurrentFg = Palette.Ansi16[p - 90 + 8]; break;
                case >= 100 and <= 107: _buf.CurrentBg = Palette.Ansi16[p - 100 + 8]; break;
                case 38: i = ReadExtendedColor(i, fg: true); break;
                case 48: i = ReadExtendedColor(i, fg: false); break;
            }
        }
    }

    private int ReadExtendedColor(int i, bool fg)
    {
        if (i + 1 >= _params.Count) return _params.Count;
        int kind = _params[i + 1];
        if (kind == 5 && i + 2 < _params.Count)
        {
            uint c = Palette.Indexed(_params[i + 2]);
            if (fg) _buf.CurrentFg = c; else _buf.CurrentBg = c;
            return i + 2;
        }
        if (kind == 2 && i + 4 < _params.Count)
        {
            uint c = Palette.Rgb((byte)_params[i + 2], (byte)_params[i + 3], (byte)_params[i + 4]);
            if (fg) _buf.CurrentFg = c; else _buf.CurrentBg = c;
            return i + 4;
        }
        return i + 1;
    }

    private void ResetSgr()
    {
        _buf.CurrentFg = Palette.DefaultFg;
        _buf.CurrentBg = Palette.DefaultBg;
        _buf.CurrentAttrs = CellAttrs.None;
    }

    private void SwallowString(byte b)
    {
        if (b == 0x07) { _state = State.Ground; return; }
        if (b == 0x1B) { _state = State.StringPendingSt; return; }
    }
}
