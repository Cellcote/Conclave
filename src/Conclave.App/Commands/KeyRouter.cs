using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Conclave.App.Commands;

// Window-level keyboard router. Listens on the tunnel phase so app-level chords with a
// modifier (Cmd-K, Ctrl-Shift-P) win even when a TextBox would otherwise consume the
// keystroke. Plain-key chords (no modifier) are handled on the bubble phase so typing
// in inputs still works normally.
public static class KeyRouter
{
    public static void Attach(Window window, CommandRegistry registry, KeyMap map)
    {
        window.AddHandler(InputElement.KeyDownEvent, (s, e) => OnKey(e, registry, map, tunneling: true),
            RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.KeyDownEvent, (s, e) => OnKey(e, registry, map, tunneling: false),
            RoutingStrategies.Bubble);
    }

    private static void OnKey(KeyEventArgs e, CommandRegistry registry, KeyMap map, bool tunneling)
    {
        if (e.Handled) return;

        var chord = KeyChord.FromEvent(e);
        // Modifier keys arriving on their own (e.g. holding ⌘) come through as
        // Key.LeftCommand / LWin / etc. Skip — they aren't a chord on their own.
        if (IsModifierKey(e.Key)) return;

        bool hasGlobalModifier = chord.Modifiers.HasFlag(KeyModifiers.Meta)
            || chord.Modifiers.HasFlag(KeyModifiers.Control);

        // Tunnel phase only handles modifier-bearing chords — that's the whole point of
        // grabbing them early. Plain-letter chords fall through to the bubble pass.
        if (tunneling && !hasGlobalModifier) return;
        if (!tunneling && hasGlobalModifier) return; // already considered on tunnel

        if (map.Lookup(chord) is not { } commandId) return;
        if (registry.TryExecute(commandId)) e.Handled = true;
    }

    private static bool IsModifierKey(Key k) => k
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt
        or Key.LWin or Key.RWin;
}
