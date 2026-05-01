using System.Collections.ObjectModel;
using Avalonia.Media;
using Conclave.App.Design;

namespace Conclave.App.ViewModels;

public sealed class SessionVm : Views.Observable
{
    public Tokens Tokens { get; }

    public string Id { get; init; } = "";
    public string Worktree { get; init; } = "";
    public string Branch { get; init; } = "";
    public string BaseBranch { get; init; } = "main";
    public string Model { get; init; } = "";

    // Secondary worktree paths for fusion sessions, passed to claude as `--add-dir <path>`.
    // Empty for repo-kind sessions.
    public IReadOnlyList<string> AdditionalDirs { get; init; } = Array.Empty<string>();
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;
    public int Pid { get; init; }
    public DiffStatVm Diff { get; init; } = new();

    private PullRequestVm? _pr;
    public PullRequestVm? Pr
    {
        get => _pr;
        set
        {
            if (Set(ref _pr, value))
            {
                Notify(nameof(HasPr));
                Notify(nameof(PrText));
            }
        }
    }

    private string? _claudeSessionId;
    // Claude-side session UUID from the system/init event. Null until the first turn completes.
    // Passed back to claude via --resume on subsequent turns.
    public string? ClaudeSessionId { get => _claudeSessionId; set => Set(ref _claudeSessionId, value); }

    // Set on a freshly-forked session so its first turn passes `--resume <source> --fork-session`,
    // which makes claude branch a new server-side session that retains the source's context.
    // Cleared as soon as ClaudeSessionId is populated from the first SystemInitEvent. Transient
    // (not persisted): on app restart a partial fork either succeeded — ClaudeSessionId is set —
    // or it didn't, in which case the next turn just starts fresh, and the model can still read
    // the copied transcript from disk if needed via tools.
    public string? PendingForkFromClaudeSessionId { get; set; }

    // System-prompt context to inject on the next turn via `--append-system-prompt`. Mirrors
    // sessions.pending_preamble — populated for fork-at-message sessions, cleared after the
    // first turn that consumes it.
    private string? _pendingPreamble;
    public string? PendingPreamble { get => _pendingPreamble; set => Set(ref _pendingPreamble, value); }

