using LivePaper.Renderer.Native;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LivePaper.Protocol;

namespace LivePaper.Renderer;

public sealed unsafe class WpeDirectRenderer : IDisposable
{
    private const uint VisibleActivityState = 1;
    private const uint FocusedActivityState = 1 << 1;
    private const uint InWindowActivityState = 1 << 2;
    private GCHandle _selfHandle;
    private readonly WaylandPresenter _presenter;
    private readonly bool _diagnosticsEnabled;
    private nint _client;
    private nint _exportable;
    private nint _backend;
    private nint _inFlightImage;
    private long _exportedAt;
    private long _lastExportedAt;
    private long _lastFrameAt;
    private long _lastReportAt = Stopwatch.GetTimestamp();
    private int _frames;
    private uint _pointerModifiers;
    private bool _disposed;
    private readonly double[] _exportIntervals = new double[2048];
    private readonly double[] _frameIntervals = new double[2048];
    private readonly double[] _latencies = new double[2048];

    public WpeDirectRenderer(uint width, uint height, bool diagnosticsEnabled, bool interactive)
    {
        _diagnosticsEnabled = diagnosticsEnabled;
        _selfHandle = GCHandle.Alloc(this);
        _presenter = new WaylandPresenter(width, height, interactive);
        Wpe.InitializeLoader("libWPEBackend-fdo-1.0.so.1");
        if (!Wpe.InitializeForEglDisplay(_presenter.EglDisplay))
        {
            throw new InvalidOperationException("wpebackend-fdo rejected the direct presenter's EGL display.");
        }

        var client = new Wpe.ExportableClient
        {
            ExportEglImage = &OnUntypedImage,
            ExportFdoEglImage = &OnExportedImage,
            ExportShmBuffer = &OnShmBuffer
        };
        _client = (nint)NativeMemory.Alloc((nuint)sizeof(Wpe.ExportableClient));
        *(Wpe.ExportableClient*)_client = client;
        _exportable = Wpe.CreateExportableBackend(
            _client,
            GCHandle.ToIntPtr(_selfHandle),
            width,
            height);
        _backend = Wpe.GetViewBackend(_exportable);
        _presenter.Input += DispatchInput;
        Wpe.SetSize(_backend, width, height);
        var webViewBackend = Wpe.CreateWebViewBackend(_backend, nint.Zero, nint.Zero);
        WebView = Wpe.CreateWebViewWithAutoplay(webViewBackend);
        Wpe.SetMediaPlaybackRequiresUserGesture(Wpe.GetSettings(WebView), false);
    }

    public nint WebView { get; }

    public void Load(string uri) => Wpe.LoadUri(WebView, uri);

    public void DispatchVisibility(VisibilityChanged visibility)
    {
        Wpe.AddActivityState(_backend, InWindowActivityState);
        if (visibility.ShouldRender)
        {
            Wpe.AddActivityState(_backend, VisibleActivityState | FocusedActivityState);
        }
        else
        {
            Wpe.RemoveActivityState(_backend, VisibleActivityState | FocusedActivityState);
        }

        if (_diagnosticsEnabled)
        {
            Console.WriteLine($"WPE activity state: 0x{Wpe.GetActivityState(_backend):x}.");
        }

        var shouldRender = visibility.ShouldRender ? "true" : "false";
        var shouldMute = visibility.ShouldMute ? "true" : "false";
        Wpe.EvaluateJavaScript(
            WebView,
            $"window.livepaper?._setVisibility({{ state: \"{visibility.State}\", " +
            $"shouldRender: {shouldRender}, shouldMute: {shouldMute} }});",
            -1,
            nint.Zero,
            nint.Zero,
            nint.Zero,
            nint.Zero,
            nint.Zero);
    }

    public void DispatchPointerPosition(PointerPositionChanged position) =>
        Wpe.EvaluateJavaScript(
            WebView,
            $"window.livepaper?._setPointerPosition({{ x: {position.X:R}, y: {position.Y:R} }});",
            -1,
            nint.Zero,
            nint.Zero,
            nint.Zero,
            nint.Zero,
            nint.Zero);

