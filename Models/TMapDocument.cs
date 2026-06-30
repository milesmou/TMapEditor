using System.Text.Json.Serialization;

namespace TMapEditor.Models;

public interface IDisplayItem
{
    string DisplayName { get; }
}

public interface ILockableDisplayItem : IDisplayItem
{
    bool IsLocked { get; set; }
    string LockIcon { get; }
}

public sealed class TMapDocument
{
    public int FormatVersion { get; set; } = 2;
    public string Name { get; set; } = "NewMap";
    public double Width { get; set; } = 4500;
    public double Height { get; set; } = 4002;
    public double GridSize { get; set; } = 32;
    public int ChunkRows { get; set; } = 3;
    public int ChunkColumns { get; set; } = 6;
    public List<TMapLayer> Layers { get; set; } = [];
    public List<TMapSprite> Sprites { get; set; } = [];
    public List<TMapResource> Resources { get; set; } = [];
    public List<TMapCell> Cells { get; set; } = [];
    public List<TMapObject> Objects { get; set; } = [];

    [JsonIgnore]
    public string? FilePath { get; set; }

    [JsonIgnore]
    public string BaseDirectory => FilePath is null
        ? Environment.CurrentDirectory
        : Path.GetDirectoryName(FilePath) ?? Environment.CurrentDirectory;
}

public sealed class TMapResource : IDisplayItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Image";
    public string ImagePath { get; set; } = "";

    [JsonIgnore]
    public string ThumbnailPath { get; set; } = "";

    [JsonIgnore]
    public string DisplayName => Name;
}

public enum TMapLayerType
{
    Image,
    Object
}

public sealed class TMapLayer
{
    public string Name { get; set; } = "Layer";
    public bool Visible { get; set; } = true;
    public TMapLayerType Type { get; set; }

    [JsonIgnore]
    public string TypeIcon => Type == TMapLayerType.Object ? "◆" : "🖼";
}

public sealed class TMapSprite : ILockableDisplayItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Sprite";
    public string Layer { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 100;
    public double Height { get; set; } = 100;
    public double Rotation { get; set; }
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public double AnchorX { get; set; } = 0.5;
    public double AnchorY { get; set; } = 0.5;
    public int Order { get; set; }
    public bool IsLocked { get; set; }

    [JsonIgnore]
    public string DisplayName => $"🖼 {Name}  [{Layer}]";

    [JsonIgnore]
    public string LockIcon => IsLocked ? "🔒" : "🔓";
}

public enum TMapCellState
{
    Walk,
    Block
}

public sealed class TMapCell
{
    public int Row { get; set; }
    public int Column { get; set; }
    public TMapCellState State { get; set; }
}

public sealed class TMapObject : ILockableDisplayItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Object";
    public string Layer { get; set; } = "";
    public string Args { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public string DisplayColor { get; set; } = "#00BFFF";
    public bool IsLocked { get; set; }

    [JsonIgnore]
    public string DisplayName => $"◆ {Name}";

    [JsonIgnore]
    public string LockIcon => IsLocked ? "🔒" : "🔓";
}

public sealed class TMapPoint
{
    public double X { get; set; }
    public double Y { get; set; }

    public TMapPoint()
    {
    }

    public TMapPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}
