namespace Conclave.App.Commands;

// Owns the catalog of static commands (palette.open, prefs.open, ...). Dynamic
// per-session commands ("Switch to <name>") are produced on demand by the palette
// VM and not registered here — they'd churn every time a session is added/removed.
public sealed class CommandRegistry
{
    private readonly Dictionary<string, AppCommand> _byId = new();

    public IReadOnlyCollection<AppCommand> All => _byId.Values;

    public void Register(AppCommand cmd) => _byId[cmd.Id] = cmd;

    public AppCommand? Get(string id) => _byId.GetValueOrDefault(id);

    public bool TryExecute(string id)
    {
        if (!_byId.TryGetValue(id, out var cmd)) return false;
        if (!cmd.CanExecute()) return false;
        cmd.Execute();
        return true;
    }
}
