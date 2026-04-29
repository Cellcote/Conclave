namespace Conclave.App.Sessions;

// Validates user-supplied branch names against git's `check-ref-format --branch` rules
// before they reach `git`. We pass branch names as positional args on `git worktree add`
// and `git branch -D`; if a name starts with `-` git would parse it as a flag, and other
// metacharacters (~, ^, :, ?, *, [, \, space, controls) cause obscure failures further
// down. Catch them here with a clear message instead.
public static class BranchNameValidator
{
    // Returns null on success, or a human-readable reason for the failure.
    public static string? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Branch name cannot be empty.";

        var n = name.Trim();

        if (n.StartsWith('-'))
            return "Branch name cannot start with '-'.";
        if (n.StartsWith('/') || n.EndsWith('/'))
            return "Branch name cannot start or end with '/'.";
        if (n.EndsWith('.') || n.EndsWith(".lock"))
            return "Branch name cannot end with '.' or '.lock'.";
        if (n.Contains("..") || n.Contains("//") || n.Contains("/.") || n.Contains("@{"))
            return "Branch name cannot contain '..', '//', '/.' or '@{'.";
        if (n == "@")
            return "Branch name cannot be just '@'.";

        foreach (var c in n)
        {
            if (c < 0x20 || c == 0x7F)
                return "Branch name cannot contain control characters.";
            if (c is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\')
                return $"Branch name cannot contain '{c}'.";
        }

        return null;
    }

    public static bool IsValid(string? name) => Validate(name) is null;
}
