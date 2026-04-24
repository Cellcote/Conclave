namespace Conclave.App.Terminal;

[System.Flags]
public enum CellAttrs : byte
{
    None = 0,
    Bold = 1 << 0,
    Dim = 1 << 1,
    Italic = 1 << 2,
    Underline = 1 << 3,
    Inverse = 1 << 4,
}

public struct TerminalCell
{
    public uint Codepoint;
    public uint Fg;
    public uint Bg;
    public CellAttrs Attrs;

    public readonly bool HasSameStyle(in TerminalCell other) =>
        Fg == other.Fg && Bg == other.Bg && Attrs == other.Attrs;
}
