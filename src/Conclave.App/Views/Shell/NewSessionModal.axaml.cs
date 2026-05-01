using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Conclave.App.ViewModels;
using RoutedEvent = Avalonia.Interactivity.RoutedEventArgs;

namespace Conclave.App.Views.Shell;

public partial class NewSessionModal : UserControl
{
    public NewSessionModal() => InitializeComponent();

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellVm shell) shell.CancelNewSession();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellVm shell) shell.CancelNewSession();
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellVm shell || shell.NewSession is not { } ns) return;
        if (!ns.CanCreate || ns.Project is null) return;
        ns.ErrorMessage = null;
        try
        {
            var created = shell.Manager.CreateSession(
                ns.Project, ns.Branch, ns.Model, ns.PermissionMode);
            shell.CancelNewSession();
            shell.ActiveSession = created;
        }
        catch (Exception ex)
        {
            ns.ErrorMessage = ExtractGitFailure(ex.Message);
        }
    }

    // `git worktree add` prefixes its failure output with "Preparing worktree …" —
    // strip that so the user sees just the meaningful line.
    private static string ExtractGitFailure(string raw)
    {
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fatal = lines.FirstOrDefault(l => l.StartsWith("fatal:", StringComparison.OrdinalIgnoreCase));
        return fatal ?? raw;
    }

    private async void OnAddProject(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellVm shell || shell.NewSession is not { } ns) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick git repositories",
            AllowMultiple = true,
        });
        if (picked.Count == 0) return;
        ns.ErrorMessage = null;

        var failures = new List<string>();
        int added = 0;
        foreach (var folder in picked)
        {
            var path = folder.Path.LocalPath;
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) name = path;
            try
            {
                shell.Manager.CreateProject(name, path);
                added++;
            }
            catch (Exception ex)
            {
                // Preserve the full message — CreateProject's "Not a git repository" error
                // puts the actionable hint on a second line, and stripping it would leave
                // the user without guidance.
                failures.Add($"{name}: {ex.Message}");
            }
        }

        // Leave Project unset so the user can pick from the dropdown or start a fusion next.
        // Only reset when we actually added something — otherwise a fully-failed pick would
        // silently clear whatever the user had selected before opening the picker.
        if (added > 0) ns.Project = null;
        if (failures.Count > 0)
        {
            ns.ErrorMessage = picked.Count == 1
                ? failures[0]
                : $"Added {added} of {picked.Count}. Skipped:\n  - {string.Join("\n  - ", failures)}";
        }
    }

    private void OnNewFusion(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellVm shell) shell.OpenNewFusionProject();
    }

    private void OnPickHaiku(object? sender, PointerPressedEventArgs e) => Pick("Haiku 4.5");
    private void OnPickSonnet(object? sender, PointerPressedEventArgs e) => Pick("Sonnet 4.5");
    private void OnPickOpus(object? sender, PointerPressedEventArgs e) => Pick("Opus 4");

    private void OnModalKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellVm shell) return;
        if (!shell.IsNewSessionOpen) return;

        if (e.Key == Key.Escape)
        {
            shell.CancelNewSession();
            e.Handled = true;
            return;
        }

        // ⌘⏎ on macOS, Ctrl+Enter elsewhere — the design's hint says ⌘⏎, but accept either.
        bool submitChord =
            e.Key == Key.Enter
            && (e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                || e.KeyModifiers.HasFlag(KeyModifiers.Control));
        if (submitChord && shell.NewSession is { CanCreate: true })
        {
            OnCreate(sender, new RoutedEvent());
            e.Handled = true;
        }
    }

    private void Pick(string model)
    {
        if (DataContext is ShellVm shell && shell.NewSession is { } ns) ns.Model = model;
    }

    private void OnPickPermDefault(object? sender, PointerPressedEventArgs e) =>
        PickPerm(PermissionModes.Default);
    private void OnPickPermAcceptEdits(object? sender, PointerPressedEventArgs e) =>
        PickPerm(PermissionModes.AcceptEdits);
    private void OnPickPermBypass(object? sender, PointerPressedEventArgs e) =>
        PickPerm(PermissionModes.BypassPermissions);

    private void PickPerm(string mode)
    {
        if (DataContext is ShellVm shell && shell.NewSession is { } ns) ns.PermissionMode = mode;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
