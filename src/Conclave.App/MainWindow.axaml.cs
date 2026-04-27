using Avalonia.Controls;
using Conclave.App.Claude;
using Conclave.App.Design;
using Conclave.App.Sessions;
using Conclave.App.ViewModels;

namespace Conclave.App;

public partial class MainWindow : Window
{
    private SessionManager? _manager;

    public MainWindow()
    {
        InitializeComponent();
        var tokens = Tokens.DarkCoolNormalMedium();
        _manager = SessionManager.Open(tokens);

        var claudeService = new ClaudeService(_manager);
        var capabilities = ClaudeCapabilities.Detect();
        var shell = new ShellVm(tokens, _manager, capabilities);
        shell.SendRequested += (session, prompt) => claudeService.RunTurnAsync(session, prompt);
        DataContext = shell;

        Closing += (_, _) => _manager?.Dispose();
    }
}
