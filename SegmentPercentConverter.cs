using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HyperDL;

/// <summary>
/// Converts (BytesDownloaded, ExpectedLength) into a 0-100 percentage for the
/// per-segment mini progress bars in the details panel.
/// </summary>
public sealed class SegmentPercentConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 ||
            values[0] is not long bytesDownloaded ||
            values[1] is not long expectedLength ||
            expectedLength <= 0)
        {
            return 0d;
        }

        return Math.Clamp(bytesDownloaded * 100d / expectedLength, 0, 100);
    }

    public object?[] ConvertBack(
        object? value,
        Type[] targetTypes,
        object? parameter,
        CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Collapsed when the bound value is null, Visible otherwise.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) => throw new NotSupportedException();
}
