namespace TMapEditor.Services;

internal static class ResourcePathUtility
{
    public static string GetUniqueResourcePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate)) return candidate;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(name)) name = "image";

        for (var index = 2; ; index++)
        {
            candidate = Path.Combine(directory, $"{name}_{index}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    public static bool IsPathWithinDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var directoryPrefix = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return fullPath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
