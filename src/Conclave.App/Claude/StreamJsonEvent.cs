namespace Conclave.App.Claude;

// Typed parsed events from `claude -p --output-format=stream-json --verbose`.
// Any shape we don't recognise becomes UnknownEvent so the caller can log it rather than crash.
public abstract record StreamJsonEvent
{
    public string Type { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string? Uuid { get; init; }
}

// First event in any run: contains the claude-side session UUID we pass to --resume later.
public sealed record SystemInitEvent : StreamJsonEvent
{
}

// An assistant turn (may be followed by more if claude did tool calls and then more thinking).
public sealed record AssistantEvent : StreamJsonEvent
{
    public string MessageId { get; init; } = "";
    public IReadOnlyList<ContentBlock> Content { get; init; } = Array.Empty<ContentBlock>();
    public string? StopReason { get; init; }
}

// A "user" event in stream-json carries tool results (the user/environment "replies" with
// the tool output back to the model).
public sealed record UserEvent : StreamJsonEvent
{
    public IReadOnlyList<ContentBlock> Content { get; init; } = Array.Empty<ContentBlock>();
}

// Emitted once at the end of the turn. is_error=true indicates a failure.
public sealed record ResultEvent : StreamJsonEvent
{
    public bool IsError { get; init; }
    public string? Subtype { get; init; }
    public string? Result { get; init; }
    public long DurationMs { get; init; }
    public string? StopReason { get; init; }
    public double? TotalCostUsd { get; init; }
}

// Rate-limit info, skipped for transcript but retained if we ever want to surface it.
public sealed record RateLimitEvent : StreamJsonEvent { }

// Partial-message delta. Emitted when the CLI is run with `--include-partial-messages`.
// Shape (inside `event`): message_start / content_block_start / content_block_delta /
// content_block_stop / message_delta / message_stop.
public sealed record StreamDeltaEvent : StreamJsonEvent
{
    // Subtype from the nested `event.type` field ("content_block_delta" etc.).
    public string EventType { get; init; } = "";
    // Present on message_start (nested event.message.id).
    public string? MessageId { get; init; }
    // Content block index (content_block_* events).
    public int? BlockIndex { get; init; }
    // event.content_block.type on content_block_start — "text" or "tool_use".
    public string? BlockType { get; init; }
    // event.delta.type on content_block_delta — "text_delta", "input_json_delta".
    public string? DeltaType { get; init; }
    // Text delta payload (event.delta.text).
    public string? DeltaText { get; init; }
}

// Known-but-informational event we don't act on yet. Covers:
//   - system subtypes other than init (compact_boundary, hook_started/progress/response, status, …)
//   - top-level types: auth_status, tool_progress, tool_use_summary
// IMPORTANT: for `system/hook_*` events the session_id is NOT durable — don't update
// our stored ClaudeSessionId from these. ClaudeService handles this by only reading
// session_id from SystemInitEvent.
public sealed record InformationalEvent : StreamJsonEvent
{
    public string? Subtype { get; init; }
}

// Any other event we didn't model — preserved raw in case we want to log or debug.
public sealed record UnknownEvent : StreamJsonEvent
{
    public string Raw { get; init; } = "";
}

// --- Content blocks inside assistant/user messages ---

public abstract record ContentBlock { public string Type { get; init; } = ""; }

public sealed record TextContent : ContentBlock
{
    public string Text { get; init; } = "";
}

public sealed record ToolUseContent : ContentBlock
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    // JSON object with the tool's arguments (keeps the raw shape so callers can pull out target/meta).
    public string InputJson { get; init; } = "{}";
}

public sealed record ToolResultContent : ContentBlock
{
    public string ToolUseId { get; init; } = "";
    // Flattened text representation of the tool's output. Nested content blocks from the tool
    // are concatenated into one string.
    public string Content { get; init; } = "";
    public bool IsError { get; init; }
}
