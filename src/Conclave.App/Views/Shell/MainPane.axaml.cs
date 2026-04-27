using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Conclave.App.ViewModels;

namespace Conclave.App.Views.Shell;

public partial class MainPane : UserControl
{
    private ScrollHelper? _scroll;

    public MainPane()
    {
        InitializeComponent();
        _scroll = ScrollHelper.AttachIfReady(
            this.FindControl<ScrollViewer>("TranscriptScroller"));
    }

    private void OnSegTerminal(object? s, PointerPressedEventArgs e) => Switch(MainView.Terminal);
    private void OnSegPlan(object? s, PointerPressedEventArgs e) => Switch(MainView.Plan);
    private void OnSegLogs(object? s, PointerPressedEventArgs e) => Switch(MainView.Logs);

    private void Switch(MainView v)
    {
        if (DataContext is ShellVm shell) shell.ActiveView = v;
    }

    // Enter sends; Shift+Enter inserts a newline (handled by default TextBox behaviour).
    private async void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        if (DataContext is not ShellVm shell) return;
        e.Handled = true;
        await shell.SendAsync();
    }

    private async void OnComposerSend(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellVm shell) await shell.SendAsync();
    }

    private void OnComposerStop(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellVm shell) shell.CancelActiveTurn();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
