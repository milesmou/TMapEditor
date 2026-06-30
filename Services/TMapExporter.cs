using System.Text.Json;
using SkiaSharp;
using TMapEditor.Models;

namespace TMapEditor.Services;

public sealed record TMapExportResult(
    int ChunkCount,
    int WalkableCount,
    int BlockedCount,
    int ObjectCount,
    bool HardwareAccelerated);

public static class TMapExporter
{
    private static readonly SKPngEncoderOptions FastPngOptions =
        new(SKPngEncoderFilterFlags.NoFilters, 1);

    public static TMapExportResult Export(TMapDocument document, string outputDirectory)
    {
        return Export(document, outputDirectory, null, true);
    }

    internal static TMapExportResult Export(
        TMapDocument document,
        string outputDirectory,
        SkiaGpuContext? gpuContext,
        bool allowContextCreation)
    {
        Validate(document);
        using var context = new ExportContext(document, gpuContext, allowContextCreation);
        try
        {
            context.PreloadImages();
            Directory.CreateDirectory(outputDirectory);
            CleanupGeneratedLayerOutputs(document, outputDirectory);
            var generatedAt = DateTime.UtcNow.ToString("O");
            var chunkCount = 0;
            foreach (var layer in document.Layers)
            {
                chunkCount += ExportLayer(context, layer, outputDirectory, generatedAt);
            }

            var (walkableCount, blockedCount, objectCount) = ExportGrid(
                document, outputDirectory, generatedAt);
            return new TMapExportResult(
                chunkCount,
                walkableCount,
                blockedCount,
                objectCount,
                context.HardwareAccelerated);
        }
        finally
        {
            context.ReleaseGpuContext();
        }
    }

    private static int ExportLayer(
        ExportContext context,
        TMapLayer layer,
        string outputDirectory,
        string generatedAt)
    {
        var document = context.Document;
        var layerName = LayerNameValidator.Validate(layer.Name);
        var layerDirectory = Path.Combine(outputDirectory, layerName);
        var spriteDirectory = Path.Combine(layerDirectory, "sprite");
        var manifestDirectory = Path.Combine(layerDirectory, "manifest");
        Directory.CreateDirectory(spriteDirectory);
        Directory.CreateDirectory(manifestDirectory);

        var chunkWidth = document.Width / document.ChunkColumns;
        var chunkHeight = document.Height / document.ChunkRows;
        var originX = -document.Width / 2;
        var originY = -document.Height / 2;
        var sprites = document.Sprites
            .Where(sprite => sprite.Layer == layer.Name)
            .OrderBy(sprite => sprite.Order)
            .Select(CreateSpriteExportItem)
            .ToList();
        var chunks = new List<TMapExportChunkManifest>();

        for (var row = 0; row < document.ChunkRows; row++)
        {
            for (var column = 0; column < document.ChunkColumns; column++)
            {
                var x = originX + column * chunkWidth;
                var y = originY + row * chunkHeight;
                var chunkSprites = sprites
                    .Where(sprite => SpriteIntersectsChunk(sprite.Bounds, x, y, chunkWidth, chunkHeight))
                    .ToList();
                if (chunkSprites.Count == 0)
                {
                    continue;
                }
                var pixelWidth = Math.Max(1, (int)Math.Round(chunkWidth));
                var pixelHeight = Math.Max(1, (int)Math.Round(chunkHeight));
                var fileBaseName = $"chunk_{row}_{column}";
                var filePath = Path.Combine(spriteDirectory, fileBaseName + ".png");
                RenderChunk(context, chunkSprites, x, y, chunkWidth, chunkHeight, pixelWidth, pixelHeight, filePath);
                chunks.Add(new TMapExportChunkManifest(row, column, x, y, fileBaseName));
            }
        }

        var sourcePath = $"{document.Name}/MapRoot/{layer.Name}";
        var manifest = new TMapExportLayerManifest(
            generatedAt,
            document.FilePath is null ? null : Path.GetFileName(document.FilePath),
            sourcePath,
            $"{document.Name}/MapRoot",
            chunkWidth,
            chunkHeight,
            document.ChunkRows,
            document.ChunkColumns,
            "sourceLayerLeftBottom",
            document.Width,
            document.Height,
            chunks);
        WriteLayerManifest(Path.Combine(manifestDirectory, layerName + ".json"), manifest);
        return chunks.Count;
    }

