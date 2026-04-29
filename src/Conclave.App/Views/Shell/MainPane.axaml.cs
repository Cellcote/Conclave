using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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

        var composer = this.FindControl<Border>("ComposerBorder");
        if (composer is not null)
        {
            composer.AddHandler(DragDrop.DragOverEvent, OnComposerDragOver);
            composer.AddHandler(DragDrop.DropEvent, OnComposerDrop);
        }
        this.FindControl<TextBox>("ComposerBox")
            ?.AddHandler(TextBox.KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnComposerDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnComposerDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ShellVm shell) return;
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;
        foreach (var item in files)
        {
            var path = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                shell.AddAttachment(path);
        }
        e.Handled = true;
    }

    private void OnRemoveAttachment(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellVm shell) return;
        if (sender is Button { DataContext: AttachmentVm vm }) shell.RemoveAttachment(vm);
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

    private void OnForkFromHereMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.DataContext is not TranscriptMessageVm msg
            || DataContext is not ShellVm shell || shell.ActiveSession is not { } source) return;
        try
        {
            var fork = shell.Manager.ForkSessionAtMessage(source, msg);
            shell.ActiveSession = fork;
        }
        catch (System.Exception ex) { shell.ShowError($"Fork session failed: {ex.Message}"); }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
