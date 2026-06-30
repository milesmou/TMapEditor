using Avalonia.Input;
using TMapEditor.Models;

namespace TMapEditor.Controls;

internal static class TMapDragFormats
{
    public static readonly DataFormat<TMapResource> Resource =
        DataFormat.CreateInProcessFormat<TMapResource>("TMapResource");
}
