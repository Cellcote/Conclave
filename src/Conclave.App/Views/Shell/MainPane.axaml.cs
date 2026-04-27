using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Conclave.App.ViewModels;

namespace Conclave.App.Views.Shell;

public partial class MainPane : UserControl
{
    private ScrollViewer? _scroller;

    public MainPane()
    {
        InitializeComponent();
        _scroller = this.FindControl<ScrollViewer>("TranscriptScroller");
        var list = this.FindControl<ItemsControl>("TranscriptList");
        if (list?.ItemsView is { } view)
            view.CollectionChanged += OnTranscriptChanged;
    }

    // Keep the transcript pinned to the latest message when new ones arrive (typical
    // mid-stream behavior). Posted to Background priority so we run after the new item
    // has been measured/arranged — otherwise ScrollToEnd uses the pre-add extent.
    private void OnTranscriptChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add
            && e.Action != NotifyCollectionChangedAction.Reset) return;
        Dispatcher.UIThread.Post(() => _scroller?.ScrollToEnd(), DispatcherPriority.Background);
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
