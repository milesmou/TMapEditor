namespace TMapEditor.Controls;

public enum EditorTool
{
    Select,
    WalkBrush,
    BlockBrush,
    EraseBrush,
    CellZBrush,
    EraseCellZBrush
}

[Flags]
internal enum ResizeHandle
{
    None = 0,
    Left = 1,
    Right = 2,
    Bottom = 4,
    Top = 8
}

public sealed class MapCellHoverEventArgs(int? row, int? column) : EventArgs
{
    public int? Row { get; } = row;
    public int? Column { get; } = column;
    public bool IsInsideMap => Row.HasValue && Column.HasValue;
}
