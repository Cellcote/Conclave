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
        string? appendSystemPrompt = null,
        IReadOnlyList<string>? additionalDirs = null,
        string? mcpConfigJson = null,
        string? permissionPromptTool = null,
        string? settingsJson = null,
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
        if (!string.IsNullOrEmpty(appendSystemPrompt))
        {
            psi.ArgumentList.Add("--append-system-prompt");
            psi.ArgumentList.Add(appendSystemPrompt);
        }
        if (additionalDirs is not null)
        {
            foreach (var dir in additionalDirs)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                psi.ArgumentList.Add("--add-dir");
                psi.ArgumentList.Add(dir);
            }
        }
        // Permission MCP wiring: --mcp-config registers our local HTTP server under the
        // name "conclave"; --permission-prompt-tool tells claude to call our tool when a
        // gated tool needs approval. Pass both or neither — half-wiring just produces an
        // unhelpful "MCP tool ... not found" error.
        //
        // Use a temp file rather than inline JSON: the config carries a per-turn bearer
        // token and on Linux process args are world-readable via /proc/<pid>/cmdline.
        // Writing to a 0600 file keeps the token off process listings on shared
        // machines. Cleaned up in `finally` after the subprocess exits.
        string? mcpConfigPath = null;
        if (!string.IsNullOrEmpty(mcpConfigJson))
        {
            mcpConfigPath = WriteOwnerOnlyTempFile(mcpConfigJson);
            psi.ArgumentList.Add("--mcp-config");
            psi.ArgumentList.Add(mcpConfigPath);
        }
        if (!string.IsNullOrEmpty(permissionPromptTool))
        {
            psi.ArgumentList.Add("--permission-prompt-tool");
            psi.ArgumentList.Add(permissionPromptTool);
        }
        // Used for permission gating: inject `permissions.ask` so tools that the CLI
        // would otherwise auto-allow in --print mode get routed through our
        // permission-prompt MCP tool instead. Layered on top of the user's regular
        // settings (settings/local/etc) — see Claude Code's setting-source merge order.
        if (!string.IsNullOrEmpty(settingsJson))
        {
            psi.ArgumentList.Add("--settings");
            psi.ArgumentList.Add(settingsJson);
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

        try
        {
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
        finally
        {
            // Best-effort: claude has already read the file by the time it spawns its
            // own MCP client, so a delete here is safe. Swallow IO errors — leaving a
            // temp file behind is preferable to crashing the turn on cleanup.
            if (mcpConfigPath is not null)
            {
                try { File.Delete(mcpConfigPath); } catch { }
            }
        }
    }

    // Writes `content` to a fresh temp file readable only by the current user. On POSIX
    // we use FileStreamOptions.UnixCreateMode so the file is created with 0600 from the
    // start — chmodding after WriteAllText leaves a brief race where another local user
    // could read the bearer token at the default umask. On Windows the user's TEMP dir
    // is already ACL-restricted, so we accept default perms there.
    private static string WriteOwnerOnlyTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"conclave-mcp-{Guid.NewGuid():N}.json");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var fs = new FileStream(path, options))
        using (var sw = new StreamWriter(fs, Encoding.UTF8))
            sw.Write(content);
        return path;
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
