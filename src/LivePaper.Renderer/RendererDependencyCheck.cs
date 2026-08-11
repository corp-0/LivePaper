using System.Runtime.InteropServices;

namespace LivePaper.Renderer;

internal static class RendererDependencyCheck
{
    private static readonly string[] SystemLibraries =
    [
        "libWPEBackend-fdo-1.0.so.1",
        "libwpe-1.0.so.1",
        "libWPEWebKit-2.0.so.1",
        "libgobject-2.0.so.0",
        "libglib-2.0.so.0",
        "libwayland-client.so.0",
        "libwayland-egl.so.1",
        "libEGL.so.1",
        "libGLESv2.so.2"
    ];

    public static void Run()
    {
        foreach (var library in SystemLibraries)
        {
            Load(library, library);
        }

        var presenter = Path.Combine(AppContext.BaseDirectory, "liblivepaper-wayland.so");
        Load(presenter, "liblivepaper-wayland.so");
    }

    private static void Load(string path, string displayName)
    {
        if (!NativeLibrary.TryLoad(path, out var handle))
        {
            throw new DllNotFoundException($"Missing native library: {displayName}");
        }

        NativeLibrary.Free(handle);
    }
}
