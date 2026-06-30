namespace TMapEditor.Services;

using System.Text.Json.Serialization;
using TMapEditor.Models;

internal sealed record TMapExportChunkManifest(
    [property: JsonPropertyName("Row")] int Row,
    [property: JsonPropertyName("Col")] int Col,
    [property: JsonPropertyName("X")] double X,
    [property: JsonPropertyName("Y")] double Y,
    [property: JsonPropertyName("File")] string File);

internal sealed record TMapExportObjectManifest(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Row")] int Row,
    [property: JsonPropertyName("Col")] int Col,
    [property: JsonPropertyName("ChunkRow")] int ChunkRow,
    [property: JsonPropertyName("ChunkCol")] int ChunkCol,
    [property: JsonPropertyName("Z")] int Z,
    [property: JsonPropertyName("Args")] string? Args);

internal sealed record TMapExportObjectImageManifest(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("File")] string File,
    [property: JsonPropertyName("X")] double X,
    [property: JsonPropertyName("Y")] double Y,
    [property: JsonPropertyName("Z")] int Z);

internal sealed record TMapExportObjectLayerManifest(
    [property: JsonPropertyName("Objects")] List<TMapExportObjectManifest> Objects,
    [property: JsonPropertyName("Images")] List<TMapExportObjectImageManifest> Images);

internal sealed record TMapExportLayerInfo(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Type")] TMapLayerType Type);

internal sealed record TMapExportGridManifest(
    [property: JsonPropertyName("GeneratedAt")] string GeneratedAt,
    [property: JsonPropertyName("TmapFile")] string? TmapFile,
    [property: JsonPropertyName("ExportType")] string ExportType,
    [property: JsonPropertyName("Layers")] List<TMapExportLayerInfo> Layers,
    [property: JsonPropertyName("GridSize")] double GridSize,
    [property: JsonPropertyName("Rows")] int Rows,
    [property: JsonPropertyName("Columns")] int Columns,
    [property: JsonPropertyName("ChunkRows")] int ChunkRows,
    [property: JsonPropertyName("ChunkColumns")] int ChunkColumns,
    [property: JsonPropertyName("OriginMode")] string OriginMode,
    [property: JsonPropertyName("MapWidth")] double MapWidth,
    [property: JsonPropertyName("MapHeight")] double MapHeight);

internal sealed record TMapExportGridPathManifest(
    [property: JsonPropertyName("GeneratedAt")] string GeneratedAt,
    [property: JsonPropertyName("TmapFile")] string? TmapFile,
    [property: JsonPropertyName("ExportType")] string ExportType,
    [property: JsonPropertyName("GridSize")] double GridSize,
    [property: JsonPropertyName("Rows")] int Rows,
    [property: JsonPropertyName("Columns")] int Columns,
    [property: JsonPropertyName("OriginMode")] string OriginMode,
    [property: JsonPropertyName("MapWidth")] double MapWidth,
    [property: JsonPropertyName("MapHeight")] double MapHeight,
    [property: JsonPropertyName("WalkableCells")] List<int[]>? WalkableCells,
    [property: JsonPropertyName("BlockedCells")] List<int[]>? BlockedCells,
    [property: JsonPropertyName("ZCells")] List<int[]>? ZCells);
