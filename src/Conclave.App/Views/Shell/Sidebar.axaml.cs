using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Conclave.App.ViewModels;

namespace Conclave.App.Views.Shell;

public partial class Sidebar : UserControl
{
    public Sidebar()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnSidebarKeyDown, handledEventsToo: false);
    }

    // --- Clicks / selection ---

    private void OnSessionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control c
            && c.DataContext is SessionVm s
            && DataContext is ShellVm shell)
        {
            shell.ActiveSession = s;
            e.Handled = true;
        }
    }

    private void OnFilterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ShellVm shell) return;
        if (sender is Control c && c.DataContext is FilterVm clicked)
        {
            foreach (var f in shell.Filters) f.IsSelected = ReferenceEquals(f, clicked);
            shell.ApplyFilter();
            e.Handled = true;
        }
    }

    // --- Search ---

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellVm shell) return;
        if (e.Key == Key.Escape)
        {
            shell.ClearSearch();
            e.Handled = true;
        }
    }

    private void OnSearchClearPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellVm shell)
        {
            shell.ClearSearch();
            e.Handled = true;
        }
    }

    // --- Rename ---

    private void OnSessionTitleDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control c && c.DataContext is SessionVm s)
            s.IsEditing = true;
    }

    private void OnEditAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.Focus();
            tb.SelectAll();
        }
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (e.Key == Key.Enter) { CommitRename(tb); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelRename(tb); e.Handled = true; }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is SessionVm s && s.IsEditing)
            CommitRename(tb);
    }

    private void CommitRename(TextBox tb)
    {
        if (tb.DataContext is not SessionVm s || DataContext is not ShellVm shell) return;
        var trimmed = (s.EditingName ?? "").Trim();
        if (!string.IsNullOrEmpty(trimmed) && trimmed != s.Title)
        {
            try { shell.Manager.RenameSession(s, trimmed); }
            catch (Exception ex) { shell.ShowError($"Rename failed: {ex.Message}"); }
        }
        s.IsEditing = false;
    }

    private void CancelRename(TextBox tb)
    {
        if (tb.DataContext is SessionVm s) s.IsEditing = false;
    }

    // --- Context menu ---

    private void OnRenameMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is SessionVm s)
            s.IsEditing = true;
    }

    private void OnDeleteMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is SessionVm s && DataContext is ShellVm shell)
            DeleteSession(shell, s);
    }

    private void OnForkMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.DataContext is not SessionVm s
            || DataContext is not ShellVm shell) return;
        try
        {
            var fork = shell.Manager.ForkSession(s);
            shell.ActiveSession = fork;
        }
        catch (Exception ex) { shell.ShowError($"Fork session failed: {ex.Message}"); }
    }

    private void OnRenameProjectMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ProjectVm p)
            p.IsEditing = true;
    }

    private void OnNewSessionForProject(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control c && c.DataContext is ProjectVm p && DataContext is ShellVm shell)
        {
            shell.OpenNewSessionForProject(p);
            e.Handled = true;
        }
    }

    // --- Project rename ---

    private void OnProjectNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control c && c.DataContext is ProjectVm p)
            p.IsEditing = true;
    }

    private void OnProjectRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (e.Key == Key.Enter) { CommitProjectRename(tb); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelProjectRename(tb); e.Handled = true; }
    }

    private void OnProjectRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is ProjectVm p && p.IsEditing)
            CommitProjectRename(tb);
    }

    private void CommitProjectRename(TextBox tb)
    {
        if (tb.DataContext is not ProjectVm p || DataContext is not ShellVm shell) return;
        var trimmed = (p.EditingName ?? "").Trim();
        if (!string.IsNullOrEmpty(trimmed) && trimmed != p.Name)
        {
            try { shell.Manager.RenameProject(p, trimmed); }
            catch (Exception ex) { shell.ShowError($"Rename project failed: {ex.Message}"); }
        }
        p.IsEditing = false;
    }

    private void CancelProjectRename(TextBox tb)
    {
        if (tb.DataContext is ProjectVm p) p.IsEditing = false;
    }

    // --- Keyboard ---

    private void OnSidebarKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellVm shell) return;
        if (shell.ActiveSession is not { } s) return;

        // Don't steal keys while a textbox is focused (e.g., rename in progress).
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox) return;

        if (e.Key == Key.F2) { s.IsEditing = true; e.Handled = true; }
        else if (e.Key == Key.Delete) { DeleteSession(shell, s); e.Handled = true; }
    }

    private static void DeleteSession(ShellVm shell, SessionVm s)
    {
        try
        {
            shell.Manager.DeleteSession(s);
            if (ReferenceEquals(shell.ActiveSession, s))
                shell.ActiveSession = null;
        }
        catch (Exception ex) { shell.ShowError($"Delete session failed: {ex.Message}"); }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
