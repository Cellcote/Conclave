using System.Diagnostics;

namespace Conclave.App.Claude;

// One-shot probe of the local `claude` binary. Runs `claude --version` once at app
// startup and caches the result. Surfaced in the titlebar so users can see at a glance
// which CLI is wired up, and lets ViewModels gate features (e.g. Opus 4.7 needs ≥ 2.1.111).
public sealed class ClaudeCapabilities
{
    public string? Version { get; }
    public bool Available => !string.IsNullOrEmpty(Version);

    // --fork-session was added to Claude Code in 2.x. Conservative gate so the menu item
    // hides on older builds where the flag would error out.
    public bool SupportsForkSession => AtLeast(Version, "2.0.0");

    private ClaudeCapabilities(string? version)
    {
        Version = version;
    }

    public static ClaudeCapabilities Detect()
    {
        try
        {
            var psi = new ProcessStartInfo("claude")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--version");

            using var p = Process.Start(psi);
            if (p is null) return new ClaudeCapabilities(null);

            var stdout = p.StandardOutput.ReadToEnd().Trim();
            // "claude --version" doesn't reliably return in some environments; cap the wait.
            if (!p.WaitForExit(2500))
            {
                try { p.Kill(); } catch { }
                return new ClaudeCapabilities(null);
            }
            if (p.ExitCode != 0) return new ClaudeCapabilities(null);

            // Output looks like "2.1.119 (Claude Code)" — keep the leading version token.
            var version = stdout.Split(' ', 2)[0];
            return new ClaudeCapabilities(version);
        }
        catch
        {
            return new ClaudeCapabilities(null);
        }
    }

    // Compare two semver-ish strings (X.Y.Z). Returns true if `version` >= `minimum`.
    public static bool AtLeast(string? version, string minimum)
    {
        if (string.IsNullOrEmpty(version)) return false;
        var a = ParseTriple(version);
        var b = ParseTriple(minimum);
        for (int i = 0; i < 3; i++)
        {
            if (a[i] > b[i]) return true;
            if (a[i] < b[i]) return false;
        }
        return true;
    }

    private static int[] ParseTriple(string v)
    {
        var parts = v.Split('.');
        var triple = new int[3];
        for (int i = 0; i < 3 && i < parts.Length; i++)
            int.TryParse(parts[i], out triple[i]);
        return triple;
    }
}
