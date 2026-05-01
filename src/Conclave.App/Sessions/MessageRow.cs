namespace Conclave.App.Sessions;

// One persisted transcript entry. `tools_json` is an optional JSON-encoded array of tool
// calls ({ kind, target, meta, status }). `seq` is the monotonic insertion order inside
// a session and is what ORDER BY uses on load.
public sealed record MessageRow
{
    public string Id { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string Role { get; init; } = "";       // "User" | "Assistant"
    public string Content { get; init; } = "";
    public string? ToolsJson { get; init; }
    public long CreatedAt { get; init; }
    public int Seq { get; init; }
    // Claude's per-message UUID from the AssistantEvent stream. Null for our locally-minted
    // user messages and for messages persisted before this column was added. Captured to
    // enable future fork-at-message paths that target claude's own JSONL session storage.
    public string? ClaudeUuid { get; init; }
    // True for synthetic "continue" prompts injected by StallDetectionService when
    // auto-resuming a stalled session. Used to hide the user bubble in the transcript.
    public bool IsAutoResume { get; init; }
}
