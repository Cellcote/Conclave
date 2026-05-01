using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Conclave.App.Claude;

// Signature the MCP server invokes for each permission prompt. Receives the inner tool
// name (e.g. "Bash"), the original input JSON, the tool_use_id claude is asking about,
// and a cancellation token tied to the HTTP request. Returns the JSON body to ship back
// (use PermissionMcpServer.BuildAllow / BuildDeny).
public delegate Task<string> PermissionPromptHandler(
    string toolName, string inputJson, string toolUseId, CancellationToken ct);

// Minimal in-process MCP server hosted over HTTP on loopback. Exists only so claude can
// reach our --permission-prompt-tool callback via the Streamable HTTP transport without
// pulling in a separate process or IPC layer. One tool: permission_prompt(tool_name,
// input, tool_use_id) -> JSON-stringified { behavior: "allow" | "deny", ... }.
//
// Wire format: one POST per JSON-RPC request; reply body is the JSON-RPC response with
// Content-Type: application/json. The MCP Streamable-HTTP spec allows SSE too, but we
// never need to push unsolicited events — synchronous request/response is enough.
//
// Auth: random Bearer token; claude carries it via the `headers` field of --mcp-config.
// Bound to 127.0.0.1 so non-local processes can't reach it.
public sealed class PermissionMcpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stopCts = new();
    private readonly ConcurrentDictionary<string, PermissionPromptHandler> _handlers = new();
    private Task? _loop;

    public int Port { get; private set; }

    // Test-only escape hatch: when set and no per-turn handler matches the bearer token,
    // fall back to this. The smoke harness uses it; the real app always routes by token.
    internal PermissionPromptHandler? FallbackHandler { get; set; }

    // Per-turn registration. ClaudeService calls this at the start of a turn to mint a
    // bearer token that identifies its handler, threads the token into the --mcp-config
    // JSON it passes claude, and unregisters at turn end.
    public string RegisterHandler(PermissionPromptHandler handler)
    {
        var token = Guid.NewGuid().ToString("N");
        _handlers[token] = handler;
        return token;
    }

    public void UnregisterHandler(string token) => _handlers.TryRemove(token, out _);

    public void Start()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            int port = PickFreePort();
            try
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/mcp/");
                _listener.Start();
                Port = port;
                _loop = Task.Run(LoopAsync);
                return;
            }
            catch (HttpListenerException)
            {
                // Race between PickFreePort closing the probe socket and HttpListener
                // binding here — try another port.
            }
        }
        throw new InvalidOperationException("could not bind permission MCP listener after 10 attempts");
    }

    private static int PickFreePort()
    {
        // HttpListener prefixes don't accept port 0, so use a TcpListener probe to grab
        // an ephemeral port, then close it before HttpListener takes it.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task LoopAsync()
    {
        while (!_stopCts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }

            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            // Auth: Bearer token must match a registered handler. Each turn registers its
            // own handler, so the same token both authenticates and routes the call to
            // the right session. Reject anything else with 401.
            var auth = ctx.Request.Headers["Authorization"];
            PermissionPromptHandler? handler = null;
            if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer "))
            {
                var token = auth["Bearer ".Length..];
                _handlers.TryGetValue(token, out handler);
            }
            handler ??= FallbackHandler;
            if (handler is null)
            {
                ctx.Response.StatusCode = 401;
                ctx.Response.Close();
                return;
            }

            if (ctx.Request.HttpMethod != "POST")
            {
                ctx.Response.StatusCode = 405;
                ctx.Response.Close();
                return;
            }

            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            var responseJson = await DispatchAsync(body, handler);

            if (responseJson is null)
            {
                // Notification — no body to return.
                ctx.Response.StatusCode = 202;
                ctx.Response.Close();
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(responseJson);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
        catch (Exception ex)
        {
            try
            {
                ctx.Response.StatusCode = 500;
                var msg = Encoding.UTF8.GetBytes(ex.Message);
                await ctx.Response.OutputStream.WriteAsync(msg);
                ctx.Response.Close();
            }
            catch { /* response already disposed */ }
        }
    }

    // Returns the JSON-RPC response body, or null for a JSON-RPC notification (no body).
    private async Task<string?> DispatchAsync(string body, PermissionPromptHandler handler)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var hasId = root.TryGetProperty("id", out var idEl);
        var method = root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() ?? "" : "";

        // Notifications have no `id` and never get a response.
        if (!hasId)
        {
            // notifications/initialized + others — just acknowledge.
            return null;
        }

        switch (method)
        {
            case "initialize":
                return BuildInitializeResponse(idEl);
            case "tools/list":
                return BuildToolsListResponse(idEl);
            case "tools/call":
                return await BuildToolsCallResponseAsync(idEl, root, handler);
            default:
                return BuildErrorResponse(idEl, -32601, $"method not found: {method}");
        }
    }

    private static string BuildInitializeResponse(JsonElement id)
    {
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            WriteId(w, id);
            w.WriteStartObject("result");
            w.WriteString("protocolVersion", "2024-11-05");
            w.WriteStartObject("capabilities");
            w.WriteStartObject("tools");
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteStartObject("serverInfo");
            w.WriteString("name", "conclave-permissions");
            w.WriteString("version", "0.1.0");
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string BuildToolsListResponse(JsonElement id)
    {
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            WriteId(w, id);
            w.WriteStartObject("result");
            w.WriteStartArray("tools");

            // Single tool: permission_prompt. Schema mirrors what the SDK passes:
            // { tool_name: string, input: object, tool_use_id?: string }.
            w.WriteStartObject();
            w.WriteString("name", "permission_prompt");
            w.WriteString("description", "Asks the user whether to allow a tool invocation.");
            w.WriteStartObject("inputSchema");
            w.WriteString("type", "object");
            w.WriteStartObject("properties");
            w.WriteStartObject("tool_name"); w.WriteString("type", "string"); w.WriteEndObject();
            w.WriteStartObject("input"); w.WriteString("type", "object"); w.WriteEndObject();
            w.WriteStartObject("tool_use_id"); w.WriteString("type", "string"); w.WriteEndObject();
            w.WriteEndObject();
            w.WriteStartArray("required");
            w.WriteStringValue("tool_name");
            w.WriteStringValue("input");
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private async Task<string> BuildToolsCallResponseAsync(JsonElement id, JsonElement root, PermissionPromptHandler handler)
    {
        // Extract params.name + params.arguments.{tool_name, input, tool_use_id}.
        string toolName = "";
        string innerToolName = "";
        string innerInputJson = "{}";
        string innerToolUseId = "";

        if (root.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            if (p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                toolName = n.GetString() ?? "";
            if (p.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object)
            {
                if (args.TryGetProperty("tool_name", out var tn) && tn.ValueKind == JsonValueKind.String)
                    innerToolName = tn.GetString() ?? "";
                if (args.TryGetProperty("input", out var ti))
                    innerInputJson = ti.GetRawText();
                if (args.TryGetProperty("tool_use_id", out var tuid) && tuid.ValueKind == JsonValueKind.String)
                    innerToolUseId = tuid.GetString() ?? "";
            }
        }

        if (toolName != "permission_prompt")
            return BuildErrorResponse(id, -32602, $"unknown tool: {toolName}");

        var decisionJson = await handler(innerToolName, innerInputJson, innerToolUseId, _stopCts.Token);

        // Tool result wraps the JSON-stringified decision in a text content block —
        // that's the convention the Claude SDK reads for permission prompt tools.
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            WriteId(w, id);
            w.WriteStartObject("result");
            w.WriteStartArray("content");
            w.WriteStartObject();
            w.WriteString("type", "text");
            w.WriteString("text", decisionJson);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string BuildErrorResponse(JsonElement id, int code, string message)
    {
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            WriteId(w, id);
            w.WriteStartObject("error");
            w.WriteNumber("code", code);
            w.WriteString("message", message);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteId(Utf8JsonWriter w, JsonElement id)
    {
        switch (id.ValueKind)
        {
            case JsonValueKind.Number:
                if (id.TryGetInt64(out var n)) w.WriteNumber("id", n);
                else w.WriteNumber("id", id.GetDouble());
                break;
            case JsonValueKind.String:
                w.WriteString("id", id.GetString());
                break;
            default:
                w.WriteNull("id");
                break;
        }
    }

    public static string BuildAllow(string updatedInputJson)
    {
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("behavior", "allow");
            // updatedInput must be re-emitted as a JSON object, not a string.
            w.WritePropertyName("updatedInput");
            using var doc = JsonDocument.Parse(updatedInputJson);
            doc.RootElement.WriteTo(w);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static string BuildDeny(string message)
    {
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("behavior", "deny");
            w.WriteString("message", message);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public string BuildMcpConfigJson(string token)
    {
        // Inline JSON for the --mcp-config flag. Single server entry named "conclave"
        // pointing at our HTTP endpoint, with the bearer token the listener checks.
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteStartObject("mcpServers");
            w.WriteStartObject("conclave");
            w.WriteString("type", "http");
            w.WriteString("url", $"http://127.0.0.1:{Port}/mcp");
            w.WriteStartObject("headers");
            w.WriteString("Authorization", $"Bearer {token}");
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public void Dispose()
    {
        _stopCts.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }
}
