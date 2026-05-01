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
        string HeadRefName, string BaseRefName, string Title,
        long? MergedAtUnixMs);

    // null = "no PR found" or "gh not usable". Not distinguished — caller just hides the card.
    public static PullRequestInfo? TryGetPullRequest(string worktreePath)
    {
        if (string.IsNullOrEmpty(worktreePath) || !Directory.Exists(worktreePath)) return null;

        // No `gh --version` preflight: just attempt the call. A missing gh fails the
        // Process.Start inside Run with a Win32Exception, which we map to (-1, "", "")
        // and treat as "no PR". The previous probe spawned a fresh gh per session at
        // startup — on Windows where every Process.Start carries a 50–150ms AV-scan
        // tax, that doubled the gh fan-out for no signal we actually used.
        var (code, stdout, _) = Run(worktreePath,
            "pr", "view",
            "--json", "number,state,isDraft,headRefName,baseRefName,title,mergedAt");
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
                Title: Str(r, "title") ?? "",
                MergedAtUnixMs: ParseMergedAt(Str(r, "mergedAt")));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // gh returns mergedAt as ISO-8601 (e.g. "2024-12-01T15:23:00Z") for merged PRs and the
    // empty string / "0001-01-01T00:00:00Z" zero value for unmerged ones. Treat anything
    // that doesn't parse, or is at/before the unix epoch, as null.
    private static long? ParseMergedAt(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        if (!DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dto))
            return null;
        var ms = dto.ToUnixTimeMilliseconds();
        return ms <= 0 ? null : ms;
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

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // gh isn't on PATH. This is the "no gh installed" branch — equivalent to a
            // failed availability check, but without the extra subprocess that the old
            // GhAvailable() preflight cost us per call.
            return (-1, "", "");
        }
        if (proc is null) return (-1, "", "");

        using (proc)
        {
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
    }

    private static string? Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
