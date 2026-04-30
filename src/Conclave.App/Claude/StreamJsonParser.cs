using System.Text.Json;

namespace Conclave.App.Claude;

public static class StreamJsonParser
{
    public static StreamJsonEvent? Parse(string line)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException) { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var type = Str(root, "type") ?? "";
            var sessionId = Str(root, "session_id") ?? "";
            var uuid = Str(root, "uuid");

            return type switch
            {
                "system" => ParseSystem(root, type, sessionId, uuid),
                "assistant" => ParseAssistant(root, type, sessionId, uuid),
                "user" => ParseUser(root, type, sessionId, uuid),
                "result" => ParseResult(root, type, sessionId, uuid),
                "rate_limit_event" => new RateLimitEvent { Type = type, SessionId = sessionId, Uuid = uuid },
                "stream_event" => ParseStreamEvent(root, type, sessionId, uuid),
                // Top-level types we acknowledge but don't act on.
                "auth_status" or "tool_progress" or "tool_use_summary" => new InformationalEvent
                {
                    Type = type,
                    SessionId = sessionId,
                    Uuid = uuid,
                    Subtype = Str(root, "subtype"),
                },
                _ => new UnknownEvent { Type = type, SessionId = sessionId, Uuid = uuid, Raw = line },
            };
        }
    }

    private static StreamJsonEvent ParseStreamEvent(JsonElement root, string type, string sid, string? uuid)
    {
        if (!root.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.Object)
            return new StreamDeltaEvent { Type = type, SessionId = sid, Uuid = uuid };

        var eventType = Str(ev, "type") ?? "";
        string? messageId = null;
        int? blockIndex = null;
        string? blockType = null;
        string? deltaType = null;
        string? deltaText = null;

        switch (eventType)
        {
            case "message_start":
                if (ev.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object)
                    messageId = Str(msg, "id");
                break;
            case "content_block_start":
                if (ev.TryGetProperty("index", out var idxS) && idxS.TryGetInt32(out var i1))
                    blockIndex = i1;
                if (ev.TryGetProperty("content_block", out var cb) && cb.ValueKind == JsonValueKind.Object)
                    blockType = Str(cb, "type");
                break;
            case "content_block_delta":
                if (ev.TryGetProperty("index", out var idxD) && idxD.TryGetInt32(out var i2))
                    blockIndex = i2;
                if (ev.TryGetProperty("delta", out var d) && d.ValueKind == JsonValueKind.Object)
                {
                    deltaType = Str(d, "type");
                    deltaText = Str(d, "text");
                }
                break;
            case "content_block_stop":
                if (ev.TryGetProperty("index", out var idxE) && idxE.TryGetInt32(out var i3))
                    blockIndex = i3;
                break;
                // message_delta / message_stop carry stop_reason + usage but we don't need them live.
        }

        return new StreamDeltaEvent
        {
            Type = type,
            SessionId = sid,
            Uuid = uuid,
            EventType = eventType,
            MessageId = messageId,
            BlockIndex = blockIndex,
            BlockType = blockType,
            DeltaType = deltaType,
            DeltaText = deltaText,
        };
    }

    private static StreamJsonEvent ParseSystem(JsonElement root, string type, string sid, string? uuid)
    {
        var subtype = Str(root, "subtype");
        if (subtype == "init")
        {
            return new SystemInitEvent
            {
                Type = type,
                SessionId = sid,
                Uuid = uuid,
                Model = Str(root, "model"),
                Cwd = Str(root, "cwd"),
            };
        }
        // Every other system event (compact_boundary, hook_started/progress/response, status,
        // task_*, files_persisted, …). Parsed into InformationalEvent so they don't fall
        // through to UnknownEvent. ClaudeService must not derive ClaudeSessionId from these —
        // per t3code, system/hook_* session_ids are NOT durable.
        return new InformationalEvent
        {
            Type = type,
            SessionId = sid,
            Uuid = uuid,
            Subtype = subtype,
        };
    }

    private static StreamJsonEvent ParseAssistant(JsonElement root, string type, string sid, string? uuid)
    {
        string messageId = "", model = "";
        string? stopReason = null;
        var content = Array.Empty<ContentBlock>();

        if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object)
        {
            messageId = Str(msg, "id") ?? "";
            model = Str(msg, "model") ?? "";
            stopReason = Str(msg, "stop_reason");
            if (msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array)
                content = ParseContentBlocks(c);
        }

        return new AssistantEvent
        {
            Type = type,
            SessionId = sid,
            Uuid = uuid,
            MessageId = messageId,
            Model = model,
            Content = content,
            StopReason = stopReason,
        };
    }

    private static StreamJsonEvent ParseUser(JsonElement root, string type, string sid, string? uuid)
    {
        var content = Array.Empty<ContentBlock>();
        if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object)
        {
            if (msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array)
                content = ParseContentBlocks(c);
        }
        return new UserEvent { Type = type, SessionId = sid, Uuid = uuid, Content = content };
    }

    private static StreamJsonEvent ParseResult(JsonElement root, string type, string sid, string? uuid)
    {
        return new ResultEvent
        {
            Type = type,
            SessionId = sid,
            Uuid = uuid,
            Subtype = Str(root, "subtype"),
            IsError = root.TryGetProperty("is_error", out var err) && err.ValueKind == JsonValueKind.True,
            Result = Str(root, "result"),
            DurationMs = root.TryGetProperty("duration_ms", out var dur) && dur.TryGetInt64(out var d) ? d : 0,
            StopReason = Str(root, "stop_reason"),
            TotalCostUsd = root.TryGetProperty("total_cost_usd", out var cost) && cost.TryGetDouble(out var c) ? c : null,
        };
    }

    private static ContentBlock[] ParseContentBlocks(JsonElement array)
    {
        var list = new List<ContentBlock>(array.GetArrayLength());
        foreach (var el in array.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var kind = Str(el, "type") ?? "";
            switch (kind)
            {
                case "text":
                    list.Add(new TextContent { Type = kind, Text = Str(el, "text") ?? "" });
                    break;
                case "tool_use":
                    list.Add(new ToolUseContent
                    {
                        Type = kind,
                        Id = Str(el, "id") ?? "",
                        Name = Str(el, "name") ?? "",
                        InputJson = el.TryGetProperty("input", out var input)
                            ? input.GetRawText()
                            : "{}",
                    });
                    break;
                case "tool_result":
                    list.Add(new ToolResultContent
                    {
                        Type = kind,
                        ToolUseId = Str(el, "tool_use_id") ?? "",
                        Content = FlattenResultContent(el),
                        IsError = el.TryGetProperty("is_error", out var ie) && ie.ValueKind == JsonValueKind.True,
                    });
                    break;
                    // thinking, redacted_thinking, etc. — skip for now.
            }
        }
        return list.ToArray();
    }

    // Tool results come back as either a string or an array of content blocks (with text
    // or image blocks inside). Flatten to a single string.
    private static string FlattenResultContent(JsonElement el)
    {
        if (!el.TryGetProperty("content", out var c)) return "";
        return c.ValueKind switch
        {
            JsonValueKind.String => c.GetString() ?? "",
            JsonValueKind.Array => string.Concat(c.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object && Str(x, "type") == "text")
                .Select(x => Str(x, "text") ?? "")),
            _ => "",
        };
    }

    private static string? Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
