namespace Conclave.App.Sessions;

// A git repo the user has registered with Conclave.
public sealed record Project(
    string Id,
    string Name,
    string Path,
    string DefaultBranch,
    long CreatedAt);
