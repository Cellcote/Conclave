using Avalonia.Input;

namespace Conclave.App.Commands;

// A single key + modifier combo, normalised so equality works regardless of how the
// chord was entered. `cmd` parses to Meta on macOS and Control elsewhere — this is
// the same convention VS Code uses, and it's what users expect when sharing keymaps
// across machines.
public readonly record struct KeyChord(Key Key, KeyModifiers Modifiers)
{
    public static KeyChord FromEvent(KeyEventArgs e) => new(e.Key, NormaliseModifiers(e.KeyModifiers));

    // Strip Shift when the underlying key already encodes case-sensitivity (letters/digits)?
    // No — we want Shift to be significant for chords like "shift+G". Just clear stray
    // modifier bits Avalonia might emit (none today, future-proofing only).
    private static KeyModifiers NormaliseModifiers(KeyModifiers m) => m;

    // "cmd+k", "ctrl+shift+p", "esc". Returns null on parse failure rather than
    // throwing — bad keymaps shouldn't crash the app, they should just be ignored.
    public static KeyChord? Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var parts = spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        var mods = KeyModifiers.None;
        Key? key = null;
        foreach (var raw in parts)
        {
            switch (raw.ToLowerInvariant())
            {
                case "cmd":
                case "meta":
                case "win":
                    mods |= OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
                    break;
                case "ctrl":
                case "control":
                    mods |= KeyModifiers.Control;
                    break;
                case "shift":
                    mods |= KeyModifiers.Shift;
                    break;
                case "alt":
                case "option":
                    mods |= KeyModifiers.Alt;
                    break;
                default:
                    if (!TryParseKey(raw, out var parsed)) return null;
                    key = parsed;
                    break;
            }
        }
        return key is null ? null : new KeyChord(key.Value, mods);
    }

    private static bool TryParseKey(string raw, out Key key)
    {
        // Single-char shortcuts: "k", "p". Avalonia's Key enum names letters as A..Z.
        if (raw.Length == 1 && char.IsLetter(raw[0]))
            return Enum.TryParse(raw.ToUpperInvariant(), out key);

        // Common aliases users will type before the formal name.
        switch (raw.ToLowerInvariant())
        {
            case "esc": key = Key.Escape; return true;
            case "enter":
            case "return": key = Key.Enter; return true;
            case "space": key = Key.Space; return true;
        }

        return Enum.TryParse(raw, ignoreCase: true, out key);
    }

    public string Display
    {
        get
        {
            var parts = new List<string>(4);
            if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add(OperatingSystem.IsMacOS() ? "⌃" : "Ctrl");
            if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add(OperatingSystem.IsMacOS() ? "⌥" : "Alt");
            if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add(OperatingSystem.IsMacOS() ? "⇧" : "Shift");
            if (Modifiers.HasFlag(KeyModifiers.Meta)) parts.Add(OperatingSystem.IsMacOS() ? "⌘" : "Win");
            parts.Add(KeyDisplay(Key));
            return OperatingSystem.IsMacOS() ? string.Concat(parts) : string.Join("+", parts);
        }
    }

    private static string KeyDisplay(Key k) => k switch
    {
        Key.Escape => "Esc",
        Key.Enter => "↵",
        Key.Space => "Space",
        _ => k.ToString(),
    };
}
