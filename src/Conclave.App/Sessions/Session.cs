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
    public long? PrMergedAt { get; init; }
    public int DiffFiles { get; init; }
    public int DiffAdd { get; init; }
    public int DiffDel { get; init; }
    public string? ClaudeSessionId { get; init; }
    public string? PlanJson { get; init; }
    // "default" | "acceptEdits" | "bypassPermissions" — the CLI value we pass through.
    public string PermissionMode { get; init; } = "default";
    // Running total of TotalCostUsd accumulated across every result event for this session.
    public double TotalCostUsd { get; init; }
    // System-prompt context to inject on the next turn via `--append-system-prompt`. Set on
    // sessions forked at a non-tail message so the model has the prior conversation as
    // context (it can't be `--resume`-d into the source's claude session at a specific
    // message). Cleared after the first successful turn that consumes it.
    public string? PendingPreamble { get; init; }
}
