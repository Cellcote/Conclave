namespace Conclave.App.Terminal;

// 16 ANSI + 240 xterm extended = 256 indexed colors, packed as 0xAARRGGBB (opaque).
public static class Palette
{
    public const uint DefaultFg = 0xFFCCCCCC;
    public const uint DefaultBg = 0xFF111111;

    public static readonly uint[] Ansi16 =
    {
        0xFF000000, 0xFFCC0000, 0xFF00CC00, 0xFFCCCC00,
        0xFF0066CC, 0xFFCC00CC, 0xFF00CCCC, 0xFFCCCCCC,
        0xFF555555, 0xFFFF5555, 0xFF55FF55, 0xFFFFFF55,
        0xFF5555FF, 0xFFFF55FF, 0xFF55FFFF, 0xFFFFFFFF,
    };

    private static readonly uint[] Indexed256 = BuildIndexed256();

    public static uint Indexed(int i)
    {
        if ((uint)i >= 256) return DefaultFg;
        return Indexed256[i];
    }

    public static uint Rgb(byte r, byte g, byte b) =>
        0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;

    private static uint[] BuildIndexed256()
    {
        var p = new uint[256];
        for (int i = 0; i < 16; i++) p[i] = Ansi16[i];
        // 6×6×6 cube at indices 16..231
        ReadOnlySpan<byte> steps = stackalloc byte[] { 0, 95, 135, 175, 215, 255 };
        for (int r = 0; r < 6; r++)
            for (int g = 0; g < 6; g++)
                for (int b = 0; b < 6; b++)
                    p[16 + r * 36 + g * 6 + b] = Rgb(steps[r], steps[g], steps[b]);
        // Grayscale ramp at 232..255
        for (int i = 0; i < 24; i++)
        {
            byte v = (byte)(8 + i * 10);
            p[232 + i] = Rgb(v, v, v);
        }
        return p;
    }
}
