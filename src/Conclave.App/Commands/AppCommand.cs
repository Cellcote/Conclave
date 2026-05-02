namespace Conclave.App.Commands;

// One thing the user can invoke from the palette (or, later, a hotkey). `CanExecute`
// gates visibility/availability — e.g. "Cancel turn" only surfaces when a session has
// an in-flight CTS. Named `AppCommand` to avoid colliding with the BCL's
// System.Windows.Input.ICommand-style "Command" type some Avalonia code picks up.
public sealed record AppCommand(
    string Id,
    string Title,
    string? Group,
    Func<bool> CanExecute,
    Action Execute);
