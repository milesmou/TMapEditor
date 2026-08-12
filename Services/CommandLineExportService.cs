namespace TMapEditor.Services;

internal static class CommandLineExportService
{
    private const int SuccessExitCode = 0;
    private const int FailureExitCode = 1;
    private const int InvalidArgumentsExitCode = 2;

    public static int Run(IReadOnlyList<string> arguments)
    {
        try
        {
            if (!TryGetOptionValue(arguments, "--export", out var inputArgument) ||
                !TryGetOptionValue(arguments, "--output", out var outputArgument))
            {
                Console.Error.WriteLine(
                    "用法: TMapEditor.exe --export <地图.tmap> --output <输出目录>");
                return InvalidArgumentsExitCode;
            }

            var inputPath = Path.GetFullPath(inputArgument);
            var outputDirectory = Path.GetFullPath(outputArgument);
            var document = TMapFileService.Load(inputPath);
            using var gpuContext = SkiaGpuContext.TryCreate();
            var result = Task.Run(() =>
                    TMapExporter.Export(document, outputDirectory, gpuContext, false))
                .GetAwaiter().GetResult();

            Console.WriteLine(
                $"导出完成: {result.ChunkCount} chunks, {result.WalkableCount} 可行走格, " +
                $"{result.BlockedCount} 阻挡格, {result.ObjectCount} 对象, " +
                $"{result.DynamicImageCount} 动态图片, " +
                $"渲染: {(result.HardwareAccelerated ? "GPU" : "CPU")}");
            return SuccessExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"导出失败: {exception.Message}");
            return FailureExitCode;
        }
    }

    private static bool TryGetOptionValue(
        IReadOnlyList<string> arguments,
        string option,
        out string value)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (!arguments[index].Equals(option, StringComparison.OrdinalIgnoreCase)) continue;
            value = arguments[index + 1];
            return true;
        }

        value = "";
        return false;
    }
}
