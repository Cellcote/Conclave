using System.Text;
using System.Text.Json;
using Conclave.App.Sessions;
using Conclave.App.ViewModels;

namespace Conclave.App.Claude;

// Orchestrates a single claude turn for a session. Appends the user message, runs the client,
// and translates streamed events into TranscriptMessageVm + ToolCallVm mutations.
// Must be invoked from the UI thread so ObservableCollection mutations stay safe.
public sealed class ClaudeService
{
    private readonly ClaudeClient _client = new();
    private readonly SessionManager _manager;

    public ClaudeService(SessionManager manager) => _manager = manager;

    public async Task RunTurnAsync(SessionVm session, string prompt, CancellationToken ct = default)
    {
        // Per-session cancellation. Combined so either the caller's token or the Cancel
        // button on the SessionVm kills the turn.
        using var internalCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, internalCts.Token);
        session.CancellationSource = internalCts;

        // 1. Append + persist the user message.
        var userMsg = new TranscriptMessageVm
        {
            Id = Guid.NewGuid().ToString("N"),
            Tokens = session.Tokens,
            Role = MessageRole.User,
            Time = Now(),
            Content = prompt,
        };
        session.AppendTranscript(userMsg);
        _manager.PersistMessage(session, userMsg);

        Log(session, LogLevel.Inf, $"Turn started (model={session.Model}, branch={session.Branch})");
        _manager.UpdateStatus(session, SessionStatus.Working);

        // Let the dispatcher render the user's bubble + Working status before we spawn the
        // claude subprocess. Otherwise the UI thread runs straight into Process.Start and
        // doesn't yield until claude produces its first stdout line — which can be 10–15s on
        // a cold start, leaving the user staring at an empty composer the whole time.
        await Task.Yield();

        // Track tool_use -> VM so the matching tool_result (delivered as a user event later
        // in the stream) can update the VM's status + meta. Also track owning-message so we
        // can re-persist when the tool result comes back.
        var toolsById = new Dictionary<string, ToolCallVm>();
        var messageByToolId = new Dictionary<string, TranscriptMessageVm>();

        // Live assistant messages built from stream_event deltas, keyed by claude's message id.
        // The final `assistant` event is still authoritative; we reuse the live VM on match.
        var liveByMessageId = new Dictionary<string, TranscriptMessageVm>();

