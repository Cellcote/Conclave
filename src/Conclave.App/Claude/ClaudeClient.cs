using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Conclave.App.Claude;

// Spawns `claude -p --output-format=stream-json --verbose [...]`, feeds the user prompt
// via stdin, and streams parsed events out as they arrive. One instance per turn.
public sealed class ClaudeClient
{
    public async IAsyncEnumerable<StreamJsonEvent> StreamAsync(
        string cwd,
        string prompt,
        string? claudeSessionId,
        string? modelAlias,
        string? permissionMode = null,
        bool includePartialMessages = true,
        string? forkFromSessionId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("claude")
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--verbose");
        if (includePartialMessages)
        {
            psi.ArgumentList.Add("--include-partial-messages");
        }
        if (!string.IsNullOrEmpty(modelAlias))
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(modelAlias);
        }
        if (!string.IsNullOrEmpty(permissionMode))
        {
            psi.ArgumentList.Add("--permission-mode");
            psi.ArgumentList.Add(permissionMode);
        }
        // Resume target: prefer a fork-from id (first turn of a freshly forked session) over
        // the regular resume id, since a forked session won't have its own ClaudeSessionId
        // until the first SystemInitEvent comes back. `--fork-session` tells claude to mint
        // a new session id from the resumed point instead of overwriting the original.
        // Only pass UUIDs that are well-formed — passing garbage makes claude fail with
        // opaque errors, and we'd rather start fresh than spew.
        if (!string.IsNullOrEmpty(forkFromSessionId) && Guid.TryParseExact(forkFromSessionId, "D", out _))
        {
            psi.ArgumentList.Add("--resume");
            psi.ArgumentList.Add(forkFromSessionId);
            psi.ArgumentList.Add("--fork-session");
        }
        else if (!string.IsNullOrEmpty(claudeSessionId) && Guid.TryParseExact(claudeSessionId, "D", out _))
        {
            psi.ArgumentList.Add("--resume");
            psi.ArgumentList.Add(claudeSessionId);
        }

        // Spawn off the UI thread: Process.Start with three redirected pipes can block the
        // calling thread long enough to swallow the layout pass for the just-appended user
        // message, so the user's bubble doesn't paint until claude itself starts streaming.
        using var proc = await Task.Run(() => Process.Start(psi), ct)
            ?? throw new InvalidOperationException("failed to spawn claude");

        // Collect stderr concurrently so it doesn't fill the pipe and deadlock us.
        var stderrBuf = new StringBuilder();
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrBuf.AppendLine(e.Data); };
        proc.BeginErrorReadLine();

        // Write the prompt and close stdin so claude processes it.
        await proc.StandardInput.WriteAsync(prompt.AsMemory(), ct);
        proc.StandardInput.Close();

        string? line;
        while ((line = await proc.StandardOutput.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var ev = StreamJsonParser.Parse(line);
            if (ev is not null) yield return ev;
        }

        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            var stderr = stderrBuf.ToString().Trim();
            throw new InvalidOperationException(
                $"claude exited with code {proc.ExitCode}" +
                (string.IsNullOrEmpty(stderr) ? "" : $"\n{stderr}"));
        }
    }

    // Map our display names ("Sonnet 4.5" / "Haiku 4.5" / "Opus 4") to the CLI's model aliases.
    public static string? ModelAliasFromDisplay(string display) => display switch
    {
        "Haiku 4.5" => "haiku",
        "Sonnet 4.5" => "sonnet",
        "Opus 4" => "opus",
        _ => null,
    };
}
