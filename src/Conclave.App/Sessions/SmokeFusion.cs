using System.Diagnostics;

namespace Conclave.App.Sessions;

// Throwaway: exercise the fusion-add / fusion-fork helpers against two real scratch repos.
// Invoked via `dotnet run -- --smoke-fusion`.
internal static class SmokeFusion
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"conclave-fusion-smoke-{Guid.NewGuid():N}");
        try
        {
            var repoA = Path.Combine(root, "repo-a");
            var repoB = Path.Combine(root, "repo-b");
            InitRepo(repoA);
            InitRepo(repoB);

            // Per-fusion-session worktrees, both on branch conclave/feat-x.
            var wtA = Path.Combine(root, "wt", "fusion-a");
            var wtB = Path.Combine(root, "wt", "fusion-b");
            var specs = new[]
            {
                new WorktreeService.FusionAddSpec(repoA, wtA, "conclave/feat-x", "main"),
                new WorktreeService.FusionAddSpec(repoB, wtB, "conclave/feat-x", "main"),
            };
            WorktreeService.AddWorktreesForFusion(specs);
            Expect(Directory.Exists(wtA), "fusion worktree A exists");
            Expect(Directory.Exists(wtB), "fusion worktree B exists");

            // Rollback: introduce a conflict by recreating an existing branch in repoA. The
            // second add (in repoB) should never run because the first should fail; conversely
            // if the first succeeds and the second fails, the first must be rolled back. We
            // verify the second case by pre-creating the branch in repoB only.
            var wtA2 = Path.Combine(root, "wt2", "fusion-a");
            var wtB2 = Path.Combine(root, "wt2", "fusion-b");
            Git(repoB, "branch", "conclave/feat-y");
            try
            {
                WorktreeService.AddWorktreesForFusion(new[]
                {
                    new WorktreeService.FusionAddSpec(repoA, wtA2, "conclave/feat-y", "main"),
                    new WorktreeService.FusionAddSpec(repoB, wtB2, "conclave/feat-y", "main"),
                });
                throw new Exception("expected fusion add to fail when branch already exists");
            }
            catch (InvalidOperationException) { /* expected */ }
            Expect(!Directory.Exists(wtA2), "rollback removed the partial worktree in repo A");

            // Fork: branch off the first fusion's HEAD in both repos.
            var forkA = Path.Combine(root, "fork", "fusion-a");
            var forkB = Path.Combine(root, "fork", "fusion-b");
            WorktreeService.ForkWorktreesForFusion(new[]
            {
                new WorktreeService.FusionForkSpec(repoA, wtA, forkA, "conclave/feat-x-fork"),
                new WorktreeService.FusionForkSpec(repoB, wtB, forkB, "conclave/feat-x-fork"),
            });
            Expect(Directory.Exists(forkA), "forked worktree A exists");
            Expect(Directory.Exists(forkB), "forked worktree B exists");

            // Cleanup all worktrees.
            WorktreeService.RemoveWorktree(repoA, forkA, "conclave/feat-x-fork");
            WorktreeService.RemoveWorktree(repoB, forkB, "conclave/feat-x-fork");
            WorktreeService.RemoveWorktree(repoA, wtA, "conclave/feat-x");
            WorktreeService.RemoveWorktree(repoB, wtB, "conclave/feat-x");

            Console.WriteLine("smoke-fusion: OK");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"smoke-fusion: FAIL — {ex}");
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void InitRepo(string path)
    {
        Directory.CreateDirectory(path);
        Git(path, "init", "-b", "main");
        Git(path, "config", "user.email", "smoke@conclave.local");
        Git(path, "config", "user.name", "smoke");
        File.WriteAllText(Path.Combine(path, "README.md"), "hello\n");
        Git(path, "add", ".");
        Git(path, "commit", "-m", "init");
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
