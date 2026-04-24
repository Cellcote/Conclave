using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Conclave.App.Design;
using Conclave.App.Sessions;

namespace Conclave.App.ViewModels;

public enum MainView { Terminal, Plan, Logs }

public sealed class ShellVm : Views.Observable
{
    public Tokens Tokens { get; }
    public SessionManager Manager { get; }

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
    public void CancelNewSession() => NewSession = null;

    // --- Composer ---

    private string _composerDraft = "";
    public string ComposerDraft
    {
        get => _composerDraft;
        set { if (Set(ref _composerDraft, value)) Notify(nameof(CanSend)); }
    }

    public bool CanSend =>
        _activeSession is { IsBusy: false }
        && !string.IsNullOrWhiteSpace(_composerDraft);

    // Fired when the user hits Send. Whoever wires this (ClaudeService) is responsible
    // for appending the user message, running the turn, and updating transcript + status.
    public event Func<SessionVm, string, Task>? SendRequested;

    public async Task SendAsync()
    {
        if (!CanSend || _activeSession is null) return;
        var session = _activeSession;
        var draft = _composerDraft.TrimEnd();
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

    public ShellVm(Tokens tokens, SessionManager manager)
    {
        Tokens = tokens;
        Manager = manager;
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
        foreach (var p in Projects)
        {
            bool anyVisible = false;
            foreach (var s in p.Sessions)
            {
                bool visible = MatchesFilter(selected, s);
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