        try
        {
            var modelAlias = ClaudeClient.ModelAliasFromDisplay(session.Model);
            await foreach (var ev in _client.StreamAsync(
                session.Worktree,
                prompt,
                session.ClaudeSessionId,
                modelAlias,
                permissionMode: session.PermissionMode,
                includePartialMessages: true,
                forkFromSessionId: session.PendingForkFromClaudeSessionId,
                ct: linked.Token))
            {
                Handle(session, ev, toolsById, messageByToolId, liveByMessageId);
            }
        }
        catch (OperationCanceledException)
        {
            // User cancelled — not an error. Flip status to Idle; do not write an
            // "[error]" transcript entry.
            Log(session, LogLevel.Wrn, "Turn cancelled by user");
            _manager.UpdateStatus(session, SessionStatus.Idle);
        }
        catch (Exception ex)
        {
            var errMsg = new TranscriptMessageVm
            {
                Id = Guid.NewGuid().ToString("N"),
                Tokens = session.Tokens,
                Role = MessageRole.Assistant,
                Time = Now(),
                Content = $"[error] {ex.Message}",
            };
            session.AppendTranscript(errMsg);
            _manager.PersistMessage(session, errMsg);
            Log(session, LogLevel.Err, ex.Message);
            _manager.UpdateStatus(session, SessionStatus.Error);
        }
        finally
        {
            session.CancellationSource = null;
        }
    }

    private static void Log(SessionVm session, LogLevel level, string message) =>
        session.AppendLog(new LogLineVm
        {
            Tokens = session.Tokens,
            Level = level,
            Message = message,
            TimestampUtc = DateTime.UtcNow,
        });

    private void Handle(
        SessionVm session,
        StreamJsonEvent ev,
        Dictionary<string, ToolCallVm> toolsById,
        Dictionary<string, TranscriptMessageVm> messageByToolId,
        Dictionary<string, TranscriptMessageVm> liveByMessageId)
    {
        switch (ev)
        {
            case SystemInitEvent init:
                if (string.IsNullOrEmpty(session.ClaudeSessionId) && !string.IsNullOrEmpty(init.SessionId))
                {
                    _manager.UpdateClaudeSessionId(session, init.SessionId);
                    // Once claude has minted our own session id, the fork is durable and
                    // future turns should --resume it directly, not re-fork from the source.
                    session.PendingForkFromClaudeSessionId = null;
                }
                break;

            case StreamDeltaEvent delta:
                HandleStreamDelta(session, delta, liveByMessageId);
                break;

            case AssistantEvent asst:
                AppendAssistantMessage(session, asst, toolsById, messageByToolId, liveByMessageId);
                break;

            case UserEvent user:
                var touched = new HashSet<TranscriptMessageVm>();
                foreach (var block in user.Content)
                {
                    if (block is ToolResultContent tr && toolsById.TryGetValue(tr.ToolUseId, out var vm))
                    {
                        vm.Status = tr.IsError ? ToolStatus.Fail : ToolStatus.Ok;
                        vm.Meta = MetaFromResult(vm.Kind, tr.Content);
                        if (messageByToolId.TryGetValue(tr.ToolUseId, out var owner))
                            touched.Add(owner);
                    }
                }
                // Persist the tool-result updates against whatever messages own them.
                foreach (var m in touched) _manager.UpdateMessageTools(m);
                // After a tool result we're back to thinking about the next step.
                _manager.UpdateStatus(session, SessionStatus.Working);
                break;

            case ResultEvent res:
                var finalStatus = res.IsError && !IsInterrupt(res)
                    ? SessionStatus.Error
                    : SessionStatus.Idle;
                var ms = res.DurationMs > 0 ? $" · {res.DurationMs}ms" : "";
                var cost = res.TotalCostUsd is { } c ? $" · ${c:0.0000}" : "";
                Log(session, finalStatus == SessionStatus.Error ? LogLevel.Err : LogLevel.Inf,
                    $"Turn completed ({res.StopReason ?? "unknown"}){ms}{cost}");
                _manager.UpdateStatus(session, finalStatus);
                // Accumulate per-session cost — ResultEvent gives us the per-turn USD total.
                if (res.TotalCostUsd is { } turnCost) _manager.AddCost(session, turnCost);
                // Claude may have edited files or created/updated a PR during the turn.
                _manager.RefreshDiff(session);
                _manager.RefreshPr(session);
                break;

            // System hook events and other informational types carry session_ids that
            // aren't durable — do not update ClaudeSessionId from them. StreamDeltaEvent
            // will matter once we enable --include-partial-messages; for now, just log.
            case InformationalEvent info:
                if (!string.IsNullOrEmpty(info.Subtype))
                    Log(session, LogLevel.Dbg, $"{info.Type}:{info.Subtype}");
                break;
            case RateLimitEvent:
                break;
        }
    }

    private void HandleStreamDelta(
        SessionVm session,
        StreamDeltaEvent delta,
        Dictionary<string, TranscriptMessageVm> liveByMessageId)
    {
        switch (delta.EventType)
        {
            case "message_start":
                if (!string.IsNullOrEmpty(delta.MessageId) && !liveByMessageId.ContainsKey(delta.MessageId))
                {
                    var live = new TranscriptMessageVm
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Tokens = session.Tokens,
                        Role = MessageRole.Assistant,
                        Time = Now(),
                    };
                    session.AppendTranscript(live);
                    _manager.PersistMessage(session, live);
                    liveByMessageId[delta.MessageId] = live;
                }
                break;

            case "content_block_delta":
                // Only text deltas stream live; tool_use input_json_delta would require assembling
                // partial JSON and we'd rather wait for the final assistant event for those.
                if (delta.DeltaType == "text_delta" && !string.IsNullOrEmpty(delta.DeltaText))
                {
                    var live = liveByMessageId.Values.LastOrDefault();
                    if (live is not null) live.Content += delta.DeltaText;
                }
                break;

            // content_block_start / stop / message_delta / message_stop: no-op. The final
            // `assistant` event carries authoritative state.
        }
    }

    // "error_during_execution" with matching interrupt markers means the user stopped the
    // turn, not that claude failed. Don't turn the dot red for this.
    private static bool IsInterrupt(ResultEvent res)
    {
        if (res.Subtype != "error_during_execution") return false;
        var text = (res.Result ?? "").ToLowerInvariant();
        return text.Contains("interrupt") || text.Contains("aborted") || text.Contains("request was aborted");
    }

    private void AppendAssistantMessage(
        SessionVm session,
        AssistantEvent asst,
        Dictionary<string, ToolCallVm> toolsById,
        Dictionary<string, TranscriptMessageVm> messageByToolId,
        Dictionary<string, TranscriptMessageVm> liveByMessageId)
    {
        var buf = new StringBuilder();

        // If the stream delta pathway built a live message for this claude message_id, use
        // it as the target (avoid duplicating a row). Otherwise create fresh.
        TranscriptMessageVm message;
        bool isLiveMessage = !string.IsNullOrEmpty(asst.MessageId)
            && liveByMessageId.TryGetValue(asst.MessageId, out var liveRef);
        if (isLiveMessage)
        {
            message = liveByMessageId[asst.MessageId];
            liveByMessageId.Remove(asst.MessageId);
        }
        else
        {
            message = new TranscriptMessageVm
            {
                Id = Guid.NewGuid().ToString("N"),
                Tokens = session.Tokens,
                Role = MessageRole.Assistant,
                Time = Now(),
            };
        }
        bool hasToolUse = false;

        foreach (var block in asst.Content)
        {
            switch (block)
            {
                case TextContent t:
                    if (buf.Length > 0) buf.Append('\n');
                    buf.Append(t.Text);
                    break;
                case ToolUseContent tu:
                    hasToolUse = true;
                    var vm = new ToolCallVm
                    {
                        Tokens = session.Tokens,
                        Kind = MapKind(tu.Name),
                        Target = TargetFromInput(tu.Name, tu.InputJson, session.Worktree),
                        Status = ToolStatus.Pending,
                    };
                    message.Tools.Add(vm);
                    toolsById[tu.Id] = vm;
                    messageByToolId[tu.Id] = message;
                    if (tu.Name == "TodoWrite") UpdatePlanFromTodoWrite(session, tu.InputJson);
                    break;
            }
        }

        // For live messages, the deltas have already populated Content — use the authoritative
        // text from the final event to correct any race / missed delta. For fresh messages,
        // this is the initial set.
        message.Content = buf.ToString();

        // Claude sometimes emits an assistant event with only tool calls and no text, or only
        // text and no tools. Only add the message if it has something to show.
        if (!string.IsNullOrEmpty(message.Content) || message.Tools.Count > 0)
        {
            if (isLiveMessage)
            {
                // Live path: row already inserted on message_start; update it.
                _manager.UpdateMessageTools(message);
            }
            else
            {
                session.AppendTranscript(message);
                _manager.PersistMessage(session, message);
            }
        }

        if (hasToolUse)
            _manager.UpdateStatus(session, SessionStatus.RunningTool);
    }

    private void UpdatePlanFromTodoWrite(SessionVm session, string inputJson)
    {
        try
        {
            var items = SessionManager.ParsePlanJson(inputJson, session.Tokens);
            session.ReplacePlan(items);
            _manager.PersistPlan(session, inputJson);
        }
        catch (JsonException)
        {
            // Malformed input — ignore; next TodoWrite will overwrite.
        }
    }

    private static string MapKind(string toolName) => toolName switch
    {
        "Read" => "READ",
        "Write" => "WRITE",
        "Edit" => "EDIT",
        "Bash" => "BASH",
        "Glob" => "GLOB",
        "Grep" => "GREP",
        "WebSearch" => "WEB",
        "WebFetch" => "FETCH",
        "TodoWrite" => "TODO",
        "Task" => "TASK",
        // Special interactive tools that the SDK would normally surface via canUseTool.
        // We render them with distinct kinds so the user can spot them in the transcript.
        "AskUserQuestion" => "ASK",
        "ExitPlanMode" => "PLAN",
        "EnterPlanMode" => "PLAN",
        _ => toolName.ToUpperInvariant(),
    };

    private static string TargetFromInput(string toolName, string inputJson, string? worktreeRoot = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            var root = doc.RootElement;
            return toolName switch
            {
                "Read" or "Write" or "Edit" => RelativeToWorktree(StringProp(root, "file_path"), worktreeRoot),
                "Bash" => StringProp(root, "command"),
                "Glob" => StringProp(root, "pattern"),
                "Grep" => StringProp(root, "pattern"),
                "WebSearch" => StringProp(root, "query"),
                "WebFetch" => StringProp(root, "url"),
                "TodoWrite" => "todo list updated",
                "Task" => StringProp(root, "description"),
                "AskUserQuestion" => FirstQuestionText(root),
                "EnterPlanMode" or "ExitPlanMode" => FirstLineOfPlan(root),
                _ => "",
            };
        }
        catch (JsonException) { return ""; }
    }

    // AskUserQuestion input: { questions: [{ question, options: [...] }] }. We surface the
    // first question's text so the user can see what claude is asking without expanding.
    private static string FirstQuestionText(JsonElement root)
    {
        if (!root.TryGetProperty("questions", out var qs)) return "user input requested";
        if (qs.ValueKind != JsonValueKind.Array || qs.GetArrayLength() == 0) return "user input requested";
        var first = qs[0];
        return first.ValueKind == JsonValueKind.Object && first.TryGetProperty("question", out var q)
            && q.ValueKind == JsonValueKind.String
            ? q.GetString() ?? "user input requested"
            : "user input requested";
    }

    // EnterPlanMode/ExitPlanMode input has a "plan" string (markdown). Show the first
    // non-empty line so it's a hint, not the whole plan.
    private static string FirstLineOfPlan(JsonElement root)
    {
        if (!root.TryGetProperty("plan", out var p) || p.ValueKind != JsonValueKind.String) return "";
        var text = p.GetString() ?? "";
        var firstLine = text.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        // Strip leading markdown markers (`#`, `-`, `*`) so the kernel of the line shows.
        firstLine = firstLine.TrimStart('#', '-', '*', ' ');
        return firstLine;
    }

    // If the file path is inside the session's worktree, strip the prefix so the transcript
    // shows "Views/Shell/RightPanel.axaml" instead of the full
    // "/Users/.../Conclave/worktrees/<id>/<slug>/Views/Shell/RightPanel.axaml" mess.
    private static string RelativeToWorktree(string path, string? worktreeRoot)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(worktreeRoot)) return path;
        var root = worktreeRoot.TrimEnd('/', '\\');
        if (path.Length > root.Length + 1
            && path.StartsWith(root, StringComparison.Ordinal)
            && (path[root.Length] == '/' || path[root.Length] == '\\'))
        {
            return path[(root.Length + 1)..];
        }
        return path;
    }

    private static string StringProp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static string MetaFromResult(string kind, string result)
    {
        if (string.IsNullOrWhiteSpace(result)) return "";

        switch (kind)
        {
            case "READ":
                // Read results are line-numbered ("   1\tline"). Newline count == line count.
                var readLines = result.Count(c => c == '\n');
                return readLines > 0 ? $"{readLines} lines" : "";

            case "GLOB":
            case "GREP":
                // One match per non-blank line.
                var matchCount = result.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));
                if (matchCount == 0) return "no matches";
                return matchCount == 1 ? "1 match" : $"{matchCount} matches";

            case "WRITE":
            case "EDIT":
                // Claude wraps successful file ops in a verbose preamble; collapse to "ok".
                if (result.Contains("has been updated", StringComparison.Ordinal)
                    || result.Contains("was created", StringComparison.Ordinal)
                    || result.Contains("file created", StringComparison.OrdinalIgnoreCase))
                    return "ok";
                return TruncateFirstLine(result);

            case "BASH":
            default:
                return TruncateFirstLine(result);
        }
    }

    private static string TruncateFirstLine(string result)
    {
        var first = result.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        return first.Length > 40 ? first[..40] + "…" : first;
    }

    private static string Now() => DateTime.Now.ToString("HH:mm");
}
