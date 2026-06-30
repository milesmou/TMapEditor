using System.Text.Json;
using TMapEditor.Models;

namespace TMapEditor.Services;

public static class TMapFileService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static TMapDocument Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var document = JsonSerializer.Deserialize(json, TMapJsonContext.Default.TMapDocument)
            ?? throw new InvalidDataException("无法读取 tmap 文件。");
        if (document.FormatVersion != 2)
            throw new InvalidDataException($"不支持 tmap 格式版本 {document.FormatVersion}，当前版本为 2。");
        document.FilePath = Path.GetFullPath(filePath);
        ApplyFileName(document);
        Normalize(document);
        return document;
    }

    public static void Save(TMapDocument document, string filePath)
    {
        document.FilePath = Path.GetFullPath(filePath);
        ApplyFileName(document);
        Normalize(document);
        var json = JsonSerializer.Serialize(document, TMapJsonContext.Default.TMapDocument);
        File.WriteAllText(document.FilePath, json + Environment.NewLine);
    }

    public static string ResolveImagePath(TMapDocument document, string imagePath)
    {
        return Path.IsPathRooted(imagePath)
            ? imagePath
            : Path.GetFullPath(Path.Combine(document.BaseDirectory, imagePath));
    }

    public static string MakePortableImagePath(TMapDocument document, string imagePath)
    {
        if (document.FilePath is null) return Path.GetFullPath(imagePath);
        var relative = Path.GetRelativePath(document.BaseDirectory, imagePath);
        return relative.Replace('\\', '/');
    }

    public static void RefreshResourcePaths(TMapDocument document)
    {
        foreach (var resource in document.Resources)
        {
            resource.ThumbnailPath = ResolveImagePath(document, resource.ImagePath);
        }
    }

    public static void ApplyFileName(TMapDocument document)
    {
        if (document.FilePath is null) return;
        var name = Path.GetFileNameWithoutExtension(document.FilePath);
        if (!string.IsNullOrWhiteSpace(name)) document.Name = name;
    }

    internal static void Normalize(TMapDocument document)
    {
        document.Layers ??= [];
        document.ViewSettings ??= new TMapViewSettings();
        document.Sprites ??= [];
        document.Resources ??= [];
        document.Cells ??= [];
        document.Cells = document.Cells
            .GroupBy(cell => (cell.Row, cell.Column))
            .Select(group => group.Last())
            .ToList();
        document.CellZs ??= [];
        document.CellZs = document.CellZs
            .GroupBy(cell => (cell.Row, cell.Column))
            .Select(group => group.Last())
            .Where(cell => cell.Z != 0)
            .ToList();
        document.Objects ??= [];
        foreach (var mapObject in document.Objects.Where(mapObject => string.IsNullOrWhiteSpace(mapObject.DisplayColor)))
            mapObject.DisplayColor = "#00BFFF";
        if (document.Objects.Count > 0)
        {
            var objectLayer = document.Layers.FirstOrDefault(layer => layer.Type == TMapLayerType.Object);
            if (objectLayer is null)
            {
                var name = "ObjectLayer";
                for (var index = 2; document.Layers.Any(layer =>
                         string.Equals(layer.Name, name, StringComparison.OrdinalIgnoreCase)); index++)
                    name = $"ObjectLayer{index}";
                objectLayer = new TMapLayer { Name = name, Type = TMapLayerType.Object };
                document.Layers.Add(objectLayer);
            }
            foreach (var mapObject in document.Objects.Where(mapObject => string.IsNullOrWhiteSpace(mapObject.Layer)))
                mapObject.Layer = objectLayer.Name;
        }
        RefreshResourcePaths(document);
    }
}
