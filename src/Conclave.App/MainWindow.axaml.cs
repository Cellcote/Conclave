using Avalonia;
using Avalonia.Controls;
using Conclave.App.Claude;
using Conclave.App.Design;
using Conclave.App.Sessions;
using Conclave.App.ViewModels;

namespace Conclave.App;

public partial class MainWindow : Window
{
    // Below this window width, the right panel + its splitter auto-collapse so the main
    // pane keeps a usable amount of room. Above the threshold we restore whatever width
    // the user had before the auto-collapse (so manual resizes survive a window narrowing).
    private const double RightCollapseThreshold = 1080;

    private SessionManager? _manager;
    private AutoCleanupService? _autoCleanup;
    private ShellVm? _shell;
    private bool _isWindowActive = true;

    private double _savedRightPanelWidth = 320;

    // ColumnDefinition isn't a Control, so x:Name doesn't generate a strongly-typed
    // field. Indexed lookup against the named ShellGrid is the cleanest way to reach
    // the splitter + right-panel columns.
    private ColumnDefinition RightSplit => ShellGrid.ColumnDefinitions[3];
    private ColumnDefinition RightCol => ShellGrid.ColumnDefinitions[4];

    public MainWindow()
    {
        InitializeComponent();

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

        // Native OS notifications for "claude is done" / "claude is asking a question".
        // We suppress while the window is active — the user already has eyes on it.
        var notifications = new NotificationService
        {
            Enabled = SettingsKeys.ReadNotificationsEnabled(_manager.Db),
            IsWindowActive = () => _isWindowActive,
        };
        _manager.Notifications = notifications;

        var claudeService = new ClaudeService(_manager);
        var capabilities = ClaudeCapabilities.Detect();
        _shell = new ShellVm(tokens, _manager, capabilities);
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
            _autoCleanup?.Dispose();
            _manager?.Dispose();
        };
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ApplyResponsiveLayout(Bounds.Width, _shell?.HasActiveSession ?? false);
    }

    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // When the active session is cleared (empty state), collapse the right column.
        // When it's restored, restore the right column to its remembered width.
        if (e.PropertyName == nameof(ShellVm.HasActiveSession))
            ApplyResponsiveLayout(Bounds.Width, _shell?.HasActiveSession ?? false);
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
}
