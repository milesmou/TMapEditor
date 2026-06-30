using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TMapEditor.Services;

namespace TMapEditor;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static int RunExportCommand(IReadOnlyList<string> arguments)
    {
        try
        {
            var exportIndex = IndexOf(arguments, "--export");
            var outputIndex = IndexOf(arguments, "--output");
            if (exportIndex < 0 || exportIndex + 1 >= arguments.Count ||
                outputIndex < 0 || outputIndex + 1 >= arguments.Count)
            {
                Console.Error.WriteLine(
                    "用法: TMapEditor.exe --export <地图.tmap> --output <输出目录>");
                return 2;
            }

            var inputPath = Path.GetFullPath(arguments[exportIndex + 1]);
            var outputDirectory = Path.GetFullPath(arguments[outputIndex + 1]);
            var document = TMapFileService.Load(inputPath);
            using var gpuContext = SkiaGpuContext.TryCreate();
            var result = Task.Run(() =>
                    TMapExporter.Export(document, outputDirectory, gpuContext, false))
                .GetAwaiter().GetResult();
            Console.WriteLine(
                $"导出完成: {result.ChunkCount} chunks, {result.WalkableCount} 可行走格, " +
                $"{result.BlockedCount} 阻挡格, {result.ObjectCount} 对象, " +
                $"渲染: {(result.HardwareAccelerated ? "GPU" : "CPU")}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"导出失败: {exception.Message}");
            return 1;
        }
    }

    private static int IndexOf(IReadOnlyList<string> arguments, string option)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index].Equals(option, StringComparison.OrdinalIgnoreCase)) return index;
        }
        return -1;
    }
}
