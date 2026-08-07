using System.Runtime.InteropServices;

namespace LivePaper.Renderer.Native;

public static unsafe partial class Wpe
{
    private const string FdoLibrary = "libWPEBackend-fdo-1.0.so.1";
    private const string WpeLibrary = "libwpe-1.0.so.1";
    private const string WebKitLibrary = "libWPEWebKit-2.0.so.1";

    [LibraryImport(WpeLibrary, EntryPoint = "wpe_loader_init", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void InitializeLoader(string implementationLibrary);

    [StructLayout(LayoutKind.Sequential)]
    public struct ExportableClient
    {
        public delegate* unmanaged<nint, nint, void> ExportEglImage;
        public delegate* unmanaged<nint, nint, void> ExportFdoEglImage;
        public delegate* unmanaged<nint, nint, void> ExportShmBuffer;
        public nint Reserved0;
        public nint Reserved1;
    }

    [LibraryImport(FdoLibrary, EntryPoint = "wpe_fdo_initialize_for_egl_display")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool InitializeForEglDisplay(nint display);

    [LibraryImport(FdoLibrary, EntryPoint = "wpe_view_backend_exportable_fdo_egl_create")]
    internal static partial nint CreateExportableBackend(
        nint client,
        nint data,
        uint width,
        uint height);

    [LibraryImport(FdoLibrary, EntryPoint = "wpe_view_backend_exportable_fdo_get_view_backend")]
    internal static partial nint GetViewBackend(nint exportable);

    [LibraryImport(FdoLibrary, EntryPoint = "wpe_view_backend_exportable_fdo_dispatch_frame_complete")]
    internal static partial void DispatchFrameComplete(nint exportable);

    [LibraryImport(FdoLibrary, EntryPoint = "wpe_view_backend_exportable_fdo_egl_dispatch_release_exported_image")]
    internal static partial void ReleaseExportedImage(nint exportable, nint image);

    [LibraryImport(FdoLibrary, EntryPoint = "wpe_view_backend_exportable_fdo_destroy")]
    internal static partial void DestroyExportableBackend(nint exportable);

    [LibraryImport(FdoLibrary, EntryPoint = "wpe_fdo_egl_exported_image_get_width")]
    internal static partial uint GetImageWidth(nint image);

    [LibraryImport(FdoLibrary, EntryPoint = "wpe_fdo_egl_exported_image_get_height")]
    internal static partial uint GetImageHeight(nint image);

    [LibraryImport(FdoLibrary, EntryPoint = "wpe_fdo_egl_exported_image_get_egl_image")]
    internal static partial nint GetEglImage(nint image);

    [LibraryImport(WpeLibrary, EntryPoint = "wpe_view_backend_dispatch_set_size")]
    internal static partial void SetSize(nint backend, uint width, uint height);

    [StructLayout(LayoutKind.Sequential)]
    public struct PointerEvent
    {
        public uint Type;
        public uint Time;
        public int X;
        public int Y;
        public uint Button;
        public uint State;
        public uint Modifiers;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AxisEvent
    {
        public uint Type;
        public uint Time;
        public int X;
        public int Y;
        public uint Axis;
        public int Value;
        public uint Modifiers;
    }

    [LibraryImport(WpeLibrary, EntryPoint = "wpe_view_backend_dispatch_pointer_event")]
    internal static partial void DispatchPointerEvent(nint backend, PointerEvent* pointerEvent);

    [LibraryImport(WpeLibrary, EntryPoint = "wpe_view_backend_dispatch_axis_event")]
    internal static partial void DispatchAxisEvent(nint backend, AxisEvent* axisEvent);

    [LibraryImport(WpeLibrary, EntryPoint = "wpe_view_backend_add_activity_state")]
    internal static partial void AddActivityState(nint backend, uint state);

    [LibraryImport(WpeLibrary, EntryPoint = "wpe_view_backend_remove_activity_state")]
    internal static partial void RemoveActivityState(nint backend, uint state);

    [LibraryImport(WpeLibrary, EntryPoint = "wpe_view_backend_get_activity_state")]
    internal static partial uint GetActivityState(nint backend);

    [LibraryImport(WebKitLibrary, EntryPoint = "webkit_web_view_backend_new")]
    internal static partial nint CreateWebViewBackend(nint backend, nint destroyNotify, nint userData);

    [LibraryImport(WebKitLibrary, EntryPoint = "webkit_web_view_get_type")]
    private static partial nuint GetWebViewType();

    [LibraryImport(WebKitLibrary, EntryPoint = "webkit_website_policies_new_with_policies", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CreateWebsitePolicies(string property, int value, nint end);

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_new", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CreateObject(
        nuint objectType,
        string firstProperty,
        nint firstValue,
        string secondProperty,
        nint secondValue,
        nint end);

    public static nint CreateWebViewWithAutoplay(nint backend)
    {
        var policies = CreateWebsitePolicies("autoplay", 0, nint.Zero);
        return CreateObject(
            GetWebViewType(),
            "backend",
            backend,
            "website-policies",
            policies,
            nint.Zero);
    }

    [LibraryImport(WebKitLibrary, EntryPoint = "webkit_web_view_load_uri", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void LoadUri(nint webView, string uri);

    [LibraryImport(WebKitLibrary, EntryPoint = "webkit_web_view_get_settings")]
    internal static partial nint GetSettings(nint webView);

    [LibraryImport(WebKitLibrary, EntryPoint = "webkit_settings_set_media_playback_requires_user_gesture")]
    internal static partial void SetMediaPlaybackRequiresUserGesture(
        nint settings,
        [MarshalAs(UnmanagedType.Bool)] bool required);

    [LibraryImport(WebKitLibrary, EntryPoint = "webkit_web_view_evaluate_javascript", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void EvaluateJavaScript(
        nint webView,
        string script,
        nint length,
        nint worldName,
        nint sourceUri,
        nint cancellable,
        nint callback,
        nint userData);

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")]
    internal static partial void Unref(nint instance);
}
