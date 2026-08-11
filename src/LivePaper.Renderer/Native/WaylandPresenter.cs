using System.Runtime.InteropServices;

namespace LivePaper.Renderer.Native;

public sealed unsafe partial class WaylandPresenter : IDisposable
{
    private const string Library = "liblivepaper-wayland.so";
    private const int IoIn = 1;
    private const int IoError = 8;
    private const int IoHangup = 16;
    private readonly nint _handle;
    private uint _source;

    public WaylandPresenter(uint width, uint height, bool interactive)
    {
        var selfHandle = GCHandle.Alloc(this);
        _handle = Create(width, height, interactive ? 1 : 0);
        if (_handle == nint.Zero)
        {
            selfHandle.Free();
            throw new InvalidOperationException("Could not create the direct bottom-layer Wayland/EGL presenter.");
        }

        _source = AddUnixFd(
            GetFileDescriptor(_handle),
            IoIn | IoError | IoHangup,
            &OnWaylandReady,
            GCHandle.ToIntPtr(selfHandle));
        SetInputCallback(_handle, &OnInput, GCHandle.ToIntPtr(selfHandle));
    }

    public event Action<uint, uint, int, int, uint, int>? Input;

    public nint EglDisplay => GetEglDisplay(_handle);

    public bool Present(nint image, uint width, uint height) =>
        Present(_handle, image, width, height);

    public bool PresentFallback(string message) => PresentFallback(_handle, message);

    public static bool SupportsLayerShell() => ProbeLayerShell();

    [UnmanagedCallersOnly]
    private static int OnWaylandReady(int fd, int condition, nint data)
    {
        var presenter = (WaylandPresenter)GCHandle.FromIntPtr(data).Target!;
        return Dispatch(presenter._handle) >= 0 ? 1 : 0;
    }

    [UnmanagedCallersOnly]
    private static void OnInput(
        nint data, uint type, uint time, int x, int y, uint detail, int value)
    {
        var presenter = (WaylandPresenter)GCHandle.FromIntPtr(data).Target!;
        presenter.Input?.Invoke(type, time, x, y, detail, value);
    }

    [LibraryImport(Library, EntryPoint = "lp_presenter_create")]
    private static partial nint Create(uint width, uint height, int interactive);

    [LibraryImport(Library, EntryPoint = "lp_probe_layer_shell")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ProbeLayerShell();

    [LibraryImport(Library, EntryPoint = "lp_presenter_set_input_callback")]
    private static partial void SetInputCallback(
        nint presenter,
        delegate* unmanaged<nint, uint, uint, int, int, uint, int, void> callback,
        nint data);

    [LibraryImport(Library, EntryPoint = "lp_presenter_egl_display")]
    private static partial nint GetEglDisplay(nint presenter);

    [LibraryImport(Library, EntryPoint = "lp_presenter_fd")]
    private static partial int GetFileDescriptor(nint presenter);

    [LibraryImport(Library, EntryPoint = "lp_presenter_dispatch")]
    private static partial int Dispatch(nint presenter);

    [LibraryImport(Library, EntryPoint = "lp_presenter_present")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Present(
        nint presenter,
        nint image,
        uint width,
        uint height);

    [LibraryImport(Library, EntryPoint = "lp_presenter_present_fallback", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PresentFallback(nint presenter, string message);

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_unix_fd_add")]
    private static partial uint AddUnixFd(
        int fd,
        int condition,
        delegate* unmanaged<int, int, nint, int> callback,
        nint data);

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_source_remove")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveSource(uint source);

    public void Dispose()
    {
        if (_source != 0)
        {
            _ = RemoveSource(_source);
            _source = 0;
        }

        // Native callbacks may still be queued, so teardown waits for this one-view process to exit.
    }
}
