using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using Conclave.App.Claude;
using Conclave.App.Design;
using Conclave.App.Sessions;

namespace Conclave.App.ViewModels;

public enum MainView { Terminal, Plan, Logs }

public sealed class ShellVm : Views.Observable
{
    public Tokens Tokens { get; }
    public SessionManager Manager { get; }
    public ClaudeCapabilities Claude { get; }

    public ObservableCollection<ProjectVm> Projects => Manager.Projects;
    public ObservableCollection<FilterVm> Filters { get; } = new();

    private SessionVm? _activeSession;
    public SessionVm? ActiveSession
    {
        get => _activeSession;
        set
        {
            var previous = _activeSession;
            if (Set(ref _activeSession, value))
            {
                if (previous is not null) previous.IsActive = false;
                if (value is not null)
                {
                    value.IsActive = true;
                    Manager.LoadTranscriptIfNeeded(value);
                    // Eager refresh for the just-activated session — the post-startup sweep
                    // staggers all sessions, but a session the user picks should reflect
                    // current diff/PR state without waiting in the sweep queue.
                    Manager.RefreshOnActivation(value);
                }
                Notify(nameof(HasActiveSession));
                Notify(nameof(ActiveProjectName));
            }
        }
    }

    public bool HasActiveSession => _activeSession is not null;

    public string ActiveProjectName
    {
        get
        {
            if (_activeSession is null) return "";
            foreach (var p in Projects)
                if (p.Sessions.Contains(_activeSession)) return p.Name;
            return "";
        }
    }

    private MainView _activeView = MainView.Terminal;
    public MainView ActiveView
    {
        get => _activeView;
        set
        {
            if (Set(ref _activeView, value))
            {
                Notify(nameof(IsTerminalView));
                Notify(nameof(IsPlanView));
                Notify(nameof(IsLogsView));
            }
        }
    }

    public bool IsTerminalView => _activeView == MainView.Terminal;
    public bool IsPlanView => _activeView == MainView.Plan;
    public bool IsLogsView => _activeView == MainView.Logs;

    private bool _rightPanelVisible = true;
    public bool RightPanelVisible { get => _rightPanelVisible; set => Set(ref _rightPanelVisible, value); }

    private NewSessionVm? _newSession;
    public NewSessionVm? NewSession
    {
        get => _newSession;
        set
        {
            if (Set(ref _newSession, value)) Notify(nameof(IsNewSessionOpen));
        }
    }
    public bool IsNewSessionOpen => _newSession is not null;

    public void OpenNewSession() => NewSession = new NewSessionVm(Tokens, Projects);
    public void OpenNewSessionForProject(ProjectVm project) => NewSession = new NewSessionVm(Tokens, Projects) { Project = project };
    public void CancelNewSession() => NewSession = null;

    private NewFusionProjectVm? _newFusion;
    public NewFusionProjectVm? NewFusion
    {
        get => _newFusion;
        set { if (Set(ref _newFusion, value)) Notify(nameof(IsNewFusionOpen)); }
    }
    public bool IsNewFusionOpen => _newFusion is not null;

    // Opens the fusion-creation modal. The new-session modal closes first because the fusion
    // modal reopens it on success with the new fusion preselected.
    public void OpenNewFusionProject()
    {
        NewSession = null;
        NewFusion = new NewFusionProjectVm(Tokens, Projects);
    }
    public void CancelNewFusionProject() => NewFusion = null;

    // --- Preferences ---

    public AutoCleanupService? AutoCleanup { get; set; }

    // Wired by MainWindow at startup. The Resume button on a stalled session row routes
    // here so the same orchestration (cancel-then-continue) is used for manual + auto.
    public StallDetectionService? StallDetection { get; set; }

    private bool _isPreferencesOpen;
    public bool IsPreferencesOpen
    {
        get => _isPreferencesOpen;
        set => Set(ref _isPreferencesOpen, value);
    }

