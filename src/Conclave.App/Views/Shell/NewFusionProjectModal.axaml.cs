using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Conclave.App.ViewModels;
using RoutedEvent = Avalonia.Interactivity.RoutedEventArgs;

namespace Conclave.App.Views.Shell;

public partial class NewFusionProjectModal : UserControl
{
    public NewFusionProjectModal() => InitializeComponent();

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellVm shell) shell.CancelNewFusionProject();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellVm shell) shell.CancelNewFusionProject();
    }

    private void OnPrimaryChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not FusionMemberPickVm pick) return;
        if (DataContext is not ShellVm shell || shell.NewFusion is not { } nf) return;
        nf.SetPrimary(pick);
    }

    private void OnSecondaryChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellVm shell || shell.NewFusion is not { } nf) return;
        nf.NotifyPickChanged();
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellVm shell || shell.NewFusion is not { } nf) return;
        if (!nf.CanCreate || nf.Primary is null) return;
        nf.ErrorMessage = null;
        try
        {
            var fusion = shell.Manager.CreateFusionProject(nf.Name.Trim(), nf.Primary, nf.Secondaries);
            shell.CancelNewFusionProject();
            // Bounce back to new-session with the fusion preselected so the user can start a
            // session in their freshly-built bundle without a second click trip.
            shell.NewSession = new NewSessionVm(shell.Tokens, shell.Projects) { Project = fusion };
        }
        catch (Exception ex)
        {
            nf.ErrorMessage = ex.Message;
        }
    }

    private void OnModalKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellVm shell) return;
        if (!shell.IsNewFusionOpen) return;

        if (e.Key == Key.Escape)
        {
            shell.CancelNewFusionProject();
            e.Handled = true;
            return;
        }

        bool submitChord = e.Key == Key.Enter
            && (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control));
        if (submitChord && shell.NewFusion is { CanCreate: true })
        {
            OnCreate(sender, new RoutedEvent());
            e.Handled = true;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
