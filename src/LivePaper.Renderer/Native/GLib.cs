using System.Runtime.InteropServices;

namespace LivePaper.Renderer.Native;

public static partial class GLib
{
    private const string Library = "libglib-2.0.so.0";

    public static MainLoop CreateMainLoop() => new(CreateMainLoop(nint.Zero, false));

    [LibraryImport(Library, EntryPoint = "g_main_loop_new")]
    private static partial nint CreateMainLoop(nint context, [MarshalAs(UnmanagedType.Bool)] bool isRunning);

    [LibraryImport(Library, EntryPoint = "g_main_loop_run")]
    private static partial void RunMainLoop(nint loop);

    [LibraryImport(Library, EntryPoint = "g_main_loop_quit")]
    private static partial void QuitMainLoop(nint loop);

    [LibraryImport(Library, EntryPoint = "g_main_loop_unref")]
    private static partial void UnreferenceMainLoop(nint loop);

    public static unsafe void Invoke(Action action)
    {
        var handle = GCHandle.Alloc(action);
        InvokeOnMainContext(nint.Zero, &RunAction, GCHandle.ToIntPtr(handle));
    }

    public static unsafe void ScheduleIdle(Action action)
    {
        var handle = GCHandle.Alloc(action);
        if (AddIdle(&RunAction, GCHandle.ToIntPtr(handle)) == 0)
        {
            handle.Free();
            throw new InvalidOperationException("GLib could not schedule an idle callback.");
        }
    }

    [LibraryImport(Library, EntryPoint = "g_idle_add")]
    private static unsafe partial uint AddIdle(
        delegate* unmanaged<nint, int> function,
        nint data);

    [LibraryImport(Library, EntryPoint = "g_main_context_invoke")]
    private static unsafe partial void InvokeOnMainContext(
        nint context,
        delegate* unmanaged<nint, int> function,
        nint data);

    [UnmanagedCallersOnly]
    private static int RunAction(nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        try
        {
            ((Action)handle.Target!).Invoke();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"GTK dispatch failed: {exception.Message}");
        }
        finally
        {
            handle.Free();
        }

        return 0;
    }

    public sealed class MainLoop(nint handle) : IDisposable
    {
        private nint _handle = handle;

        public void Run() => RunMainLoop(_handle);

        public void Quit() => QuitMainLoop(_handle);

        public void Dispose()
        {
            if (_handle == nint.Zero)
            {
                return;
            }

            UnreferenceMainLoop(_handle);
            _handle = nint.Zero;
        }
    }
}
