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

    public Task RunTurnAsync(SessionVm session, string prompt, CancellationToken ct = default)
        => RunTurnAsync(session, prompt, isAutoResume: false, ct);

    public async Task RunTurnAsync(SessionVm session, string prompt, bool isAutoResume, CancellationToken ct = default)
    {
        // Per-session cancellation. Combined so either the caller's token or the Cancel
        // button on the SessionVm kills the turn.
        using var internalCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, internalCts.Token);
        session.CancellationSource = internalCts;
        // Reset the stall timestamp at turn start so a stalled prior turn doesn't immediately
        // re-flag this fresh one before any events have had time to arrive. StallDetectionService
        // also clears IsStalled when it sees a turn enter Working — but stamping here closes
        // the race where the timer fires between RunTurnAsync's UpdateStatus(Working) and the
        // first stream event.
        session.LastStreamEventAt = DateTime.UtcNow;
        session.IsStalled = false;

        // Per-turn permission router. Registered with the shared MCP server so claude
        // can call our permission_prompt tool over HTTP; cancelled in finally so a
        // pending approval doesn't leak past the turn that owns it.
        PermissionTurnHandler? permHandler = null;
        string? permToken = null;
        string? mcpConfigJson = null;
        string? permissionPromptTool = null;
        string? settingsJson = null;
        if (_manager.Permissions is { } mcpServer && session.PermissionMode != PermissionModes.BypassPermissions)
        {
            permHandler = new PermissionTurnHandler();
            permToken = mcpServer.RegisterHandler(permHandler.HandleAsync);
            mcpConfigJson = mcpServer.BuildMcpConfigJson(permToken);
            permissionPromptTool = "mcp__conclave__permission_prompt";
            settingsJson = BuildAskSettingsJson(session.PermissionMode);
        }

        // 1. Append + persist the user message.
        var userMsg = new TranscriptMessageVm
        {
            Id = Guid.NewGuid().ToString("N"),
            Tokens = session.Tokens,
            Role = MessageRole.User,
            Time = Now(),
            Content = prompt,
            IsAutoResume = isAutoResume,
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
        // Each entry carries a StringBuilder so a long response with hundreds of small text
        // deltas doesn't quadratically copy the growing message via `live.Content += ...`.
        var liveByMessageId = new Dictionary<string, LiveAssistantState>();

        // Capture once at turn start — any clear that happens mid-stream (or the next turn's
        // clear) should not affect this in-flight invocation's args.
        var preamble = session.PendingPreamble;

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
                appendSystemPrompt: preamble,
                additionalDirs: session.AdditionalDirs.Count > 0 ? session.AdditionalDirs : null,
                mcpConfigJson: mcpConfigJson,
                permissionPromptTool: permissionPromptTool,
                settingsJson: settingsJson,
                ct: linked.Token))
            {
                Handle(session, ev, toolsById, messageByToolId, liveByMessageId, permHandler);
            }
            // The stream completed without throwing — claude has consumed the preamble (it
            // was injected into the system prompt of this invocation). Clear it so future
            // turns don't keep re-injecting (which would just waste tokens).
            if (preamble is not null)
                _manager.ClearPendingPreamble(session);
        }
        catch (OperationCanceledException)
        {
            // User cancelled — not an error. Flip status to Idle; do not write an
            // "[error]" transcript entry. When StallDetectionService is the canceller
            // (auto-resume case) it sets SuppressNextTurnCompleteNotification so the
            // user doesn't see a "turn complete" toast for what's internally a restart.
            var suppress = session.SuppressNextTurnCompleteNotification;
            session.SuppressNextTurnCompleteNotification = false;
            Log(session, LogLevel.Wrn, suppress
                ? "Turn cancelled for auto-resume after stall"
                : "Turn cancelled by user");
            _manager.UpdateStatus(session, SessionStatus.Idle, suppressNotification: suppress);
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
            session.CurrentTurnTask = null;
            // Defensive cleanup: if the turn ended via a real ResultEvent rather than the
            // OCE catch (e.g. the network recovered mid-cancel), the OCE handler never ran
            // and the auto-resume flag is still set. Clearing here means the next legit
            // turn-complete won't get its notification suppressed by a stale flag.
            session.SuppressNextTurnCompleteNotification = false;
            // Release any in-flight permission prompts so the MCP handler unwinds and
            // claude doesn't sit on an orphaned request.
            permHandler?.CancelAll();
            if (permToken is not null) _manager.Permissions?.UnregisterHandler(permToken);
        }
    }

    // Inject `permissions.ask` rules so the CLI routes gated tool calls through our MCP
    // permission_prompt instead of silently auto-allowing in --print mode. Edit/Write/
    // NotebookEdit drop out of the list under acceptEdits — the CLI's built-in mode
    // handles those, so we'd just be re-asking for things the user already opted out of.
    // Read-only tools (Read, Glob, Grep) are never gated; they don't mutate anything.
    private static string BuildAskSettingsJson(string permissionMode) => permissionMode switch
    {
        PermissionModes.AcceptEdits =>
            "{\"permissions\":{\"ask\":[\"Bash\",\"WebFetch\",\"WebSearch\",\"Task\"]}}",
        _ =>
            "{\"permissions\":{\"ask\":[\"Bash\",\"Edit\",\"Write\",\"NotebookEdit\",\"WebFetch\",\"WebSearch\",\"Task\"]}}",
    };

    private static void Log(SessionVm session, LogLevel level, string message) =>
        session.AppendLog(new LogLineVm
        {
            Tokens = session.Tokens,
            Level = level,
            Message = message,
            TimestampUtc = DateTime.UtcNow,
        });

    // Per-live-message scratch state. Buffer accumulates text_delta payloads cheaply;
    // VM.Content is only refreshed periodically (see HandleStreamDelta) so a long response
    // with thousands of deltas doesn't allocate a full new string per delta.
    private sealed class LiveAssistantState
    {
        public TranscriptMessageVm Vm { get; }
        public StringBuilder Buffer { get; } = new();
        public DateTime LastFlushUtc;
        public LiveAssistantState(TranscriptMessageVm vm) => Vm = vm;
    }

    private void Handle(
        SessionVm session,
        StreamJsonEvent ev,
        Dictionary<string, ToolCallVm> toolsById,
        Dictionary<string, TranscriptMessageVm> messageByToolId,
        Dictionary<string, LiveAssistantState> liveByMessageId,
        PermissionTurnHandler? permHandler)
    {
        // StallDetectionService checks how long ago the last event was — stamp on every
        // type so a long-running tool (no text deltas for minutes) doesn't false-positive.
        // Clearing IsStalled here also lets a session that briefly stalled then recovered
        // drop out of needs-attention without manual intervention.
        session.LastStreamEventAt = DateTime.UtcNow;
        if (session.IsStalled) session.IsStalled = false;

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
                AppendAssistantMessage(session, asst, toolsById, messageByToolId, liveByMessageId, permHandler);
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
                // Record the result-arrival time before flipping status so StallDetectionService
                // can detect the "result landed mid-cancel" race and skip a piggyback auto-resume.
                session.LastResultEventAt = DateTime.UtcNow;
                // A clean turn-complete resets the auto-resume retry budget so the next stall
                // gets the same one-shot it would have on a fresh session.
                session.AutoResumeAttempts = 0;
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

    // Min interval between Content flushes for a streaming assistant message. ~30Hz feels
    // smooth for streaming text but spares us a full-string ToString allocation per delta —
    // for long responses (hundreds–thousands of small deltas) that adds up.
    private static readonly TimeSpan ContentFlushInterval = TimeSpan.FromMilliseconds(33);

    private void HandleStreamDelta(
        SessionVm session,
        StreamDeltaEvent delta,
        Dictionary<string, LiveAssistantState> liveByMessageId)
    {
        switch (delta.EventType)
        {
            case "message_start":
                // Append a live VM to the transcript so deltas stream into a visible bubble,
                // but do NOT persist yet — at this point Content is empty and Tools are
                // empty. If the turn is killed mid-stream we'd leave a blank row in the DB
                // that paints as an empty assistant bubble on reload. The AssistantEvent
                // path inserts the row once content/tools are real.
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
                    liveByMessageId[delta.MessageId] = new LiveAssistantState(live);
                }
                break;

            case "content_block_delta":
                // Only text deltas stream live; tool_use input_json_delta would require assembling
                // partial JSON and we'd rather wait for the final assistant event for those.
                if (delta.DeltaType == "text_delta" && !string.IsNullOrEmpty(delta.DeltaText))
                {
                    var state = liveByMessageId.Values.LastOrDefault();
                    if (state is not null)
                    {
                        state.Buffer.Append(delta.DeltaText);
                        var now = DateTime.UtcNow;
                        if (now - state.LastFlushUtc >= ContentFlushInterval)
                        {
                            state.Vm.Content = state.Buffer.ToString();
                            state.LastFlushUtc = now;
                        }
                    }
                }
                break;

            case "content_block_stop":
                // Flush any pending text accumulated since the last throttled update so the
                // bubble shows the full block before the next one starts (or the AssistantEvent
                // overwrites with the authoritative content). Unconditionally assign — the
                // Set guard in Observable filters no-op writes when content matches.
                {
                    var state = liveByMessageId.Values.LastOrDefault();
                    if (state is not null && state.Buffer.Length > 0)
                    {
                        state.Vm.Content = state.Buffer.ToString();
                        state.LastFlushUtc = DateTime.UtcNow;
                    }
                }
                break;

                // message_delta / message_stop: no-op. The final `assistant` event carries
                // authoritative state.
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
        Dictionary<string, LiveAssistantState> liveByMessageId,
        PermissionTurnHandler? permHandler)
    {
        var buf = new StringBuilder();

        // If the stream delta pathway built a live message for this claude message_id, use
        // it as the target (avoid duplicating a row). Otherwise create fresh.
        TranscriptMessageVm message;
        bool isLiveMessage = !string.IsNullOrEmpty(asst.MessageId)
            && liveByMessageId.ContainsKey(asst.MessageId);
        if (isLiveMessage)
        {
            message = liveByMessageId[asst.MessageId].Vm;
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
                    // TodoWrite is mirrored into the right-hand plan panel — rendering a
                    // pill in the transcript too is just noise, so update the plan and skip.
                    if (tu.Name == "TodoWrite")
                    {
                        UpdatePlanFromTodoWrite(session, tu.InputJson);
                        break;
                    }
                    // Task (subagent) calls don't carry useful per-pill state — the agent's
                    // summary lands in the next assistant message anyway. Skip the pill.
                    if (tu.Name == "Task") break;
                    var vm = new ToolCallVm
                    {
                        Tokens = session.Tokens,
                        Kind = MapKind(tu.Name),
                        Target = TargetFromInput(tu.Name, tu.InputJson, session.Worktree),
                        Status = ToolStatus.Pending,
                        ToolUseId = tu.Id,
                        PermissionHandler = permHandler,
                    };
                    message.Tools.Add(vm);
                    toolsById[tu.Id] = vm;
                    messageByToolId[tu.Id] = message;
                    // Register the VM with the permission handler so when claude calls
                    // permission_prompt for this tool_use_id, the handler knows which
                    // pill to flip to PendingApproval.
                    permHandler?.NoteToolUse(tu.Id, vm);
                    // AskUserQuestion + ExitPlanMode are interactive — claude is waiting on
                    // the user. EnterPlanMode is just claude announcing it switched modes,
                    // not a question, so it stays out of this branch. Fire both a native
                    // notification (so the user is pulled back to the window) and a session
                    // log warning (so the persistent record explains why the session is stuck
                    // and how to recover).
                    if (tu.Name is "AskUserQuestion" or "ExitPlanMode")
                    {
                        _manager.Notifications?.NotifyQuestionPending(session.Title, vm.Target);
                        WarnUnhandledInteractiveTool(session, tu.Name);
                    }
                    break;
            }
        }

        // For live messages, the deltas have already populated Content — use the authoritative
        // text from the final event to correct any race / missed delta. For fresh messages,
        // this is the initial set.
        message.Content = buf.ToString();
        // Capture claude's per-event uuid so future fork-at-message paths can map our row
        // back to claude's JSONL event chain. AssistantEvent carries the final, authoritative
        // uuid for the logical message — message_start deltas have their own (different) uuids.
        if (!string.IsNullOrEmpty(asst.Uuid)) message.ClaudeUuid = asst.Uuid;

        // Claude sometimes emits an assistant event with only tool calls and no text, or only
        // text and no tools. Only add the message if it has something to show.
        if (!string.IsNullOrEmpty(message.Content) || message.Tools.Count > 0)
        {
            if (!isLiveMessage)
            {
                session.AppendTranscript(message);
            }
            // Insert lazily — message_start no longer persists. The VM is already in the
            // transcript (live path appended on message_start; fresh path appended just
            // above), so the only DB operation needed is the insert.
            _manager.PersistMessage(session, message);
        }

        if (hasToolUse)
            _manager.UpdateStatus(session, SessionStatus.RunningTool);
    }

    // AskUserQuestion and ExitPlanMode are interactive tools that the Agent SDK normally
    // satisfies via a `canUseTool` callback. Our CLI-based integration has no such hook —
    // claude emits the tool_use, the user sees it in the transcript, and then the model
    // sits there forever waiting for a tool_result we cannot supply. Surface a warning in
    // the session log so the user understands why the session looks frozen and what to do.
    // Tracked under "Permission handling — option 1 shipped" in PHASE_4.md.
    private static void WarnUnhandledInteractiveTool(SessionVm session, string toolName) =>
        Log(session, LogLevel.Wrn,
            $"{toolName} invoked but Conclave's CLI integration can't answer it. " +
            "Claude will wait for a response that won't arrive — cancel the turn and rephrase, " +
            "or set permission mode to 'Full access' so the model doesn't gate on it.");

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
