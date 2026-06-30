using System.Globalization;
using Avalonia.Data.Converters;

namespace TMapEditor.Controls;

public sealed class PercentageSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percentage = value is double number ? number : 100;
        var baseSize = double.TryParse(parameter?.ToString(), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var parsed) ? parsed : 1;
        return baseSize * percentage / 100;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
