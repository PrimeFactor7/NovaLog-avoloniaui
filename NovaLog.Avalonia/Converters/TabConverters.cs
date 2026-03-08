using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NovaLog.Avalonia.ViewModels;

/// <summary>Converts bool IsActive to FontWeight (Bold when active).</summary>
public sealed class BoolToFontWeightConverter : IValueConverter
{
    public static readonly BoolToFontWeightConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontWeight.Bold : FontWeight.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts bool IsActive to opacity (1.0 when active, 0.6 when inactive).</summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.6;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a hex color string (e.g., "#00FF41") to a SolidColorBrush.</summary>
public sealed class HexToColorConverter : IValueConverter
{
    public static readonly HexToColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch (FormatException) { /* invalid hex color, fall through */ }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts minimap visibility to the desired native vertical scrollbar mode.</summary>
public sealed class MinimapToVerticalScrollBarVisibilityConverter : IValueConverter
{
    public static readonly MinimapToVerticalScrollBarVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Two-way converter: bool (true=index 1, false=index 0) ↔ int SelectedIndex.</summary>
public sealed class BoolToIndexConverter : IValueConverter
{
    public static readonly BoolToIndexConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1 : 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is 1;
}

/// <summary>Two-way converter: indent size (2→0, 4→1) ↔ SelectedIndex for segmented control.</summary>
public sealed class IndentSizeToIndexConverter : IValueConverter
{
    public static readonly IndentSizeToIndexConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is 4 ? 1 : 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is 1 ? 4 : 2;
}
