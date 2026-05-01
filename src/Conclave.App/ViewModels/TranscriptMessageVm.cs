using System.Collections.ObjectModel;
using Conclave.App.Design;

namespace Conclave.App.ViewModels;

public enum MessageRole { User, Assistant }

public sealed class TranscriptMessageVm : Views.Observable
{
    public string Id { get; init; } = "";  // UUID — matches the `id` column in the messages table.
    public Tokens Tokens { get; init; } = null!;
    public MessageRole Role { get; init; }
    // Claude's per-message UUID from AssistantEvent. Null for user messages and for messages
    // persisted before this column existed. Lets future fork-at-message paths target the
    // CLI's JSONL session storage; not used directly by path A's synthetic-context fork.
    public string? ClaudeUuid { get; set; }

    // True for synthetic "continue" prompts injected by StallDetectionService when
    // auto-resuming a stalled session. The user bubble is hidden in the transcript view
    // and replaced with a small "— resumed after wake —" separator. Always false on
    // assistant messages.
    public bool IsAutoResume { get; init; }

    public ObservableCollection<ToolCallVm> Tools { get; } = new();

    private string _time = "";
    public string Time
    {
        get => _time;
        set { if (Set(ref _time, value)) Notify(nameof(HeaderText)); }
    }

    private string _content = "";
    public string Content
    {
        get => _content;
        set
        {
            if (Set(ref _content, value))
            {
                Notify(nameof(HasContent));
                Notify(nameof(ShouldShowContent));
            }
        }
    }

    public bool HasContent => !string.IsNullOrEmpty(_content);

    // SessionVm.AppendTranscript hides this when the previous message has the same role —
    // keeps consecutive assistant chunks (text → tool → more text) looking like one reply.
    private bool _showHeader = true;
    public bool ShowHeader
    {
        get => _showHeader;
        set
        {
            if (Set(ref _showHeader, value))
            {
                Notify(nameof(TopSpacing));
                Notify(nameof(ContentTopMargin));
                Notify(nameof(ShouldShowHeader));
            }
        }
    }

    // 24px between messages with a header, tighter when the header is hidden so tool-call
    // chains read as one block.
    public double TopSpacing => _showHeader ? 22 : 6;

    // Vertical gap between the header and the body (0 when there's no header).
    public Avalonia.Thickness ContentTopMargin => _showHeader ? new Avalonia.Thickness(0) : default;

    public bool IsUser => Role == MessageRole.User;
    public bool IsAssistant => Role == MessageRole.Assistant;
    public string LabelPrefix => Role == MessageRole.User ? "You" : "Claude";
    public string HeaderText => $"{LabelPrefix} · {_time}";

    // The transcript template only shows the header/body when the row is a regular message;
    // auto-resume rows render as a thin "resumed after wake" separator and nothing else.
    // Computed in the VM rather than via a XAML MultiBinding because compiled bindings on
    // bool conjunctions are awkward to express cleanly across Avalonia versions.
    public bool ShouldShowHeader => _showHeader && !IsAutoResume;
    public bool ShouldShowContent => HasContent && !IsAutoResume;
}
