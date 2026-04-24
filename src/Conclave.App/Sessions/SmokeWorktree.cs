using System.Diagnostics;

namespace Conclave.App.Sessions;

// Throwaway: verify worktree add/remove against a real scratch repo.
// Invoked via `dotnet run -- --smoke-worktree`.
internal static class SmokeWorktree
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"conclave-wt-smoke-{Guid.NewGuid():N}");
        var repo = Path.Combine(root, "repo");
        var wt = Path.Combine(root, "wt", "brave-otter");
        try
        {
            Directory.CreateDirectory(repo);
            Git(repo, "init", "-b", "main");
            Git(repo, "config", "user.email", "smoke@conclave.local");
            Git(repo, "config", "user.name", "smoke");
            File.WriteAllText(Path.Combine(repo, "README.md"), "hello\n");
            Git(repo, "add", ".");
            Git(repo, "commit", "-m", "init");

            Expect(WorktreeService.IsGitRepo(repo), "IsGitRepo recognises a real repo");
            Expect(!WorktreeService.IsGitRepo(root), "IsGitRepo rejects a non-repo");
            Expect(WorktreeService.DetectDefaultBranch(repo) == "main",
                "DetectDefaultBranch returns main");

            WorktreeService.AddWorktree(repo, wt, "conclave/brave-otter", "main");
            Expect(Directory.Exists(wt), "worktree directory exists");
            Expect(File.Exists(Path.Combine(wt, "README.md")), "worktree has repo content");
            Expect(File.Exists(Path.Combine(wt, ".git")),
                "worktree has a .git pointer file");

            WorktreeService.RemoveWorktree(repo, wt, "conclave/brave-otter");
            Expect(!Directory.Exists(wt), "worktree directory removed");

            // Branch should be gone too.
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = repo,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("branch");
            psi.ArgumentList.Add("--list");
            psi.ArgumentList.Add("conclave/brave-otter");
            using var p = Process.Start(psi)!;
            string branches = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Expect(string.IsNullOrWhiteSpace(branches), "branch deleted");

            Console.WriteLine("smoke-worktree: OK");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"smoke-worktree: FAIL — {ex}");
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void Git(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception($"git {string.Join(' ', args)} failed");
    }

    private static void Expect(bool cond, string what)
    {
        if (!cond) throw new Exception($"expectation failed: {what}");
    }
}
