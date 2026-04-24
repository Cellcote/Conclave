using Avalonia.Media;
using Conclave.App.Design;

namespace Conclave.App.ViewModels;

public enum ToolStatus { Ok, Fail, Pending }

// One tool invocation rendered as a compact pill in the transcript.
// e.g. READ  NOTES.md                                    ✓ 142 lines
public sealed class ToolCallVm : Views.Observable
{
    public Tokens Tokens { get; init; } = null!;
    public string Kind { get; init; } = "";      // READ / BASH / WRITE / EDIT / …
    public string Target { get; init; } = "";

    private string _meta = "";
    public string Meta { get => _meta; set => Set(ref _meta, value); }

    private ToolStatus _status = ToolStatus.Pending;
    public ToolStatus Status
    {
        get => _status;
        set { if (Set(ref _status, value)) { Notify(nameof(StatusGlyph)); Notify(nameof(StatusBrush)); } }
    }

    public string StatusGlyph => _status switch
    {
        ToolStatus.Ok => "✓",
        ToolStatus.Fail => "✕",
        ToolStatus.Pending => "…",
        _ => "",
    };

    public IBrush StatusBrush => _status switch
    {
        ToolStatus.Ok => Tokens.Ok,
        ToolStatus.Fail => Tokens.Err,
        ToolStatus.Pending => Tokens.Warn,
        _ => Tokens.TextMute,
    };
}
