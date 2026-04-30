using Avalonia.Media;

namespace Conclave.App.Design;

// oklch → sRGB converter. Used because the design spec uses oklch(L C h) for the
// accent and semantic colors and Avalonia has no native oklch support.
// Implementation from Björn Ottosson's original oklab math plus the standard sRGB gamma curve.
public static class OklchConverter
{
    public static Color Oklch(double l, double c, double hDeg, double alpha = 1.0)
    {
        var rad = hDeg * Math.PI / 180.0;
        var a = c * Math.Cos(rad);
        var b = c * Math.Sin(rad);
        return OklabToColor(l, a, b, alpha);
    }

    public static Color OklabToColor(double L, double a, double b, double alpha)
    {
        // oklab → LMS (linear cone responses)
        var l_ = L + 0.3963377774 * a + 0.2158037573 * b;
        var m_ = L - 0.1055613458 * a - 0.0638541728 * b;
        var s_ = L - 0.0894841775 * a - 1.2914855480 * b;

        var lCube = l_ * l_ * l_;
        var mCube = m_ * m_ * m_;
        var sCube = s_ * s_ * s_;

        // LMS → linear sRGB
        var rLin = +4.0767416621 * lCube - 3.3077115913 * mCube + 0.2309699292 * sCube;
        var gLin = -1.2684380046 * lCube + 2.6097574011 * mCube - 0.3413193965 * sCube;
        var bLin = -0.0041960863 * lCube - 0.7034186147 * mCube + 1.7076147010 * sCube;

        return Color.FromArgb(
            (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255),
            ToSrgbByte(rLin),
            ToSrgbByte(gLin),
            ToSrgbByte(bLin));
    }

    private static byte ToSrgbByte(double linear)
    {
        var v = Math.Clamp(linear, 0.0, 1.0);
        var s = v <= 0.0031308
            ? 12.92 * v
            : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055;
        return (byte)Math.Round(Math.Clamp(s, 0.0, 1.0) * 255);
    }
}
