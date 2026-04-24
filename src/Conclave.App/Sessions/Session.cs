namespace Conclave.App.Sessions;

// Parameterless record + init-only properties so Dapper hydrates by column name
// (MatchNamesWithUnderscores). Avoids the strict ctor-signature matching Dapper does
// when a positional record is used.
public sealed record Session
{
    public string Id { get; init; } = "";
    public string ProjectId { get; init; } = "";
    public string Name { get; init; } = "";
    public string BranchName { get; init; } = "";
    public string WorktreePath { get; init; } = "";
    public long CreatedAt { get; init; }
    public long LastActiveAt { get; init; }

    public string BaseBranch { get; init; } = "main";
    public string Model { get; init; } = "Sonnet 4.5";
    public long? StartedUtc { get; init; }
    public string Status { get; init; } = "Idle";
    public int UnreadCount { get; init; }
    public int? PrNumber { get; init; }
    public string? PrState { get; init; }
    public int DiffFiles { get; init; }
    public int DiffAdd { get; init; }
    public int DiffDel { get; init; }
    public string? ClaudeSessionId { get; init; }
    public string? PlanJson { get; init; }
    // "default" | "acceptEdits" | "bypassPermissions" — the CLI value we pass through.
    public string PermissionMode { get; init; } = "default";
}
