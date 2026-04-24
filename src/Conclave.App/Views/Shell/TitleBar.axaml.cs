using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Conclave.App.ViewModels;

namespace Conclave.App.Views.Shell;

public partial class TitleBar : UserControl
{
    public TitleBar() => InitializeComponent();

    private void OnNewSession(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellVm shell) shell.OpenNewSession();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
