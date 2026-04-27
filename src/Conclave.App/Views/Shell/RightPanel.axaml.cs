using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Conclave.App.ViewModels;

namespace Conclave.App.Views.Shell;

public partial class RightPanel : UserControl
{
    public RightPanel() => InitializeComponent();

    private void OnPermissionModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb || cb.SelectedItem is not ComboBoxItem item) return;
        if (item.Content is not string mode) return;
        if (DataContext is not ShellVm shell || shell.ActiveSession is not { } session) return;
        if (session.PermissionMode == mode) return;
        shell.Manager.UpdatePermissionMode(session, mode);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
