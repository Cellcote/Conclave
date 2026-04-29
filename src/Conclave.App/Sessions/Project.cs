namespace Conclave.App.Sessions;

// A project the user has registered with Conclave. Either a single git repo (Kind = "repo")
// or a fusion that bundles N repos (Kind = "fusion"). For fusion rows, Path and DefaultBranch
// are unused — the real values live on the underlying member projects (see ProjectMember).
public sealed record Project(
    string Id,
    string Name,
    string Path,
    string DefaultBranch,
    long CreatedAt,
    string Kind = "repo");

public static class ProjectKinds
{
    public const string Repo = "repo";
    public const string Fusion = "fusion";
}

// Membership row for a fusion project. Ordinal 0 is the primary repo (whose worktree is the
// session's cwd); ordinal >= 1 are the secondaries passed to claude via --add-dir.
public sealed record ProjectMember(
    string FusionId,
    string MemberId,
    int Ordinal);

// One row per member repo for a fusion session. Empty for non-fusion sessions, where the
// session's WorktreePath/BranchName/BaseBranch on the sessions row is sufficient.
public sealed record SessionWorktree(
    string SessionId,
    string MemberProjectId,
    string WorktreePath,
    string BranchName,
    string BaseBranch,
    int Ordinal);
