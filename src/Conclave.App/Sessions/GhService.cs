using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Conclave.App.Sessions;

// Thin wrapper around `gh pr view`. Returns null in every failure mode — no `gh`,
// not authenticated, no PR for the current branch, etc. — so the PR card simply
// doesn't render. Meant to be called on a background thread; each probe runs its
// own process.
public static class GhService
{
    public readonly record struct PullRequestInfo(
        int Number, string State, bool IsDraft,
        string HeadRefName, string BaseRefName, string Title);

    // null = "no PR found" or "gh not usable". Not distinguished — caller just hides the card.
    public static PullRequestInfo? TryGetPullRequest(string worktreePath)
    {
        if (string.IsNullOrEmpty(worktreePath) || !Directory.Exists(worktreePath)) return null;
        if (!GhAvailable()) return null;

        var (code, stdout, _) = Run(worktreePath,
            "pr", "view",
            "--json", "number,state,isDraft,headRefName,baseRefName,title");
        if (code != 0 || string.IsNullOrWhiteSpace(stdout)) return null;

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var r = doc.RootElement;
            return new PullRequestInfo(
                Number: r.TryGetProperty("number", out var n) && n.TryGetInt32(out var num) ? num : 0,
                State: Str(r, "state") ?? "OPEN",
                IsDraft: r.TryGetProperty("isDraft", out var d) && d.ValueKind == JsonValueKind.True,
                HeadRefName: Str(r, "headRefName") ?? "",
                BaseRefName: Str(r, "baseRefName") ?? "",
                Title: Str(r, "title") ?? "");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool GhAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("gh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--version");
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(800);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static (int Code, string Stdout, string Stderr) Run(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("gh")
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("failed to start gh");
        var sout = new StringBuilder();
        var serr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) sout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) serr.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        // Hard cap so a network hang doesn't freeze the UI thread.
        if (!proc.WaitForExit(5000))
        {
            try { proc.Kill(); } catch { }
            return (-1, sout.ToString(), serr.ToString());
        }
        return (proc.ExitCode, sout.ToString(), serr.ToString());
    }

    private static string? Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
