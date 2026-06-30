using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SkiaSharp;

namespace TMapEditor.Services;

internal sealed class SkiaGpuContext : IDisposable
{
    private NativeWindow? _window;
    private GRGlInterface? _glInterface;
    private GRContext? _context;

    private SkiaGpuContext()
    {
        try
        {
            var settings = new NativeWindowSettings
            {
                API = ContextAPI.OpenGL,
                APIVersion = new Version(3, 3),
                Profile = ContextProfile.Core,
                Flags = ContextFlags.ForwardCompatible,
                ClientSize = new Vector2i(1, 1),
                StartVisible = false,
                StartFocused = false,
                WindowBorder = WindowBorder.Hidden,
                Title = "TMapEditor GPU Export"
            };

            _window = new NativeWindow(settings);
            _window.Context.MakeCurrent();
            _glInterface = GRGlInterface.Create(GLFW.GetProcAddress);
            if (_glInterface is null || !_glInterface.Validate())
                throw new InvalidOperationException("无法创建 Skia OpenGL 接口。");

            _context = GRContext.CreateGl(_glInterface)
                       ?? throw new InvalidOperationException("无法创建 Skia GPU 上下文。");
            _window.Context.MakeNoneCurrent();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public static SkiaGpuContext? TryCreate()
    {
        try
        {
            return new SkiaGpuContext();
        }
        catch
        {
            return null;
        }
    }

    public SKSurface? CreateSurface(SKImageInfo imageInfo)
    {
        _window!.Context.MakeCurrent();
        return SKSurface.Create(_context!, false, imageInfo);
    }

    public void ReleaseCurrent()
    {
        _window?.Context.MakeNoneCurrent();
    }

    public void Dispose()
    {
        if (_window is not null)
        {
            _window.Context.MakeCurrent();
        }

        _context?.Dispose();
        _context = null;
        _glInterface?.Dispose();
        _glInterface = null;
        _window?.Dispose();
        _window = null;
    }
}