    private void DispatchInput(uint type, uint time, int x, int y, uint detail, int value)
    {
        if (type == 3)
        {
            var axisEvent = new Wpe.AxisEvent
            {
                Type = 1,
                Time = time,
                X = x,
                Y = y,
                Axis = detail,
                Value = value,
                Modifiers = _pointerModifiers
            };
            Wpe.DispatchAxisEvent(_backend, &axisEvent);
            return;
        }

        if (type == 2 && detail is >= 1 and <= 5)
        {
            var buttonMask = 1u << (19 + (int)detail);
            if (value != 0)
            {
                _pointerModifiers |= buttonMask;
            }
            else
            {
                _pointerModifiers &= ~buttonMask;
            }
        }

        var pointerEvent = new Wpe.PointerEvent
        {
            Type = type,
            Time = time,
            X = x,
            Y = y,
            Button = detail,
            State = (uint)value,
            Modifiers = _pointerModifiers
        };
        Wpe.DispatchPointerEvent(_backend, &pointerEvent);
    }

    [UnmanagedCallersOnly]
    private static void OnExportedImage(nint data, nint image)
    {
        var renderer = FromHandle(data);
        renderer._inFlightImage = image;
        renderer._exportedAt = Stopwatch.GetTimestamp();
        if (renderer._lastExportedAt != 0)
        {
            renderer._exportIntervals[Math.Min(renderer._frames, 2047)] =
                Stopwatch.GetElapsedTime(renderer._lastExportedAt, renderer._exportedAt).TotalMilliseconds;
        }
        renderer._lastExportedAt = renderer._exportedAt;
        var presented = renderer._presenter.Present(
            Wpe.GetEglImage(image),
            Wpe.GetImageWidth(image),
            Wpe.GetImageHeight(image));
        if (!presented)
        {
            Console.Error.WriteLine("Direct Wayland EGL presentation failed.");
        }

        GLib.ScheduleIdle(renderer.OnFrameDone);
    }

    private void OnFrameDone()
    {
        var now = Stopwatch.GetTimestamp();
        if (_inFlightImage != nint.Zero)
        {
            Wpe.ReleaseExportedImage(_exportable, _inFlightImage);
            _inFlightImage = nint.Zero;
        }

        Wpe.DispatchFrameComplete(_exportable);
        var index = Math.Min(_frames, 2047);
        _latencies[index] = Stopwatch.GetElapsedTime(_exportedAt, now).TotalMilliseconds;
        if (_lastFrameAt != 0)
        {
            _frameIntervals[index] = Stopwatch.GetElapsedTime(_lastFrameAt, now).TotalMilliseconds;
        }
        _frames++;
        if (_diagnosticsEnabled && Stopwatch.GetElapsedTime(_lastReportAt, now) >= TimeSpan.FromSeconds(10))
        {
            Console.WriteLine(
                $"Direct presentation: frames={_frames}, " +
                $"export-ms p50/p95={Percentile(_exportIntervals, 0.50):F2}/{Percentile(_exportIntervals, 0.95):F2}, " +
                $"frame-ms p50/p95={Percentile(_frameIntervals, 0.50):F2}/{Percentile(_frameIntervals, 0.95):F2}, " +
                $"latency-ms p50/p95={Percentile(_latencies, 0.50):F2}/{Percentile(_latencies, 0.95):F2}.");
            _frames = 0;
            _lastReportAt = now;
        }

        _lastFrameAt = now;
    }

    private double Percentile(double[] values, double percentile)
    {
        var count = Math.Min(_frames, values.Length);
        if (count == 0)
        {
            return 0;
        }

        var sorted = values.AsSpan(0, count).ToArray();
        Array.Sort(sorted);
        return sorted[(int)Math.Ceiling(count * percentile) - 1];
    }

    [UnmanagedCallersOnly]
    private static void OnUntypedImage(nint data, nint image) =>
        Console.Error.WriteLine("Direct presenter does not support untyped EGL images.");

    [UnmanagedCallersOnly]
    private static void OnShmBuffer(nint data, nint buffer) =>
        Console.Error.WriteLine("Direct presenter does not support shared-memory exports.");

    private static WpeDirectRenderer FromHandle(nint data) =>
        (WpeDirectRenderer)GCHandle.FromIntPtr(data).Target!;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (WebView != nint.Zero)
        {
            Wpe.Unref(WebView);
        }

        if (_inFlightImage != nint.Zero)
        {
            Wpe.ReleaseExportedImage(_exportable, _inFlightImage);
            _inFlightImage = nint.Zero;
        }

        if (_exportable != nint.Zero)
        {
            Wpe.DestroyExportableBackend(_exportable);
            _exportable = nint.Zero;
            _backend = nint.Zero;
        }

        if (_client != nint.Zero)
        {
            NativeMemory.Free((void*)_client);
            _client = nint.Zero;
        }

        _presenter.Dispose();
        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }
}
