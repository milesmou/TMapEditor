using TMapEditor.Models;

namespace TMapEditor.Services;

public readonly record struct BlockedRegionOptimizationResult(
    int AddedBlockedCells,
    int AddedWalkableCells,
    bool HasWalkableSeed);

public static class BlockedRegionOptimizer
{
    private static readonly (int Row, int Column)[] NeighborOffsets =
    [
        (-1, 0),
        (1, 0),
        (0, -1),
        (0, 1)
    ];

    public static BlockedRegionOptimizationResult Optimize(TMapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Width <= 0 || document.Height <= 0 || document.GridSize <= 0)
            throw new InvalidOperationException("地图宽高和网格尺寸必须大于 0。");

        var rows = checked((int)Math.Ceiling(document.Height / document.GridSize));
        var columns = checked((int)Math.Ceiling(document.Width / document.GridSize));
        var blocked = document.Cells
            .Where(cell => cell.State == TMapCellState.Block && IsInside(cell.Row, cell.Column, rows, columns))
            .Select(cell => (cell.Row, cell.Column))
            .ToHashSet();
        var walkableSeeds = document.Cells
            .Where(cell => cell.State == TMapCellState.Walk && IsInside(cell.Row, cell.Column, rows, columns))
            .Select(cell => (cell.Row, cell.Column))
            .Distinct()
            .ToList();
        if (walkableSeeds.Count == 0) return new BlockedRegionOptimizationResult(0, 0, false);

        var reachable = walkableSeeds.ToHashSet();
        var pending = new Queue<(int Row, int Column)>(walkableSeeds);
        while (pending.TryDequeue(out var current))
        {
            foreach (var offset in NeighborOffsets)
            {
                var neighbor = (Row: current.Row + offset.Row, Column: current.Column + offset.Column);
                if (!IsInside(neighbor.Row, neighbor.Column, rows, columns) ||
                    blocked.Contains(neighbor) || !reachable.Add(neighbor))
                    continue;
                pending.Enqueue(neighbor);
            }
        }

        var marked = document.Cells
            .Where(cell => IsInside(cell.Row, cell.Column, rows, columns))
            .Select(cell => (cell.Row, cell.Column))
            .ToHashSet();
        var addedBlocked = 0;
        var addedWalkable = 0;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var position = (Row: row, Column: column);
                if (marked.Contains(position)) continue;
                var state = reachable.Contains(position) ? TMapCellState.Walk : TMapCellState.Block;
                document.Cells.Add(new TMapCell
                {
                    Row = row,
                    Column = column,
                    State = state
                });
                if (state == TMapCellState.Walk) addedWalkable++;
                else addedBlocked++;
            }
        }

        return new BlockedRegionOptimizationResult(addedBlocked, addedWalkable, true);
    }

    private static bool IsInside(int row, int column, int rows, int columns) =>
        row >= 0 && row < rows && column >= 0 && column < columns;
}
