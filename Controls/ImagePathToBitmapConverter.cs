using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace TMapEditor.Controls;

public sealed class ImagePathToBitmapConverter : IValueConverter, IDisposable
{
    private readonly Dictionary<string, Bitmap> _cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (_cache.TryGetValue(fullPath, out var bitmap)) return bitmap;
            bitmap = new Bitmap(fullPath);
            _cache[fullPath] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    public void Dispose()
    {
        foreach (var bitmap in _cache.Values)
        {
            bitmap.Dispose();
        }
        _cache.Clear();
    }
}
