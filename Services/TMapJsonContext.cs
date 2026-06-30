using System.Text.Json.Serialization;
using TMapEditor.Models;

namespace TMapEditor.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TMapDocument))]
[JsonSerializable(typeof(EditorSettings))]
[JsonSerializable(typeof(List<TMapExportChunkManifest>))]
[JsonSerializable(typeof(List<TMapExportObjectManifest>))]
[JsonSerializable(typeof(List<TMapExportObjectImageManifest>))]
[JsonSerializable(typeof(TMapExportObjectLayerManifest))]
[JsonSerializable(typeof(TMapExportGridManifest))]
[JsonSerializable(typeof(TMapExportGridPathManifest))]
internal sealed partial class TMapJsonContext : JsonSerializerContext;
