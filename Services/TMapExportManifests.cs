namespace TMapEditor.Services;

internal sealed record TMapExportChunkManifest(
    int Row,
    int Col,
    double X,
    double Y,
    string File);

internal sealed record TMapExportLayerManifest(
    string GeneratedAt,
    string? TmapFile,
    string SourceLayerPath,
    string BoundsLayerPath,
    double ChunkWidth,
    double ChunkHeight,
    int Rows,
    int Columns,
    string OriginMode,
    double SourceLayerWidth,
    double SourceLayerHeight,
    List<TMapExportChunkManifest> Chunks);

internal sealed record TMapExportObjectManifest(
    string Name,
    int Row,
    int Col,
    int ChunkRow,
    int ChunkCol,
    string? Args);

internal sealed record TMapExportGridManifest(
    string GeneratedAt,
    string? TmapFile,
    string SourceLayerPath,
    string GridTargetPath,
    string ExportType,
    double GridSize,
    int Rows,
    int Columns,
    int ChunkRows,
    int ChunkColumns,
    string OriginMode,
    double SourceLayerWidth,
    double SourceLayerHeight,
    List<TMapExportObjectManifest> Objects,
    List<int[]>? WalkableCells,
    List<int[]>? BlockedCells);
