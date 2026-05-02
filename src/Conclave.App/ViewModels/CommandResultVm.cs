using Conclave.App.Commands;

namespace Conclave.App.ViewModels;

// One row in the command palette. Wraps a static AppCommand or a synthetic action
// (e.g. "Switch to <session>"). The palette VM materialises these on every query
// change, so it's a plain immutable record — no Observable plumbing needed.
public sealed record CommandResultVm(
    string Title,
    string? Subtitle,
    string? Shortcut,
    Action Execute,
    int Score);
