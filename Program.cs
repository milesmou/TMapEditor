using Aprillz.MewUI;
using Aprillz.MewUI.Skia.Interop;
using TMapEditor.Services;

namespace TMapEditor;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(argument => argument.Equals("--export", StringComparison.OrdinalIgnoreCase)))
        {
            return CommandLineExportService.Run(args);
        }

        using var singleInstance = SingleInstanceGuard.TryAcquire();
        if (singleInstance is null)
        {
            return 0;
        }

        if (OperatingSystem.IsWindows())
        {
            Win32Platform.Register();
            Direct2DBackend.Register();
            SkiaDirect2DInterop.Register();
        }
        else if (OperatingSystem.IsMacOS())
        {
            MacOSPlatform.Register();
            MewVGMacOSBackend.Register();
        }
        else
        {
            X11Platform.Register();
            MewVGX11Backend.Register();
        }

        ThemeManager.Default = ThemeVariant.Dark;
        ThemeManager.DefaultAccentColor = new Color(0, 180, 255);
        var mainWindow = new MainWindow();
        var pendingActivation = 0;

        void RestoreAndActivate()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                Interlocked.Exchange(ref pendingActivation, 1);
                return;
            }

            dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (mainWindow.WindowState == Aprillz.MewUI.Controls.WindowState.Minimized)
                {
                    mainWindow.Restore();
                }

                mainWindow.Activate();
            });
        }

        mainWindow.Loaded += () =>
        {
            if (Interlocked.Exchange(ref pendingActivation, 0) != 0)
            {
                RestoreAndActivate();
            }
        };
        singleInstance.ListenForActivation(RestoreAndActivate);
        Application.Run(mainWindow);
        return 0;
    }
}