    private string _title = "";
    public string Title { get => _title; set => Set(ref _title, value); }

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (Set(ref _isEditing, value) && value) EditingName = _title;
        }
    }

    private string _editingName = "";
    public string EditingName { get => _editingName; set => Set(ref _editingName, value); }

    private SessionStatus _status;
    public SessionStatus Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                Notify(nameof(StatusLabel));
                Notify(nameof(StatusPulses));
                Notify(nameof(StatusColor));
                Notify(nameof(IsBusy));
                Notify(nameof(IsBusyAndNotStalled));
                Notify(nameof(CanSend));
            }
        }
    }

    private int _unread;
    public int Unread { get => _unread; set => Set(ref _unread, value); }

    public string StatusLabel => Status.Label();
    public bool StatusPulses => Status.Pulses();
    public bool IsBusy => Status is SessionStatus.Working or SessionStatus.RunningTool;
    // The "Claude is thinking..." throbber should hide while a session is stalled — the
    // stalled banner takes its slot. Exposed as a single boolean so the XAML doesn't need
    // a MultiBinding (compiled bindings on bool conjunctions are awkward in Avalonia).
    public bool IsBusyAndNotStalled => IsBusy && !_isStalled;

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (Set(ref _isActive, value))
            {
                Notify(nameof(BackgroundBrush));
                Notify(nameof(TitleBrush));
                Notify(nameof(TitleFontWeight));
            }
        }
    }

    // Driven by ShellVm.ApplyFilter — false hides the row in the sidebar.
    private bool _isVisibleInTree = true;
    public bool IsVisibleInTree { get => _isVisibleInTree; set => Set(ref _isVisibleInTree, value); }

    // False until SessionManager has fetched this session's messages from the DB. Flipped
    // true the first time the session is activated; avoids loading transcripts for sessions
    // the user never opens.
    public bool TranscriptLoaded { get; set; }

    // Set by ClaudeService while a turn is in flight; ShellVm.Cancel() pulls the plug on it.
    public CancellationTokenSource? CancellationSource { get; set; }

    // The Task returned by ClaudeService.RunTurnAsync for the in-flight turn. Auto-resume
    // awaits this with a timeout after cancelling, so we know the previous turn has fully
    // unwound before sending "continue". Null when no turn is in flight.
    public Task? CurrentTurnTask { get; set; }

    // UTC timestamp of the most recent stream event from the claude CLI for the current turn.
    // StallDetectionService uses this to detect a stalled subprocess (e.g. after sleep/wake)
    // when Status is Working/RunningTool but no events have arrived for some time. In-memory
    // only — not persisted; on app restart the BuildSessionVm path resets in-progress turns
    // to Idle anyway.
    public DateTime LastStreamEventAt { get; set; }

    // UTC timestamp of the most recent ResultEvent (turn-complete signal). Used by
    // StallDetectionService to suppress auto-resume races: if a result lands within the
    // small window between cancellation and re-prompt, the network was clearly recovering
    // and we shouldn't pile a synthetic "continue" on top of a real completion.
    public DateTime LastResultEventAt { get; set; }

    // Number of times StallDetectionService has tried to auto-resume the current stall.
    // Capped at 1 — after a second consecutive stall the session stays in IsStalled=true
    // (needs-attention) and the user has to act. Reset to 0 on every successful turn-complete.
    public int AutoResumeAttempts { get; set; }

    // Set by StallDetectionService just before cancelling a stalled turn for auto-resume.
    // ClaudeService's OCE-handler reads this so it can pass suppressNotification=true to
    // SessionManager.UpdateStatus, preventing a spurious "turn complete" toast for what
    // is internally a restart. Cleared by ClaudeService after the cancellation is observed.
    public bool SuppressNextTurnCompleteNotification { get; set; }

    // True when StallDetectionService has flagged the session as not having received stream
    // events for the configured threshold while Status is Working/RunningTool. Surfaced as
    // "Needs attention" via the sidebar filter and a Resume button on the row. Cleared when
    // the turn either resumes (events start arriving again) or gets cancelled.
    private bool _isStalled;
    public bool IsStalled
    {
        get => _isStalled;
        set
        {
            if (Set(ref _isStalled, value)) Notify(nameof(IsBusyAndNotStalled));
        }
    }

    private string _permissionMode = PermissionModes.Default;
    public string PermissionMode
    {
        get => _permissionMode;
        set
        {
            if (Set(ref _permissionMode, value)) Notify(nameof(PermissionModeDisplay));
        }
    }
    public string PermissionModeDisplay => PermissionModes.DisplayName(_permissionMode);

    private double _totalCostUsd;
    public double TotalCostUsd
    {
        get => _totalCostUsd;
        set { if (Set(ref _totalCostUsd, value)) Notify(nameof(TotalCostFormatted)); }
    }
    public string TotalCostFormatted => $"${_totalCostUsd:0.00}";
    public bool HasCost => _totalCostUsd > 0;

    public IBrush BackgroundBrush => _isActive ? Tokens.Panel : Brushes.Transparent;
    public IBrush TitleBrush => _isActive ? Tokens.Text : Tokens.TextDim;
    public FontWeight TitleFontWeight => _isActive ? FontWeight.Medium : FontWeight.Normal;

    public IBrush StatusColor => Status switch
    {
        SessionStatus.Working => Tokens.AccentBrush,
        SessionStatus.Waiting => Tokens.Warn,
        SessionStatus.RunningTool => Tokens.Ok,
        SessionStatus.Idle => Tokens.TextMute,
        SessionStatus.Error => Tokens.Err,
        SessionStatus.Queued => Tokens.TextMute,
        SessionStatus.Completed => Tokens.Ok,
        _ => Tokens.TextMute,
    };

    public bool ShowDiff => Diff.HasChanges;
    public string AddText => $"+{Diff.Add}";
    public string DelText => $"−{Diff.Del}";

    public bool HasPr => Pr is not null;
    public string PrText => Pr is null ? "" : $"#{Pr.Number}";
    public bool HasUnread => _unread > 0;

    public string StartedFormatted => StartedUtc.ToLocalTime().ToString("HH:mm:ss");
    public string PidFormatted => Pid > 0 ? Pid.ToString() : "—";

    public ObservableCollection<TranscriptMessageVm> Transcript { get; } = new();

    // Appends a transcript message, setting ShowHeader=false when the previous message has
    // the same role. Callers should use this instead of Transcript.Add so consecutive
    // assistant chunks (text → tool → more text) don't stutter with repeated headers.
    public void AppendTranscript(TranscriptMessageVm msg)
    {
        if (Transcript.Count > 0 && Transcript[^1].Role == msg.Role)
            msg.ShowHeader = false;
        Transcript.Add(msg);
    }

    // Most-recent TodoWrite state for this session. Replaced wholesale when a new TodoWrite
    // arrives (TodoWrite is itself always a full-replace of the list). Persisted via the
    // sessions.plan_json column.
    public ObservableCollection<PlanItemVm> Plan { get; } = new();

    public bool HasPlan => Plan.Count > 0;
    public int PlanCompletedCount => Plan.Count(p => p.IsCompleted);
    public double PlanProgress => Plan.Count == 0 ? 0 : (double)PlanCompletedCount / Plan.Count;
    public string PlanHeader => Plan.Count == 0
        ? "No plan yet"
        : $"Current plan · {PlanCompletedCount} of {Plan.Count} complete";
    // Compact "X / Y" used in the right-sidebar TODOS section header.
    public string PlanSummary => Plan.Count == 0 ? "" : $"{PlanCompletedCount} / {Plan.Count}";

    public void ReplacePlan(IEnumerable<PlanItemVm> items)
    {
        Plan.Clear();
        foreach (var i in items) Plan.Add(i);
        Notify(nameof(HasPlan));
        Notify(nameof(PlanCompletedCount));
        Notify(nameof(PlanProgress));
        Notify(nameof(PlanHeader));
        Notify(nameof(PlanSummary));
    }

    // Structured session log: lifecycle events, informational stream events, errors.
    // In-memory only for now (not persisted). Bounded ring so a chatty session doesn't leak.
    public ObservableCollection<LogLineVm> Logs { get; } = new();
    private const int LogCap = 500;
    public void AppendLog(LogLineVm entry)
    {
        Logs.Add(entry);
        while (Logs.Count > LogCap) Logs.RemoveAt(0);
    }

    // --- Composer (per-session draft state) ---
    // Lives on the session — not the shell — so switching sessions preserves each one's
    // in-progress input. In-memory only; not persisted across app restarts.

    private string _composerDraft = "";
    public string ComposerDraft
    {
        get => _composerDraft;
        set { if (Set(ref _composerDraft, value)) Notify(nameof(CanSend)); }
    }

    public ObservableCollection<AttachmentVm> ComposerAttachments { get; } = new();
    public bool HasComposerAttachments => ComposerAttachments.Count > 0;

    public bool CanSend =>
        !IsBusy && (!string.IsNullOrWhiteSpace(_composerDraft) || ComposerAttachments.Count > 0);

    public void AddAttachment(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        foreach (var existing in ComposerAttachments)
            if (string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase)) return;
        ComposerAttachments.Add(new AttachmentVm(Tokens, path));
    }

    public void RemoveAttachment(AttachmentVm attachment) => ComposerAttachments.Remove(attachment);

    public SessionVm(Tokens tokens)
    {
        Tokens = tokens;
        // Rebroadcast Diff totals as own-class notifications so sidebar bindings (AddText,
        // DelText, ShowDiff) update when RefreshDiff mutates the underlying DiffStatVm.
        Diff.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(DiffStatVm.Files): Notify(nameof(ShowDiff)); break;
                case nameof(DiffStatVm.Add): Notify(nameof(AddText)); break;
                case nameof(DiffStatVm.Del): Notify(nameof(DelText)); break;
            }
        };
        ComposerAttachments.CollectionChanged += (_, _) =>
        {
            Notify(nameof(HasComposerAttachments));
            Notify(nameof(CanSend));
        };
    }
}
