using System.Diagnostics;
using System.Text;

namespace Conclave.App.Sessions;

// Thin wrapper around the `git` CLI for worktree operations.
// Keeps the spike simple — no LibGit2Sharp native dependency.
public static class WorktreeService
{
    public static bool IsGitRepo(string path)
    {
        if (!Directory.Exists(path)) return false;
        var (code, _, _) = Run(path, "rev-parse", "--git-dir");
        return code == 0;
    }

    // Best-effort detection. Each candidate is verified to resolve to a commit
    // before being returned — otherwise `git worktree add … <base>` fails with
    // "fatal: not a valid object name". Throws if the repo has no branches at
    // all (new repo with zero commits).
    public static string DetectDefaultBranch(string repoPath)
    {
        // 1. origin/HEAD
        var (c1, out1, _) = Run(repoPath, "symbolic-ref", "--short", "refs/remotes/origin/HEAD");
        if (c1 == 0 && !string.IsNullOrWhiteSpace(out1))
        {
            var s = out1.Trim();
            var slash = s.IndexOf('/');
            var name = slash >= 0 ? s[(slash + 1)..] : s;
            if (BranchExists(repoPath, name)) return name;
        }

        // 2. current HEAD if it's a real branch pointing to a real commit
        var (c2, out2, _) = Run(repoPath, "rev-parse", "--abbrev-ref", "HEAD");
        if (c2 == 0)
        {
            var name = out2.Trim();
            if (name != "HEAD" && BranchExists(repoPath, name)) return name;
        }

        // 3. common defaults
        foreach (var cand in new[] { "main", "master", "trunk", "develop" })
            if (BranchExists(repoPath, cand)) return cand;

        // 4. any local branch at all
        var (c4, out4, _) = Run(repoPath, "for-each-ref", "--format=%(refname:short)", "refs/heads/");
        if (c4 == 0)
        {
            var first = out4.Split('\n').Select(s => s.Trim()).FirstOrDefault(s => !string.IsNullOrEmpty(s));
            if (first is not null) return first;
        }

        throw new InvalidOperationException(
            "Repository has no commits yet. Add an initial commit before creating a session.");
    }

    public static bool BranchExists(string repoPath, string name)
    {
        var (code, _, _) = Run(repoPath, "rev-parse", "--verify", "--quiet", $"refs/heads/{name}");
        return code == 0;
    }

    public readonly record struct DiffStat(
        int Files, int Add, int Del, IReadOnlyList<DiffFileChange> Changes);

    public readonly record struct DiffFileChange(
        string Kind,   // "A" (added), "M" (modified), "D" (deleted), "R" (renamed)
        string Path, int Add, int Del);

    // Returns the diff between base_branch...HEAD for this worktree. Used to populate the
    // sidebar +N −M summary and the right panel's per-file list.
    public static DiffStat ComputeDiff(string worktreePath, string baseBranch)
    {
        if (string.IsNullOrEmpty(worktreePath) || !Directory.Exists(worktreePath))
            return new DiffStat(0, 0, 0, Array.Empty<DiffFileChange>());

        var spec = $"{baseBranch}...HEAD";
        var (numCode, numOut, _) = Run(worktreePath, "diff", "--numstat", spec);
        var (nameCode, nameOut, _) = Run(worktreePath, "diff", "--name-status", spec);
        if (numCode != 0 || nameCode != 0)
            return new DiffStat(0, 0, 0, Array.Empty<DiffFileChange>());

        var kindByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in nameOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            // "R100\told\tnew" → use "R" + the new path as key.
            var kind = parts[0].Length > 0 ? parts[0][..1].ToUpperInvariant() : "";
            var path = parts.Length >= 3 ? parts[^1] : parts[1];
            kindByPath[path] = kind;
        }

        var changes = new List<DiffFileChange>();
        int files = 0, totalAdd = 0, totalDel = 0;
        foreach (var line in numOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 3) continue;
            int add = parts[0] == "-" ? 0 : int.TryParse(parts[0], out var a) ? a : 0;
            int del = parts[1] == "-" ? 0 : int.TryParse(parts[1], out var d) ? d : 0;
            var path = parts[2];
            kindByPath.TryGetValue(path, out var kind);
            changes.Add(new DiffFileChange(kind ?? "M", path, add, del));
            files++;
            totalAdd += add;
            totalDel += del;
        }
        return new DiffStat(files, totalAdd, totalDel, changes);
    }

    // Creates a new branch off baseBranch and checks it out in worktreePath.
    public static void AddWorktree(string repoPath, string worktreePath, string branchName, string baseBranch)
    {
        var parent = Path.GetDirectoryName(worktreePath);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        var (code, _, err) = Run(repoPath, "worktree", "add", "-b", branchName, worktreePath, baseBranch);
        if (code != 0)
            throw new InvalidOperationException($"git worktree add failed: {err.Trim()}");
    }

    // Removes the worktree and deletes the branch.
    public static void RemoveWorktree(string repoPath, string worktreePath, string branchName)
    {
        // --force because the worktree may have uncommitted changes; a user-initiated delete
        // is an intentional discard. Swallow errors from the remove step because we still
        // want to attempt the branch delete even if the worktree is already gone.
        Run(repoPath, "worktree", "remove", "--force", worktreePath);
        Run(repoPath, "branch", "-D", branchName);
    }

    private static (int Code, string Stdout, string Stderr) Run(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("failed to start git");
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();
        return (proc.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
