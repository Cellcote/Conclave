using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Conclave.App.Views;

namespace Conclave.App.Design;

public enum ThemeMode { Dark, Light }
public enum AccentHue { Orange, Cream, Cool, Green, Magenta }
public enum DensityPreset { Cozy, Normal, Dense }
public enum RadiusPreset { Sharp, Medium, Soft }

// C# port of makeTokens() from variant-b-full.jsx. Produces every brush + numeric value the
// views need to render. Expose brushes as IBrush so XAML bindings can bind to them directly.
public sealed class Tokens : Observable
{
    public ThemeMode Theme { get; }
    public AccentHue Accent { get; }
    public DensityPreset Density { get; }
    public RadiusPreset Radii { get; }

    // Colors
    public IBrush Bg { get; }
    public IBrush Panel { get; }
    public IBrush Panel2 { get; }
    public IBrush PanelHi { get; }
    public IBrush Border { get; }
    public IBrush BorderHi { get; }
    public IBrush Text { get; }
    public IBrush TextDim { get; }
    public IBrush TextMute { get; }
    public IBrush AccentBrush { get; }
    public IBrush AccentFg { get; }
    public IBrush AccentDim { get; }
    public IBrush AccentMid { get; }
    public IBrush Ok { get; }
    public IBrush Warn { get; }
    public IBrush Err { get; }
    public IBrush Info { get; }

    // Raw colors for cases that need non-brush forms (BoxShadow, inline math).
    public Color AccentColor { get; }
    public Color OkColor { get; }
    public Color WarnColor { get; }
    public Color ErrColor { get; }
    public Color BgColor { get; }
    public Color BorderColor { get; }
    public Color TextMuteColor { get; }

    // Spacing
    public double RowPadY { get; }
    public double RowPadX { get; }
    public double MainPad { get; }
    public double MainTop { get; }

    // Radius — doubles for inline math, CornerRadius structs for XAML bindings.
    public double RadXs { get; }
    public double RadSm { get; }
    public double RadMd { get; }
    public double RadLg { get; }
    public CornerRadius RadXsCorner => new(RadXs);
    public CornerRadius RadSmCorner => new(RadSm);
    public CornerRadius RadMdCorner => new(RadMd);
    public CornerRadius RadLgCorner => new(RadLg);

