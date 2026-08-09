using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Umbrella.Wallet.App;

/// <summary>
/// True when the bound value equals the ConverterParameter (case-insensitive). Used to light up
/// the active segmented-filter chip without a boolean-per-option on the view model.
/// </summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public static readonly StringEqualsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Toast accent — red for errors, green for notices. Value is the bool ToastIsError.</summary>
public sealed class ToastBorderConverter : IValueConverter
{
    public static readonly ToastBorderConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Avalonia.Media.SolidColorBrush.Parse(value is true ? "#E24B4A" : "#3DDC97");
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Toast glyph — a warning triangle for errors, a check for notices.</summary>
public sealed class ToastGlyphConverter : IValueConverter
{
    public static readonly ToastGlyphConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "⚠" : "✓";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Row visibility for the Market search box. Values are [Symbol, Name, Query]; the row is visible
/// when the query is empty or is a case-insensitive substring of either the ticker or the name.
/// Kept as an IsVisible filter (not a collection rebuild) so the live price/sparkline updates,
/// which index rows by symbol, keep working untouched.
/// </summary>
public sealed class MarketFilterConverter : IMultiValueConverter
{
    public static readonly MarketFilterConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3) return true;
        var symbol = values[0]?.ToString() ?? string.Empty;
        var name = values[1]?.ToString() ?? string.Empty;
        var query = values[2]?.ToString()?.Trim() ?? string.Empty;
        if (query.Length == 0) return true;
        return symbol.Contains(query, StringComparison.OrdinalIgnoreCase)
            || name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
