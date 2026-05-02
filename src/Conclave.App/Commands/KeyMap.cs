using System.Text.Json;

namespace Conclave.App.Commands;

// Maps key chords to command ids. Defaults are baked in code; user overrides come from
// the settings table as JSON of the form `[ { "key": "cmd+k", "command": "palette.open" } ]`.
// Overrides are merged on top of defaults — a user can rebind without losing the rest.
public sealed class KeyMap
{
    private readonly Dictionary<KeyChord, string> _bindings = new();

    public IReadOnlyDictionary<KeyChord, string> Bindings => _bindings;

    public void Bind(string chord, string commandId)
    {
        if (KeyChord.Parse(chord) is { } parsed) _bindings[parsed] = commandId;
    }

    public void Bind(KeyChord chord, string commandId) => _bindings[chord] = commandId;

    public string? Lookup(KeyChord chord) => _bindings.GetValueOrDefault(chord);

    // First chord bound to the given command id, for display purposes. Linear scan —
    // we never have enough bindings for a reverse index to be worth the maintenance.
    public KeyChord? FindForCommand(string commandId)
    {
        foreach (var (chord, id) in _bindings)
            if (id == commandId) return chord;
        return null;
    }

    // Defaults — kept minimal until we agree on the catalog. Cmd-K is the only one
    // we're confident in shipping right now.
    public static KeyMap Defaults()
    {
        var map = new KeyMap();
        map.Bind("cmd+k", "palette.open");
        return map;
    }

    // Manual JsonDocument walk rather than JsonSerializer.Deserialize so we don't pull
    // in reflection-based deserialization — the project publishes with NativeAOT and the
    // schema is trivial enough that hand-rolling is cleaner than wiring up a source-gen
    // JsonSerializerContext.
    public void ApplyOverridesJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (!entry.TryGetProperty("key", out var keyEl) || keyEl.ValueKind != JsonValueKind.String) continue;
                if (!entry.TryGetProperty("command", out var cmdEl) || cmdEl.ValueKind != JsonValueKind.String) continue;
                var keyStr = keyEl.GetString();
                var cmdStr = cmdEl.GetString();
                if (string.IsNullOrWhiteSpace(keyStr) || string.IsNullOrWhiteSpace(cmdStr)) continue;
                if (KeyChord.Parse(keyStr) is { } chord) _bindings[chord] = cmdStr;
            }
        }
        catch (JsonException)
        {
            // Bad JSON — keep defaults. Users get told via a settings UI later; for now
            // we just don't apply broken overrides.
        }
    }
}
