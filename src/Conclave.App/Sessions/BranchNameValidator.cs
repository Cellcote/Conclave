namespace Conclave.App.Sessions;

// Validates user-supplied branch names against git's `check-ref-format --branch` rules
// before they reach `git`. We pass branch names as positional args on `git worktree add`
// and `git branch -D`; if a name starts with `-` git would parse it as a flag, and other
// metacharacters (~, ^, :, ?, *, [, \, space, controls) cause obscure failures further
// down. Catch them here with a clear message instead.
public static class BranchNameValidator
{
    // Returns null on success, or a human-readable reason for the failure.
    // Validates the input string exactly as given — callers must trim themselves so
    // IsValid("foo ") can't disagree with what actually flows into git.
    public static string? Validate(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "Branch name cannot be empty.";

        if (name.StartsWith('-'))
            return "Branch name cannot start with '-'.";
        if (name.StartsWith('.'))
            return "Branch name cannot start with '.'.";
        if (name.StartsWith('/') || name.EndsWith('/'))
            return "Branch name cannot start or end with '/'.";
        if (name.EndsWith('.') || name.EndsWith(".lock"))
            return "Branch name cannot end with '.' or '.lock'.";
        if (name.Contains("..") || name.Contains("//") || name.Contains("/.") || name.Contains("@{"))
            return "Branch name cannot contain '..', '//', '/.' or '@{'.";
        if (name == "@")
            return "Branch name cannot be just '@'.";

        foreach (var c in name)
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
