using Avalonia.Media;
using Conclave.App.Claude;
using Conclave.App.Design;

namespace Conclave.App.ViewModels;

public enum ToolStatus { Ok, Fail, Pending, PendingApproval }

// One tool invocation rendered as a compact pill in the transcript.
// e.g. READ  NOTES.md                                    ✓ 142 lines
public sealed class ToolCallVm : Views.Observable
{
    public Tokens Tokens { get; init; } = null!;
    public string Kind { get; init; } = "";      // READ / BASH / WRITE / EDIT / …
    public string Target { get; init; } = "";

    // Set by ClaudeService when this VM is created from a tool_use block. Used by the
    // approve/deny buttons to talk back to the per-turn permission handler.
    public string ToolUseId { get; set; } = "";
    public PermissionTurnHandler? PermissionHandler { get; set; }

    private string _meta = "";
    public string Meta { get => _meta; set => Set(ref _meta, value); }

    private ToolStatus _status = ToolStatus.Pending;
    public ToolStatus Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                Notify(nameof(StatusGlyph));
                Notify(nameof(StatusBrush));
                Notify(nameof(IsAwaitingApproval));
            }
        }
    }

    public bool IsAwaitingApproval => _status == ToolStatus.PendingApproval;

    public string StatusGlyph => _status switch
    {
        ToolStatus.Ok => "✓",
        ToolStatus.Fail => "✕",
        ToolStatus.Pending => "…",
        ToolStatus.PendingApproval => "?",
        _ => "",
    };

    public IBrush StatusBrush => _status switch
    {
        ToolStatus.Ok => Tokens.Ok,
        ToolStatus.Fail => Tokens.Err,
        ToolStatus.Pending => Tokens.Warn,
        ToolStatus.PendingApproval => Tokens.Warn,
        _ => Tokens.TextMute,
    };

    public void Approve() => PermissionHandler?.Resolve(ToolUseId, PermissionDecision.Allow);
    public void Deny() => PermissionHandler?.Resolve(ToolUseId, PermissionDecision.Deny);
}
