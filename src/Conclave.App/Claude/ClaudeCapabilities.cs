using System.Diagnostics;
using Conclave.App.Views;

namespace Conclave.App.Claude;

// One-shot probe of the local `claude` binary. Runs `claude --version` once at app
// startup and caches the result. Surfaced in the titlebar so users can see at a glance
// which CLI is wired up, and lets ViewModels gate features (e.g. Opus 4.7 needs ≥ 2.1.111).
//
// The probe runs on a background thread (BeginProbe) — on Windows, `claude` is a
// node/npm shim that can take >1s to spin up, and we used to block the UI thread
// waiting for it. The instance starts in an empty state (Available == false) and
// raises PropertyChanged once the result lands so XAML bindings update in place.
public sealed class ClaudeCapabilities : Observable
{
    private string? _version;
    public string? Version
    {
        get => _version;
        private set
        {
            if (Set(ref _version, value))
            {
                Notify(nameof(Available));
                Notify(nameof(SupportsForkSession));
            }
        }
    }

    public bool Available => !string.IsNullOrEmpty(_version);

    // --fork-session was added to Claude Code in 2.x. Conservative gate so the menu item
    // hides on older builds where the flag would error out.
    public bool SupportsForkSession => AtLeast(_version, "2.0.0");

    // Kick off `claude --version` on the thread pool. Result is published back to the
    // capabilities object via PropertyChanged; bindings update wherever they sit. Fire
    // and forget — failures leave Version null, which matches the "not detected" UX.
    public void BeginProbe()
    {
        _ = Task.Run(() =>
        {
            var version = ProbeOnce();
            // PropertyChanged subscribers (Avalonia bindings) marshal themselves to the
            // UI thread, so we don't need a Dispatcher hop here.
            Version = version;
            StartupLog.Mark(version is null
                ? "claude probe complete (not detected)"
                : $"claude probe complete: {version}");
        });
    }

    private static string? ProbeOnce()
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
            if (p is null) return null;

            var stdout = p.StandardOutput.ReadToEnd().Trim();
            // "claude --version" doesn't reliably return in some environments; cap the wait.
            if (!p.WaitForExit(2500))
            {
                try { p.Kill(); } catch { }
                return null;
            }
            if (p.ExitCode != 0) return null;

            // Output looks like "2.1.119 (Claude Code)" — keep the leading version token.
            return stdout.Split(' ', 2)[0];
        }
        catch
        {
            return null;
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
