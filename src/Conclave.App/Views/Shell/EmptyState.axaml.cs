using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Conclave.App.ViewModels;

namespace Conclave.App.Views.Shell;

public partial class EmptyState : UserControl
{
    public EmptyState() => InitializeComponent();

    private void OnNewSession(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellVm shell) shell.OpenNewSession();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
