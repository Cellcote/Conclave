using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Conclave.App.ViewModels;

namespace Conclave.App.Views.Shell;

public partial class AboutModal : UserControl
{
    public AboutModal() => InitializeComponent();

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellVm shell) shell.CloseAbout();
    }

    private void OnDone(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellVm shell) shell.CloseAbout();
    }

    private void OnModalKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellVm shell || !shell.IsAboutOpen) return;
        if (e.Key == Key.Escape)
        {
            shell.CloseAbout();
            e.Handled = true;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
