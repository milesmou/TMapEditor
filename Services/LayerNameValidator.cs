namespace TMapEditor.Services;

public static class LayerNameValidator
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string Validate(string? value)
    {
        var name = value?.Trim() ?? "";
        if (name.Length == 0) throw new InvalidDataException("图层名称不能为空。");
        if (name is "." or ".." || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("图层名称包含不能用于导出目录的字符。");
        if (ReservedNames.Contains(name)) throw new InvalidDataException($"“{name}”是系统保留名称，请使用其他图层名。");
        return name;
    }
}
