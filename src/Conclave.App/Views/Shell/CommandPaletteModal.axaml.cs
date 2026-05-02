using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Conclave.App.ViewModels;

namespace Conclave.App.Views.Shell;

public partial class CommandPaletteModal : UserControl
{
    public CommandPaletteModal()
    {
        InitializeComponent();
        // Auto-focus the query input every time the palette opens. The IsVisible
        // toggle that drives this fires before the input is laid out, so we post the
        // focus to the next dispatcher tick instead of attempting it inline.
        DataContextChanged += (_, _) => FocusOnOpen();
        this.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible) FocusOnOpen();
        };
    }

    private void FocusOnOpen()
    {
        if (DataContext is not ShellVm shell || !shell.IsCommandPaletteOpen) return;
        Dispatcher.UIThread.Post(() =>
        {
            this.FindControl<TextBox>("QueryInput")?.Focus();
        });
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellVm shell) shell.CloseCommandPalette();
    }

    private void OnResultPressed(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (DataContext is not ShellVm shell || shell.CommandPalette is not { } palette) return;
        // The clicked row is selected before the release fires (ListBox handles the
        // pointer-pressed), so we just execute the current selection.
        palette.ExecuteSelected();
        e.Handled = true;
    }

    private void OnModalKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellVm shell || !shell.IsCommandPaletteOpen) return;
        var palette = shell.CommandPalette;
        if (palette is null) return;

        switch (e.Key)
        {
            case Key.Escape:
                shell.CloseCommandPalette();
                e.Handled = true;
                break;
            case Key.Down:
                palette.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                palette.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                palette.ExecuteSelected();
                e.Handled = true;
                break;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
