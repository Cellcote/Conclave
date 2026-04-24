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
        // Only pass --resume if we have a well-formed UUID. Passing garbage makes claude
        // fail with opaque errors; we'd rather start fresh than spew.
        if (!string.IsNullOrEmpty(claudeSessionId) && Guid.TryParseExact(claudeSessionId, "D", out _))
        {
            psi.ArgumentList.Add("--resume");
            psi.ArgumentList.Add(claudeSessionId);
        }

        using var proc = Process.Start(psi)
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