    public Tokens(ThemeMode theme, AccentHue accent, DensityPreset density, RadiusPreset radius)
    {
        Theme = theme;
        Accent = accent;
        Density = density;
        Radii = radius;

        var dark = theme == ThemeMode.Dark;
        var hue = accent switch
        {
            AccentHue.Orange => 32.0,
            AccentHue.Cream => 72.0,
            AccentHue.Cool => 240.0,
            AccentHue.Green => 158.0,
            AccentHue.Magenta => 320.0,
            _ => 240.0,
        };

        // Base palette
        if (dark)
        {
            BgColor = Color.Parse("#0D0E10");
            var panel = Color.Parse("#131418");
            var panel2 = Color.Parse("#181A1F");
            var panelHi = Color.Parse("#1E2026");
            BorderColor = Color.Parse("#222429");
            var borderHi = Color.Parse("#2E3139");
            var text = Color.Parse("#E8E9EC");
            var textDim = Color.Parse("#8A8D96");
            TextMuteColor = Color.Parse("#4E5058");

            AccentColor = OklchConverter.Oklch(0.72, 0.13, hue);
            var accentFg = BgColor;
            var accentDim = OklchConverter.Oklch(0.72, 0.13, hue, 0.14);
            var accentMid = OklchConverter.Oklch(0.72, 0.13, hue, 0.28);
            OkColor = OklchConverter.Oklch(0.74, 0.14, 158);
            WarnColor = OklchConverter.Oklch(0.80, 0.15, 80);
            ErrColor = OklchConverter.Oklch(0.68, 0.18, 25);
            var info = OklchConverter.Oklch(0.72, 0.12, 240);

            Bg = new ImmutableSolidColorBrush(BgColor);
            Panel = new ImmutableSolidColorBrush(panel);
            Panel2 = new ImmutableSolidColorBrush(panel2);
            PanelHi = new ImmutableSolidColorBrush(panelHi);
            Border = new ImmutableSolidColorBrush(BorderColor);
            BorderHi = new ImmutableSolidColorBrush(borderHi);
            Text = new ImmutableSolidColorBrush(text);
            TextDim = new ImmutableSolidColorBrush(textDim);
            TextMute = new ImmutableSolidColorBrush(TextMuteColor);
            AccentBrush = new ImmutableSolidColorBrush(AccentColor);
            AccentFg = new ImmutableSolidColorBrush(accentFg);
            AccentDim = new ImmutableSolidColorBrush(accentDim);
            AccentMid = new ImmutableSolidColorBrush(accentMid);
            Ok = new ImmutableSolidColorBrush(OkColor);
            Warn = new ImmutableSolidColorBrush(WarnColor);
            Err = new ImmutableSolidColorBrush(ErrColor);
            Info = new ImmutableSolidColorBrush(info);
        }
        else
        {
            BgColor = Color.Parse("#FDFDFC");
            var panel = Color.Parse("#F7F7F6");
            var panel2 = Color.Parse("#F1F1EF");
            var panelHi = Color.Parse("#ECECEA");
            BorderColor = Color.Parse("#E6E6E3");
            var borderHi = Color.Parse("#D4D4D0");
            var text = Color.Parse("#0F1012");
            var textDim = Color.Parse("#666870");
            TextMuteColor = Color.Parse("#9A9CA3");

            AccentColor = OklchConverter.Oklch(0.55, 0.14, hue);
            var accentFg = Color.Parse("#FFFFFF");
            var accentDim = OklchConverter.Oklch(0.55, 0.14, hue, 0.10);
            var accentMid = OklchConverter.Oklch(0.55, 0.14, hue, 0.22);
            OkColor = OklchConverter.Oklch(0.50, 0.14, 158);
            WarnColor = OklchConverter.Oklch(0.58, 0.15, 65);
            ErrColor = OklchConverter.Oklch(0.54, 0.18, 25);
            var info = OklchConverter.Oklch(0.55, 0.14, 240);

            Bg = new ImmutableSolidColorBrush(BgColor);
            Panel = new ImmutableSolidColorBrush(panel);
            Panel2 = new ImmutableSolidColorBrush(panel2);
            PanelHi = new ImmutableSolidColorBrush(panelHi);
            Border = new ImmutableSolidColorBrush(BorderColor);
            BorderHi = new ImmutableSolidColorBrush(borderHi);
            Text = new ImmutableSolidColorBrush(text);
            TextDim = new ImmutableSolidColorBrush(textDim);
            TextMute = new ImmutableSolidColorBrush(TextMuteColor);
            AccentBrush = new ImmutableSolidColorBrush(AccentColor);
            AccentFg = new ImmutableSolidColorBrush(accentFg);
            AccentDim = new ImmutableSolidColorBrush(accentDim);
            AccentMid = new ImmutableSolidColorBrush(accentMid);
            Ok = new ImmutableSolidColorBrush(OkColor);
            Warn = new ImmutableSolidColorBrush(WarnColor);
            Err = new ImmutableSolidColorBrush(ErrColor);
            Info = new ImmutableSolidColorBrush(info);
        }

        // Density → padding scale
        (RowPadY, RowPadX, MainPad, MainTop) = density switch
        {
            DensityPreset.Cozy => (9, 12, 32, 16),
            DensityPreset.Normal => (7, 10, 24, 12),
            DensityPreset.Dense => (5, 9, 18, 9),
            _ => (7, 10, 24, 12),
        };

        // Radius preset
        (RadXs, RadSm, RadMd, RadLg) = radius switch
        {
            RadiusPreset.Sharp => (2, 3, 4, 6),
            RadiusPreset.Medium => (3, 5, 7, 9),
            RadiusPreset.Soft => (5, 8, 11, 14),
            _ => (3, 5, 7, 9),
        };
    }

    public static Tokens DarkCoolNormalMedium() =>
        new(ThemeMode.Dark, AccentHue.Cool, DensityPreset.Normal, RadiusPreset.Medium);
}
