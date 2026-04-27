using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Conclave.App.ViewModels;

namespace Conclave.App.Views.Shell;

public partial class Toast : UserControl
{
    public Toast() => InitializeComponent();

    private void OnDismissPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellVm shell)
        {
            shell.DismissToast();
            e.Handled = true;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
