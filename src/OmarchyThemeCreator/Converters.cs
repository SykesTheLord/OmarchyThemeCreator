using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OmarchyThemeCreator;

/// <summary>
/// Highlights a ticked wallhaven color swatch: an opaque white border when the bound
/// <c>IsSelected</c> is true, otherwise a transparent one. The border thickness stays fixed so
/// toggling selection never shifts the swatch layout.
/// </summary>
public sealed class SwatchSelectionBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Brushes.White : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Wraps an Avalonia <see cref="Color"/> in a <see cref="SolidColorBrush"/> for swatches.</summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Color c ? new SolidColorBrush(c) : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ISolidColorBrush b ? b.Color : Colors.Transparent;
}

/// <summary>Turns a hex color string (with or without leading '#') into a brush for swatches.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && s.Length > 0)
        {
            string hex = s.StartsWith('#') ? s : "#" + s;
            if (Color.TryParse(hex, out Color c)) return new SolidColorBrush(c);
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Shows just the file name for a full background image path.</summary>
public sealed class PathToFileNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? Path.GetFileName(s) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
