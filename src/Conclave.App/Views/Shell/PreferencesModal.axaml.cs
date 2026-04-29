using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Conclave.App.ViewModels;

namespace Conclave.App.Views.Shell;

public partial class PreferencesModal : UserControl
{
    public PreferencesModal() => InitializeComponent();

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellVm shell) shell.ClosePreferences();
    }

    private void OnDone(object? sender, RoutedEventArgs e)
    {
        CommitDays();
        if (DataContext is ShellVm shell) shell.ClosePreferences();
    }

    private async void OnCleanupNow(object? sender, RoutedEventArgs e)
    {
        CommitDays();
        if (DataContext is ShellVm shell) await shell.RunCleanupNowAsync();
    }

    private void OnModalKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellVm shell || !shell.IsPreferencesOpen) return;
        if (e.Key == Key.Escape)
        {
            CommitDays();
            shell.ClosePreferences();
            e.Handled = true;
        }
    }

    // The days TextBox uses one-way binding for display; commit happens on Enter or blur
    // so the user isn't forced to clean intermediate keystrokes (e.g. typing "10" through "1").
    private void OnDaysKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitDays();
            e.Handled = true;
        }
    }

    private void OnDaysCommit(object? sender, RoutedEventArgs e) => CommitDays();

    private void CommitDays()
    {
        if (this.FindControl<TextBox>("DaysInput") is not { } box) return;
        if (DataContext is not ShellVm shell) return;
        if (int.TryParse(box.Text, out var n) && n > 0)
            shell.AutoCleanupDays = n;
        // Re-display whatever the VM accepted so out-of-range input snaps back.
        box.Text = shell.AutoCleanupDays.ToString();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
