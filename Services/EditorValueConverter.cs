using System.Globalization;
using Aprillz.MewUI;

namespace TMapEditor.Services;

internal static class EditorValueConverter
{
    public static string RequiredName(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException("名称不能为空。")
            : value.Trim();
    }

    public static double PositiveDouble(string value, string fieldName)
    {
        var number = ParseDouble(value, fieldName);
        return number > 0
            ? number
            : throw new InvalidDataException($"{fieldName}必须大于 0。");
    }

    public static double ParseDouble(string value, string fieldName)
    {
        return TryDouble(value, out var number)
            ? number
            : throw new InvalidDataException($"{fieldName}必须是有效数字。");
    }

    public static bool TryDouble(string value, out double number)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out number) ||
               double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    public static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    public static Color ParseDisplayColor(string? value)
    {
        try
        {
            return Color.FromHex(value ?? "#00BFFF");
        }
        catch (FormatException)
        {
            return new Color(0, 191, 255);
        }
    }

    public static string FormatDisplayColor(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
