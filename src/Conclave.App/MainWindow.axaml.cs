using Avalonia;
using Avalonia.Controls;
using Conclave.App.Claude;
using Conclave.App.Design;
using Conclave.App.Sessions;
using Conclave.App.ViewModels;
using Conclave.App.Views.Shell;

namespace Conclave.App;

public partial class MainWindow : Window
{
    // Below this window width, the right panel + its splitter auto-collapse so the main
    // pane keeps a usable amount of room. Above the threshold we restore whatever width
    // the user had before the auto-collapse (so manual resizes survive a window narrowing).
    private const double RightCollapseThreshold = 1080;

    private SessionManager? _manager;
    private AutoCleanupService? _autoCleanup;
    private PermissionMcpServer? _permissions;
    private ShellVm? _shell;
    // Written on the UI thread from Activated/Deactivated, read by NotificationService
    // from a ClaudeService async continuation — volatile keeps the cross-thread read honest.
    private volatile bool _isWindowActive = true;

    private double _savedRightPanelWidth = 320;

    // ColumnDefinition isn't a Control, so x:Name doesn't generate a strongly-typed
    // field. Indexed lookup against the named ShellGrid is the cleanest way to reach
    // the splitter + right-panel columns.
    private ColumnDefinition RightSplit => ShellGrid.ColumnDefinitions[3];
    private ColumnDefinition RightCol => ShellGrid.ColumnDefinitions[4];

    // Lazy-instantiated modal user-controls. Each is created on first open and kept
    // around for the life of the window so subsequent opens are instantaneous (the
    // first-open cost is what we cared about; tearing down on close would just push
    // it onto the next open). DataContext inherits from MainWindow → ShellVm.
    private NewSessionModal? _newSessionModal;
    private NewFusionProjectModal? _newFusionModal;
    private PreferencesModal? _preferencesModal;
    private AboutModal? _aboutModal;

    public MainWindow()
    {
        StartupLog.Mark("MainWindow ctor: begin");
        InitializeComponent();
        StartupLog.Mark("MainWindow ctor: InitializeComponent done");

        // macOS-only: extend our content under the title bar so the native traffic
        // lights sit inside the Avalonia window. Windows + Linux keep their default
        // system chrome — mixing custom + system chrome there tends to look off.
        if (OperatingSystem.IsMacOS())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = -1;
        }

        var tokens = Tokens.DarkCoolNormalMedium();
        _manager = SessionManager.Open(tokens);
        StartupLog.Mark("MainWindow ctor: SessionManager.Open done");

        // Native OS notifications for "claude is done" / "claude is asking a question".
        // We suppress while the window is active — the user already has eyes on it.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
        var notifications = new NotificationService
        {
            Enabled = SettingsKeys.ReadNotificationsEnabled(_manager.Db),
            IsWindowActive = () => _isWindowActive,
            IconPath = File.Exists(iconPath) ? iconPath : null,
        };
        _manager.Notifications = notifications;

        // Local HTTP MCP server claude calls when a gated tool needs the user's approval.
        // One listener per app process, routed per turn via bearer-token handlers.
        _permissions = new PermissionMcpServer();
        _permissions.Start();
        _manager.Permissions = _permissions;

        var claudeService = new ClaudeService(_manager);
        // Seed the title-bar capabilities from the cached version in the settings table so
        // the very first paint already shows the previous launch's claude version. The
        // background probe re-validates and writes back if the user upgraded the CLI
        // between launches. Pre-cache (or first-ever launch) the seed is empty and the
        // title bar binding paints with Available=false until the probe returns.
        var capabilities = new ClaudeCapabilities(_manager.Db);
        capabilities.BeginProbe();
        _shell = new ShellVm(tokens, _manager, capabilities);
        StartupLog.Mark("MainWindow ctor: ShellVm built");
        _shell.SendRequested += (session, prompt) => claudeService.RunTurnAsync(session, prompt);
        _shell.PropertyChanged += OnShellPropertyChanged;

