using System.Globalization;
using Avalonia.Data.Converters;

namespace Conclave.App.ViewModels;

// Tiny helper: int → bool (true if zero). Used for empty-state visibility toggles
// without having to invert via `!` inside the XAML.
public sealed class CountIsZeroConverter : IValueConverter
{
    public static readonly CountIsZeroConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n == 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