    public void OpenPreferences() => IsPreferencesOpen = true;
    public void ClosePreferences() => IsPreferencesOpen = false;

    // --- About ---

    private bool _isAboutOpen;
    public bool IsAboutOpen
    {
        get => _isAboutOpen;
        set => Set(ref _isAboutOpen, value);
    }

    public void OpenAbout() => IsAboutOpen = true;
    public void CloseAbout() => IsAboutOpen = false;

    // Reads the assembly's InformationalVersion (stamped by `dotnet publish -p:Version=...`)
    // and trims the trailing `+commit` build metadata SourceLink appends. Falls back to the
    // numeric assembly version, then a dev sentinel.
    public string AppVersion
    {
        get
        {
            var asm = typeof(ShellVm).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
            {
                var plus = info.IndexOf('+');
                return plus >= 0 ? info[..plus] : info;
            }
            return asm.GetName().Version?.ToString() ?? "0.0.0-dev";
        }
    }

    public bool AutoCleanupEnabled
    {
        get => SettingsKeys.ReadAutoCleanupEnabled(Manager.Db);
        set
        {
            if (value == AutoCleanupEnabled) return;
            Manager.Db.SetSetting(SettingsKeys.AutoCleanupEnabled, value ? "true" : "false");
            Notify();
        }
    }

    public int AutoCleanupDays
    {
        get => SettingsKeys.ReadAutoCleanupDays(Manager.Db);
        set
        {
            var clamped = value < 1 ? 1 : value;
            if (clamped == AutoCleanupDays) return;
            Manager.Db.SetSetting(SettingsKeys.AutoCleanupDays, clamped.ToString());
            Notify();
        }
    }

    public bool NotificationsEnabled
    {
        get => SettingsKeys.ReadNotificationsEnabled(Manager.Db);
        set
        {
            if (value == NotificationsEnabled) return;
            Manager.Db.SetSetting(SettingsKeys.NotificationsEnabled, value ? "true" : "false");
            // Push the new value into the running service so the next event respects it
            // without requiring a restart.
            if (Manager.Notifications is { } svc) svc.Enabled = value;
            Notify();
        }
    }

    public bool AutoResumeStalledEnabled
    {
        get => SettingsKeys.ReadAutoResumeStalledSessions(Manager.Db);
        set
        {
            if (value == AutoResumeStalledEnabled) return;
            Manager.Db.SetSetting(SettingsKeys.AutoResumeStalledSessions, value ? "true" : "false");
            Notify();
        }
    }