        _autoCleanup = new AutoCleanupService(_manager);
        _shell.AutoCleanup = _autoCleanup;
        _autoCleanup.Start();

        DataContext = _shell;

        Activated += (_, _) => _isWindowActive = true;
        Deactivated += (_, _) => _isWindowActive = false;

        SizeChanged += (_, e) => ApplyResponsiveLayout(e.NewSize.Width, _shell?.HasActiveSession ?? false);
        Closing += (_, _) =>
        {
            // Order matters: dispose the session manager (and any active turns) before
            // the MCP listener so an in-flight permission HTTP response can still be
            // written back. Closing the listener first races the response onto a closed
            // socket and claude sees a connection error instead of a clean deny.
            _autoCleanup?.Dispose();
            _manager?.Dispose();
            _permissions?.Dispose();
        };
        StartupLog.Mark("MainWindow ctor: end");
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ApplyResponsiveLayout(Bounds.Width, _shell?.HasActiveSession ?? false);
        StartupLog.Mark("MainWindow OnLoaded");

        // Now that the first frame has rendered, kick a staggered sweep that refreshes
        // diff + PR state for every session. BuildSessionVm intentionally skips this on
        // load — bursting N×2 git/gh subprocesses at startup is the single largest cause
        // of slow first-paint on Windows where every Process.Start hits the AV scanner.
        // 75ms between sessions keeps the pipeline busy without monopolising it. Skip
        // the active session — the ShellVm constructor's auto-select fired
        // RefreshOnActivation for it already, and we don't want to double its budget.
        _manager?.RefreshAllStaggered(TimeSpan.FromMilliseconds(75), _shell?.ActiveSession);
    }

    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // When the active session is cleared (empty state), collapse the right column.
        // When it's restored, restore the right column to its remembered width.
        if (e.PropertyName == nameof(ShellVm.HasActiveSession))
            ApplyResponsiveLayout(Bounds.Width, _shell?.HasActiveSession ?? false);

        // Lazy-materialise the heavier modals the first time the user opens them. The
        // modal stays in the visual tree once instantiated so re-opens are immediate;
        // we just toggle IsVisible via the same binding the modal's XAML already has.
        if (e.PropertyName == nameof(ShellVm.IsNewSessionOpen) && _shell?.IsNewSessionOpen == true)
            EnsureModal(ref _newSessionModal);
        else if (e.PropertyName == nameof(ShellVm.IsNewFusionOpen) && _shell?.IsNewFusionOpen == true)
            EnsureModal(ref _newFusionModal);
        else if (e.PropertyName == nameof(ShellVm.IsPreferencesOpen) && _shell?.IsPreferencesOpen == true)
            EnsureModal(ref _preferencesModal);
        else if (e.PropertyName == nameof(ShellVm.IsAboutOpen) && _shell?.IsAboutOpen == true)
            EnsureModal(ref _aboutModal);
    }

    private void EnsureModal<T>(ref T? slot) where T : Control, new()
    {
        if (slot is not null) return;
        slot = new T();
        ModalHost.Children.Add(slot);
    }

    private void ApplyResponsiveLayout(double windowWidth, bool hasActiveSession)
    {
        bool shouldCollapseRight = !hasActiveSession || windowWidth < RightCollapseThreshold;

        if (shouldCollapseRight && !RightColIsZero())
        {
            // Remember whatever the user had set so we can restore it on widen.
            _savedRightPanelWidth = RightCol.Width.Value > 0
                ? RightCol.Width.Value
                : _savedRightPanelWidth;
            RightCol.Width = new GridLength(0);
            RightSplit.Width = new GridLength(0);
        }
        else if (!shouldCollapseRight && RightColIsZero())
        {
            RightCol.Width = new GridLength(_savedRightPanelWidth);
            RightSplit.Width = new GridLength(4);
        }
    }

    private bool RightColIsZero() => RightCol.Width.Value == 0;

    private void OnAboutMenuClick(object? sender, System.EventArgs e) => _shell?.OpenAbout();
}
