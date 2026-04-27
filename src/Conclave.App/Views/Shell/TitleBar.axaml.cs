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
        // Stop the bubble so the title-bar drag handler below doesn't also fire.
        e.Handled = true;
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (VisualRoot is not Window window) return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        window.BeginMoveDrag(e);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
