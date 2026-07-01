using Avalonia;

namespace TMapEditor;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(argument => argument.Equals("--export", StringComparison.OrdinalIgnoreCase)))
        {
            return App.RunExportCommand(args);
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.AngleEgl]
            })
            .With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = [AvaloniaNativeRenderingMode.Metal]
            })
            .WithInterFont()
            .LogToTrace();
    }
}
