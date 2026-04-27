using Avalonia.Media;
using Conclave.App.Design;

namespace Conclave.App.ViewModels;

// One row in the sidebar's filter list (All, Running, Needs attention, Idle).
public sealed class FilterVm : Views.Observable
{
    public Tokens Tokens { get; }
    public string Label { get; init; } = "";
    public bool HasDot { get; init; }
    public IBrush? DotColor { get; init; }
    public bool Pulse { get; init; }

    private int _count;
    public int Count { get => _count; set => Set(ref _count, value); }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
            {
                Notify(nameof(BackgroundBrush));
                Notify(nameof(TextBrush));
                Notify(nameof(FontWeight));
            }
        }
    }

    public IBrush BackgroundBrush => _isSelected ? Tokens.Panel : Brushes.Transparent;
    public IBrush TextBrush => _isSelected ? Tokens.Text : Tokens.TextDim;
    public Avalonia.Media.FontWeight FontWeight =>
        _isSelected ? Avalonia.Media.FontWeight.Medium : Avalonia.Media.FontWeight.Normal;

    public FilterVm(Tokens tokens) => Tokens = tokens;
}
