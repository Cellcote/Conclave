using Avalonia;
using Avalonia.Media;

namespace Conclave.App.Terminal;

// Monospace-font helpers for Avalonia 12: measure cell, map codepoint → glyph index,
// build GlyphRuns using per-typeface default advances.
public sealed class GlyphCache
{
    private readonly GlyphTypeface _glyphTypeface;
    private readonly double _fontSize;
    private readonly double _scale;
    private readonly Dictionary<uint, ushort> _glyphByCodepoint = new(256);

    public double CellWidth { get; }
    public double CellHeight { get; }
    public double Baseline { get; }

    public GlyphCache(Typeface typeface, double fontSize)
    {
        _glyphTypeface = typeface.GlyphTypeface;
        _fontSize = fontSize;

        var m = _glyphTypeface.Metrics;
        _scale = fontSize / m.DesignEmHeight;

        // Cell width from 'M' (monospace assumption).
        ushort glyphM = GetGlyph('M');
        _glyphTypeface.TryGetHorizontalGlyphAdvance(glyphM, out ushort advance);
        CellWidth = advance * _scale;

        // Avalonia's Ascent is negative (up from baseline); Descent is positive (down).
        CellHeight = (-m.Ascent + m.Descent + m.LineGap) * _scale;
        Baseline = -m.Ascent * _scale;
    }

    public ushort GetGlyph(uint codepoint)
    {
        if (_glyphByCodepoint.TryGetValue(codepoint, out var g)) return g;
        var map = _glyphTypeface.CharacterToGlyphMap;
        g = map.TryGetGlyph((int)codepoint, out var glyph) ? glyph : (ushort)0;
        _glyphByCodepoint[codepoint] = g;
        return g;
    }

    public GlyphRun BuildRun(ReadOnlyMemory<char> chars, ushort[] glyphs, int glyphCount, Point baselineOrigin)
    {
        var slice = new ArraySegment<ushort>(glyphs, 0, glyphCount);
        return new GlyphRun(_glyphTypeface, _fontSize, chars, slice, baselineOrigin, 0);
    }
}
