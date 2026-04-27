using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
                if (previous is not null)
                {
                    previous.IsActive = false;
                    previous.PropertyChanged -= OnActiveSessionPropertyChanged;
                }
                if (value is not null)
                {
                    value.IsActive = true;
                    value.PropertyChanged += OnActiveSessionPropertyChanged;
                    Manager.LoadTranscriptIfNeeded(value);
                }
                Notify(nameof(HasActiveSession));
                Notify(nameof(ActiveProjectName));
                Notify(nameof(CanSend));
            }
        }
    }

    private void OnActiveSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionVm.IsBusy)) Notify(nameof(CanSend));
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

    private string _composerDraft = "";
    public string ComposerDraft
    {
        get => _composerDraft;
        set { if (Set(ref _composerDraft, value)) Notify(nameof(CanSend)); }
    }

    // Files dropped into the composer. Surfaced as pills below the input; cleared on Send.
    public ObservableCollection<AttachmentVm> ComposerAttachments { get; } = new();
    public bool HasComposerAttachments => ComposerAttachments.Count > 0;

    public bool CanSend =>
        _activeSession is { IsBusy: false }
        && (!string.IsNullOrWhiteSpace(_composerDraft) || ComposerAttachments.Count > 0);

    public void AddAttachment(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        foreach (var existing in ComposerAttachments)
            if (string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase)) return;
        ComposerAttachments.Add(new AttachmentVm(Tokens, path));
    }

    public void RemoveAttachment(AttachmentVm attachment) => ComposerAttachments.Remove(attachment);

    // Fired when the user hits Send. Whoever wires this (ClaudeService) is responsible
    // for appending the user message, running the turn, and updating transcript + status.
    public event Func<SessionVm, string, Task>? SendRequested;

    public async Task SendAsync()
    {
        if (!CanSend || _activeSession is null) return;
        var session = _activeSession;
        var draft = _composerDraft.TrimEnd();
        // The CLI's @-mention syntax inlines file contents; prefix attachments so the
        // model sees them as context before the prompt.
        if (ComposerAttachments.Count > 0)
        {
            // Newline-separated: paths may contain spaces, which would otherwise terminate
            // the @-mention at the wrong boundary.
            var refs = string.Join('\n', ComposerAttachments.Select(a => "@" + a.Path));
            draft = draft.Length == 0 ? refs : refs + "\n\n" + draft;
            ComposerAttachments.Clear();
        }
        ComposerDraft = "";
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

        ComposerAttachments.CollectionChanged += (_, _) =>
        {
            Notify(nameof(HasComposerAttachments));
            Notify(nameof(CanSend));
        };

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
        // Status changes can move a session in/out of the current filter.
        if (e.PropertyName == nameof(SessionVm.Status))
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
            p.IsVisibleInTree = anyVisible;
        }
    }

    private static bool MatchesFilter(FilterVm? filter, SessionVm s) =>
        filter?.Label switch
        {
            null or "All sessions" => true,
            "Running" => s.Status is SessionStatus.Working or SessionStatus.RunningTool,
            "Needs attention" => s.Status is SessionStatus.Waiting or SessionStatus.Error,
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
