namespace Conclave.App.Claude;

// Headless end-to-end check for the permission MCP wire format. Spawns claude with our
// in-process HTTP MCP server registered as the --permission-prompt-tool, asks it to run
// a gated Bash command, and verifies our tool was invoked.
//
// "Always allow" stub — the spike's job is to confirm claude reaches our server, calls
// our tool with the expected argument shape, and accepts our reply. The interactive
// approve/reject UX is built on top of this once the wire is proven.
//
// Invoke via `dotnet run -- --smoke-permission`.
internal static class SmokePermission
{
    public static async Task<int> RunAsync()
    {
        using var server = new PermissionMcpServer();

        int callCount = 0;
        string? lastToolName = null;
        string? lastInput = null;

        Task<string> AllowAll(string toolName, string inputJson, string toolUseId, CancellationToken _)
        {
            Interlocked.Increment(ref callCount);
            lastToolName = toolName;
            lastInput = inputJson;
            Console.WriteLine($"  permission_prompt: tool={toolName} input={Truncate(inputJson)} useId={toolUseId}");
            return Task.FromResult(PermissionMcpServer.BuildAllow(inputJson));
        }

        server.Start();
        var token = server.RegisterHandler(AllowAll);
        Console.WriteLine($"  mcp listener: http://127.0.0.1:{server.Port}/mcp (token=…{token[^6..]})");

        var client = new ClaudeClient();
        var cwd = Environment.CurrentDirectory;

        bool sawResult = false;
        bool resultErrored = false;

        try
        {
            await foreach (var ev in client.StreamAsync(
                cwd,
                // Force a Bash call with a command that isn't already on the user's
                // settings allowlist (echo / dig / curl etc are common allowlist entries).
                // `whoami` is plain and rarely allowlisted, so default mode should gate it.
                "Run `whoami` using the Bash tool. Do not respond with anything else.",
                claudeSessionId: null,
                modelAlias: null,
                permissionMode: "default",
                mcpConfigJson: server.BuildMcpConfigJson(token),
                permissionPromptTool: "mcp__conclave__permission_prompt",
                // `default` permission mode in --print silently auto-allows; we have to
                // inject an explicit `ask` rule so the CLI routes the call through our
                // prompt tool instead of just running it.
                settingsJson: "{\"permissions\":{\"ask\":[\"Bash\",\"Edit\",\"Write\",\"NotebookEdit\",\"WebFetch\",\"WebSearch\",\"Task\"]}}"))
            {
                Console.WriteLine($"  event: {ev.Type,-10}");
                if (ev is ResultEvent r)
                {
                    sawResult = true;
                    resultErrored = r.IsError;
                    Console.WriteLine($"           stop={r.StopReason} result={Truncate(r.Result)}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"smoke-permission: FAIL — {ex.Message}");
            return 1;
        }

        Console.WriteLine($"  callCount={callCount} lastTool={lastToolName} sawResult={sawResult} errored={resultErrored}");

        if (callCount == 0)
        {
            Console.Error.WriteLine("smoke-permission: FAIL — permission_prompt was never called");
            return 1;
        }
        if (lastToolName != "Bash")
        {
            Console.Error.WriteLine($"smoke-permission: FAIL — expected Bash, got {lastToolName}");
            return 1;
        }
        if (!sawResult || resultErrored)
        {
            Console.Error.WriteLine("smoke-permission: FAIL — claude turn did not complete cleanly");
            return 1;
        }

        Console.WriteLine("smoke-permission: OK");
        return 0;
    }

    private static string Truncate(string? s, int n = 80)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace('\n', ' ');
        return s.Length <= n ? s : s[..n] + "…";
    }
}