    public async Task RunCleanupNowAsync()
    {
        if (AutoCleanup is null) return;
        try { await AutoCleanup.RunOnceAsync(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    // --- Search ---

    private string _searchQuery = "";
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (Set(ref _searchQuery, value))
            {
                Notify(nameof(HasSearchQuery));
                ApplyFilter();
            }
        }
    }
    public bool HasSearchQuery => !string.IsNullOrEmpty(_searchQuery);
    public void ClearSearch() => SearchQuery = "";

    // --- Toast (transient error/info banner) ---

    private string? _toastMessage;
    public string? ToastMessage
    {
        get => _toastMessage;
        private set { if (Set(ref _toastMessage, value)) Notify(nameof(IsToastVisible)); }
    }
    public bool IsToastVisible => !string.IsNullOrEmpty(_toastMessage);

    private CancellationTokenSource? _toastCts;

    // Show a transient error message at the bottom of the main pane. Auto-clears after
    // a few seconds. Subsequent calls cancel the previous timer and replace the text.
    public void ShowError(string message)
    {
        ToastMessage = message;
        _toastCts?.Cancel();
        var cts = new CancellationTokenSource();
        _toastCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(5000, cts.Token);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(_toastCts, cts)) ToastMessage = null;
                });
            }
            catch (TaskCanceledException) { /* superseded by a newer toast */ }
        });
    }

    public void DismissToast() => ToastMessage = null;

    // --- Composer ---
    // Composer state (draft + attachments) lives on SessionVm so it isn't shared
    // across sessions when the user switches. These methods just route to the
    // active session's composer.

    public void AddAttachment(string path) => _activeSession?.AddAttachment(path);

    public void RemoveAttachment(AttachmentVm attachment) => _activeSession?.RemoveAttachment(attachment);

    // Fired when the user hits Send. Whoever wires this (ClaudeService) is responsible
    // for appending the user message, running the turn, and updating transcript + status.
    public event Func<SessionVm, string, Task>? SendRequested;

    public async Task SendAsync()
    {
        if (_activeSession is not { CanSend: true } session) return;
        var draft = session.ComposerDraft.TrimEnd();
        // The CLI's @-mention syntax inlines file contents; prefix attachments so the
        // model sees them as context before the prompt.
        if (session.ComposerAttachments.Count > 0)
        {
            // Newline-separated: paths may contain spaces, which would otherwise terminate
            // the @-mention at the wrong boundary.
            var refs = string.Join('\n', session.ComposerAttachments.Select(a => "@" + a.Path));
            draft = draft.Length == 0 ? refs : refs + "\n\n" + draft;
            session.ComposerAttachments.Clear();
        }
        session.ComposerDraft = "";
        var handler = SendRequested;
        if (handler is not null) await handler(session, draft);
    }

    // Cancels the active session's in-flight turn, if any. ClaudeService swaps the CTS
    // to null on completion, so this is a no-op when nothing's running.
    public void CancelActiveTurn()
    {
        try { _activeSession?.CancellationSource?.Cancel(); }
        catch (ObjectDisposedException) { /* race: already completed */ }
    }

    // Manual "Resume" button on a stalled session. Same orchestration as auto-resume but
    // ignores the per-session retry cap — the user explicitly asked.
    public void ResumeStalledSession(SessionVm session)
    {
        if (StallDetection is { } svc) _ = svc.ResumeAsync(session, ignoreRetryCap: true);
    }

    public ShellVm(Tokens tokens, SessionManager manager, ClaudeCapabilities claude)
    {
        Tokens = tokens;
        Manager = manager;
        Claude = claude;
        BuildFilters();
        RecalcFilterCounts();

        // Recalc when projects or their sessions mutate.
        Manager.Projects.CollectionChanged += OnProjectsChanged;
        foreach (var p in Manager.Projects)
        {
            p.Sessions.CollectionChanged += OnSessionsChanged;
            foreach (var s in p.Sessions) HookSession(s);
        }

        ApplyFilter();

        // Default selection: first session we find that's currently visible.
        foreach (var p in Manager.Projects)
            foreach (var s in p.Sessions)
                if (s.IsVisibleInTree) { ActiveSession = s; goto selected; }
    selected:;
    }

    private void HookSession(SessionVm s) => s.PropertyChanged += OnSessionPropertyChanged;
    private void UnhookSession(SessionVm s) => s.PropertyChanged -= OnSessionPropertyChanged;

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Status or IsStalled changes can move a session in/out of the current filter.
        if (e.PropertyName == nameof(SessionVm.Status) || e.PropertyName == nameof(SessionVm.IsStalled))
        {
            ApplyFilter();
            RecalcFilterCounts();
        }
    }

    private void OnProjectsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (ProjectVm p in e.NewItems)
            {
                p.Sessions.CollectionChanged += OnSessionsChanged;
                foreach (var s in p.Sessions) HookSession(s);
            }
        if (e.OldItems is not null)
            foreach (ProjectVm p in e.OldItems)
            {
                p.Sessions.CollectionChanged -= OnSessionsChanged;
                foreach (var s in p.Sessions) UnhookSession(s);
            }
        ApplyFilter();
        RecalcFilterCounts();
    }

    private void OnSessionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (SessionVm s in e.NewItems) HookSession(s);
        if (e.OldItems is not null)
            foreach (SessionVm s in e.OldItems) UnhookSession(s);
        ApplyFilter();
        RecalcFilterCounts();
    }

    public void ApplyFilter()
    {
        var selected = Filters.FirstOrDefault(f => f.IsSelected);
        var query = _searchQuery.Trim();
        bool filterIsActive = !string.IsNullOrEmpty(query) ||
            (selected is not null && selected.Label != "All sessions");
        foreach (var p in Projects)
        {
            bool anyVisible = false;
            bool projectMatches = string.IsNullOrEmpty(query) ||
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
            foreach (var s in p.Sessions)
            {
                bool visible = MatchesFilter(selected, s) && MatchesSearch(s, p, query, projectMatches);
                s.IsVisibleInTree = visible;
                if (visible) anyVisible = true;
            }
            // Empty projects must still surface so the user can rename / delete them.
            // Hide them only when a status filter is narrowing the tree to specific sessions
            // (e.g. "Running"), or when a search query is set and the project name does not match.
            bool emptyProjectMatch = p.Sessions.Count == 0
                && (selected is null || selected.Label == "All sessions")
                && (string.IsNullOrEmpty(query) || projectMatches);
            p.IsVisibleInTree = anyVisible || emptyProjectMatch;
            // A collapsed project would otherwise hide its matches behind the chevron;
            // force it open so search/filter results are actually visible.
            if (filterIsActive && anyVisible) p.IsExpanded = true;
        }
    }

    private static bool MatchesFilter(FilterVm? filter, SessionVm s) =>
        filter?.Label switch
        {
            null or "All sessions" => true,
            "Running" => (s.Status is SessionStatus.Working or SessionStatus.RunningTool) && !s.IsStalled,
            // IsStalled is an in-memory flag that overlays Working/RunningTool when the
            // claude stream has been silent past the configured threshold. Treated as
            // needs-attention so the user has one obvious place to find it.
            "Needs attention" => s.Status is SessionStatus.Waiting or SessionStatus.Error || s.IsStalled,
            "Idle" => s.Status is SessionStatus.Idle or SessionStatus.Completed,
            _ => true,
        };

    // Matches if any of: project name, session title, branch contains the query (case-insensitive).
    // Empty query matches everything.
    private static bool MatchesSearch(SessionVm s, ProjectVm p, string query, bool projectMatches)
    {
        if (string.IsNullOrEmpty(query)) return true;
        if (projectMatches) return true;
        return s.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || s.Branch.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void BuildFilters()
    {
        Filters.Clear();
        Filters.Add(new FilterVm(Tokens) { Label = "All sessions", IsSelected = true });
        Filters.Add(new FilterVm(Tokens) { Label = "Running", HasDot = true, DotColor = Tokens.AccentBrush, Pulse = true });
        Filters.Add(new FilterVm(Tokens) { Label = "Needs attention", HasDot = true, DotColor = Tokens.Warn });
        Filters.Add(new FilterVm(Tokens) { Label = "Idle", HasDot = true, DotColor = Tokens.TextMute });
    }

    private void RecalcFilterCounts()
    {
        if (Filters.Count < 4) return;
        int total = 0, running = 0, attention = 0, idle = 0;
        foreach (var p in Projects)
            foreach (var s in p.Sessions)
            {
                total++;
                if (s.IsStalled)
                {
                    // Stalled sessions are still nominally Working/RunningTool but should
                    // count under attention, not running, so the badges reflect what the
                    // sidebar filter would show.
                    attention++;
                    continue;
                }
                switch (s.Status)
                {
                    case SessionStatus.Working:
                    case SessionStatus.RunningTool: running++; break;
                    case SessionStatus.Waiting:
                    case SessionStatus.Error: attention++; break;
                    case SessionStatus.Idle:
                    case SessionStatus.Completed: idle++; break;
                }
            }
        Filters[0].Count = total;
        Filters[1].Count = running;
        Filters[2].Count = attention;
        Filters[3].Count = idle;
    }
}
