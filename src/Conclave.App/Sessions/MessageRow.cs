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
}