    private static void RenderChunk(
        ExportContext context,
        IReadOnlyList<SpriteExportItem> sprites,
        double chunkX,
        double chunkY,
        double chunkWidth,
        double chunkHeight,
        int pixelWidth,
        int pixelHeight,
        string outputPath)
    {
        var scaleX = pixelWidth / chunkWidth;
        var scaleY = pixelHeight / chunkHeight;
        var imageInfo = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = context.CreateSurface(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, pixelWidth, pixelHeight));
        foreach (var item in sprites)
        {
            var sprite = item.Sprite;
            var bitmap = context.LoadBitmap(sprite.ImagePath);
            var centerX = (float)((sprite.X - chunkX) * scaleX);
            var centerY = (float)((chunkY + chunkHeight - sprite.Y) * scaleY);
            canvas.Save();
            canvas.Translate(centerX, centerY);
            canvas.RotateDegrees((float)-sprite.Rotation);
            canvas.Scale((float)(sprite.ScaleX * scaleX), (float)(sprite.ScaleY * scaleY));
            var rect = new SKRect(
                (float)(-sprite.AnchorX * sprite.Width),
                (float)(-(1 - sprite.AnchorY) * sprite.Height),
                (float)((1 - sprite.AnchorX) * sprite.Width),
                (float)(sprite.AnchorY * sprite.Height));
            canvas.DrawBitmap(bitmap, rect);
            canvas.Restore();
        }
        canvas.Restore();
        using var image = surface.Snapshot();
        using var rasterImage = image.ToRasterImage(true);
        using var pixmap = rasterImage.PeekPixels();
        if (pixmap is null)
            throw new InvalidOperationException("无法读取 chunk 像素数据。");
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128);
        if (!pixmap.Encode(stream, FastPngOptions))
            throw new InvalidOperationException($"无法写入 PNG：{outputPath}");
    }

    private static (int WalkableCount, int BlockedCount, int ObjectCount) ExportGrid(
        TMapDocument document,
        string outputDirectory,
        string generatedAt)
    {
        var originX = -document.Width / 2;
        var originY = -document.Height / 2;
        var columns = (int)Math.Ceiling(document.Width / document.GridSize);
        var rows = (int)Math.Ceiling(document.Height / document.GridSize);
        var allWalkableCells = document.Cells.Where(cell => cell.State == TMapCellState.Walk)
            .OrderBy(cell => cell.Row).ThenBy(cell => cell.Column)
            .Select(cell => new[] { cell.Row, cell.Column }).ToList();
        var allBlockedCells = document.Cells.Where(cell => cell.State == TMapCellState.Block)
            .OrderBy(cell => cell.Row).ThenBy(cell => cell.Column)
            .Select(cell => new[] { cell.Row, cell.Column }).ToList();
        var (walkableCells, blockedCells) = SelectCellLayerToExport(allWalkableCells, allBlockedCells);

        var chunkWidth = document.Width / document.ChunkColumns;
        var chunkHeight = document.Height / document.ChunkRows;
        var objects = document.Objects.Select(mapObject =>
        {
            var column = (int)Math.Floor((mapObject.X - originX) / document.GridSize);
            var row = (int)Math.Floor((mapObject.Y - originY) / document.GridSize);
            if (column < 0 || column >= columns || row < 0 || row >= rows) return null;
            return new TMapExportObjectManifest(
                mapObject.Name,
                row,
                column,
                Math.Clamp((int)Math.Floor((mapObject.Y - originY) / chunkHeight), 0, document.ChunkRows - 1),
                Math.Clamp((int)Math.Floor((mapObject.X - originX) / chunkWidth), 0, document.ChunkColumns - 1),
                string.IsNullOrWhiteSpace(mapObject.Args) ? null : mapObject.Args.Trim());
        }).OfType<TMapExportObjectManifest>().ToList();

        var manifest = new TMapExportGridManifest(
            generatedAt,
            document.FilePath is null ? null : Path.GetFileName(document.FilePath),
            "grid",
            document.Layers.Select(layer => layer.Name).ToList(),
            document.GridSize,
            rows,
            columns,
            document.ChunkRows,
            document.ChunkColumns,
            "sourceLayerLeftBottom",
            document.Width,
            document.Height,
            objects,
            walkableCells,
            blockedCells);
        WriteGridManifest(Path.Combine(outputDirectory, "Grid.json"), manifest);
        return (walkableCells?.Count ?? 0, blockedCells?.Count ?? 0, objects.Count);
    }

    private static (List<int[]>? WalkableCells, List<int[]>? BlockedCells) SelectCellLayerToExport(
        List<int[]> walkableCells,
        List<int[]> blockedCells)
    {
        if (walkableCells.Count == 0 && blockedCells.Count == 0) return (null, null);
        if (walkableCells.Count > 0 && blockedCells.Count == 0) return (walkableCells, null);
        if (blockedCells.Count > 0 && walkableCells.Count == 0) return (null, blockedCells);
        return walkableCells.Count <= blockedCells.Count
            ? (walkableCells, null)
            : (null, blockedCells);
    }

    private static SpriteExportItem CreateSpriteExportItem(TMapSprite sprite)
    {
        var localCorners = new[]
        {
            new TMapPoint(-sprite.AnchorX * sprite.Width, -sprite.AnchorY * sprite.Height),
            new TMapPoint((1 - sprite.AnchorX) * sprite.Width, -sprite.AnchorY * sprite.Height),
            new TMapPoint((1 - sprite.AnchorX) * sprite.Width, (1 - sprite.AnchorY) * sprite.Height),
            new TMapPoint(-sprite.AnchorX * sprite.Width, (1 - sprite.AnchorY) * sprite.Height)
        };
        var radians = sprite.Rotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var corners = localCorners.Select(point =>
        {
            var x = point.X * sprite.ScaleX;
            var y = point.Y * sprite.ScaleY;
            return new TMapPoint(
                sprite.X + cos * x - sin * y,
                sprite.Y + sin * x + cos * y);
        }).ToList();
        return new SpriteExportItem(sprite,
            corners.Min(point => point.X),
            corners.Min(point => point.Y),
            corners.Max(point => point.X),
            corners.Max(point => point.Y));
    }

    private static bool SpriteIntersectsChunk(
        SpriteExportBounds bounds,
        double chunkX,
        double chunkY,
        double chunkWidth,
        double chunkHeight)
    {
        return bounds.MinX < chunkX + chunkWidth && bounds.MaxX > chunkX &&
               bounds.MinY < chunkY + chunkHeight && bounds.MaxY > chunkY;
    }

    private sealed class ExportContext : IDisposable
    {
        private readonly Dictionary<string, SKBitmap?> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly SkiaGpuContext? _gpuContext;
        private readonly bool _ownsGpuContext;
        private bool _softwareFallback;

        public ExportContext(
            TMapDocument document,
            SkiaGpuContext? gpuContext,
            bool allowContextCreation)
        {
            Document = document;
            _gpuContext = gpuContext ?? (allowContextCreation ? SkiaGpuContext.TryCreate() : null);
            _ownsGpuContext = gpuContext is null && _gpuContext is not null;
        }

        public TMapDocument Document { get; }
        public bool HardwareAccelerated => _gpuContext is not null && !_softwareFallback;

        public SKSurface CreateSurface(SKImageInfo imageInfo)
        {
            if (_gpuContext is not null)
            {
                try
                {
                    var gpuSurface = _gpuContext.CreateSurface(imageInfo);
                    if (gpuSurface is not null) return gpuSurface;
                }
                catch
                {
                    // GPU 驱动可能拒绝超大纹理，当前 chunk 自动改用软件表面。
                }
            }

            _softwareFallback = true;
            return SKSurface.Create(imageInfo)
                   ?? throw new InvalidOperationException("无法创建 chunk 绘制表面。");
        }

        public SKBitmap LoadBitmap(string imagePath)
        {
            var fullPath = TMapFileService.ResolveImagePath(Document, imagePath);
            if (_bitmapCache.TryGetValue(fullPath, out var cached) && cached is not null) return cached;
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"图片元素引用的文件不存在：{imagePath}", fullPath);
            var bitmap = SKBitmap.Decode(fullPath)
                         ?? throw new InvalidDataException($"无法解码图片文件：{imagePath}");
            _bitmapCache[fullPath] = bitmap;
            return bitmap;
        }

        public void PreloadImages()
        {
            foreach (var imagePath in Document.Sprites.Select(sprite => sprite.ImagePath)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                LoadBitmap(imagePath);
            }
        }

        public void ReleaseGpuContext()
        {
            _gpuContext?.ReleaseCurrent();
        }

        public void Dispose()
        {
            foreach (var bitmap in _bitmapCache.Values)
            {
                bitmap?.Dispose();
            }
            if (_ownsGpuContext) _gpuContext?.Dispose();
        }
    }

    private sealed record SpriteExportItem(TMapSprite Sprite, double MinX, double MinY, double MaxX, double MaxY)
    {
        public SpriteExportBounds Bounds { get; } = new(MinX, MinY, MaxX, MaxY);
    }

    private readonly record struct SpriteExportBounds(double MinX, double MinY, double MaxX, double MaxY);

    private static void Validate(TMapDocument document)
    {
        if (document.Width <= 0 || document.Height <= 0) throw new InvalidDataException("地图宽高必须大于 0。");
        if (document.GridSize <= 0) throw new InvalidDataException("网格尺寸必须大于 0。");
        if (document.ChunkRows <= 0 || document.ChunkColumns <= 0)
            throw new InvalidDataException("Chunk 行列必须大于 0。");
        var columns = (int)Math.Ceiling(document.Width / document.GridSize);
        var rows = (int)Math.Ceiling(document.Height / document.GridSize);
        var invalidCell = document.Cells.FirstOrDefault(cell =>
            cell.Row < 0 || cell.Row >= rows || cell.Column < 0 || cell.Column >= columns);
        if (invalidCell is not null)
            throw new InvalidDataException($"格子 [{invalidCell.Row},{invalidCell.Column}] 超出地图范围。");
        var layerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in document.Layers)
        {
            var name = LayerNameValidator.Validate(layer.Name);
            if (!layerNames.Add(name)) throw new InvalidDataException($"存在重名图层：{name}。");
        }
        var orphan = document.Sprites.FirstOrDefault(sprite => !layerNames.Contains(sprite.Layer));
        if (orphan is not null)
            throw new InvalidDataException($"图片元素“{orphan.Name}”引用了不存在的图层“{orphan.Layer}”。");
    }

    private static void CleanupGeneratedLayerOutputs(TMapDocument document, string outputDirectory)
    {
        var currentLayers = document.Layers
            .Select(layer => LayerNameValidator.Validate(layer.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var layerName in currentLayers)
        {
            CleanupGeneratedLayer(Path.Combine(outputDirectory, layerName), layerName);
        }

        foreach (var layerDirectory in Directory.EnumerateDirectories(outputDirectory))
        {
            var layerName = Path.GetFileName(layerDirectory);
            if (currentLayers.Contains(layerName)) continue;
            var manifestPath = Path.Combine(layerDirectory, "manifest", layerName + ".json");
            if (IsGeneratedLayerManifest(manifestPath))
                CleanupGeneratedLayer(layerDirectory, layerName);
        }
    }

    private static void CleanupGeneratedLayer(string layerDirectory, string layerName)
    {
        var spriteDirectory = Path.Combine(layerDirectory, "sprite");
        if (Directory.Exists(spriteDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(spriteDirectory, "chunk_*_*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(path).Split('_');
                if (name.Length == 3 && int.TryParse(name[1], out _) && int.TryParse(name[2], out _))
                    File.Delete(path);
            }
        }

        var manifestPath = Path.Combine(layerDirectory, "manifest", layerName + ".json");
        if (File.Exists(manifestPath)) File.Delete(manifestPath);
        DeleteDirectoryIfEmpty(Path.Combine(layerDirectory, "sprite"));
        DeleteDirectoryIfEmpty(Path.Combine(layerDirectory, "manifest"));
        DeleteDirectoryIfEmpty(layerDirectory);
    }

    private static bool IsGeneratedLayerManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath)) return false;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = json.RootElement;
            return root.TryGetProperty("originMode", out var originMode) &&
                   originMode.GetString() == "sourceLayerLeftBottom" &&
                   root.TryGetProperty("chunks", out var chunks) && chunks.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void DeleteDirectoryIfEmpty(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            Directory.Delete(path);
    }

    private static void WriteLayerManifest(string path, TMapExportLayerManifest value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, TMapJsonContext.Default.TMapExportLayerManifest) + Environment.NewLine);
    }

    private static void WriteGridManifest(string path, TMapExportGridManifest value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, TMapJsonContext.Default.TMapExportGridManifest) + Environment.NewLine);
    }

}
