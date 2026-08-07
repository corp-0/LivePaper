using System.Net;
using System.Net.Sockets;
using System.Text;
using LivePaper.Protocol;

namespace LivePaper.Renderer;

public sealed class WallpaperHttpServer : IDisposable
{
    private readonly string _root;
    private readonly string _entryPath;
    private readonly TcpListener _listener;
    private readonly bool _logRequests;
    private readonly string? _bootstrapPropertiesJson;
    private readonly VisibilityChanged? _bootstrapVisibility;
    private readonly PointerPositionChanged? _bootstrapPointerPosition;
    private readonly bool _force2DTransforms;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _serverTask;

    private WallpaperHttpServer(
        string root,
        string entry,
        bool logRequests,
        string? bootstrapPropertiesJson,
        VisibilityChanged? bootstrapVisibility,
        PointerPositionChanged? bootstrapPointerPosition,
        bool force2DTransforms)
    {
        _root = root;
        _entryPath = Path.GetFullPath(entry, root);
        _logRequests = logRequests;
        _bootstrapPropertiesJson = bootstrapPropertiesJson;
        _bootstrapVisibility = bootstrapVisibility;
        _bootstrapPointerPosition = bootstrapPointerPosition;
        _force2DTransforms = force2DTransforms;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        EntryUri = new Uri($"http://127.0.0.1:{port}/{Uri.EscapeDataString(entry).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase)}");
        _serverTask = RunAsync(_shutdown.Token);
    }

    public Uri EntryUri { get; }

    public static WallpaperHttpServer Start(
        string root,
        string entry,
        bool logRequests = false,
        string? bootstrapPropertiesJson = null,
        VisibilityChanged? bootstrapVisibility = null,
        PointerPositionChanged? bootstrapPointerPosition = null,
        bool force2DTransforms = false) =>
        new(root, entry, logRequests, bootstrapPropertiesJson, bootstrapVisibility, bootstrapPointerPosition, force2DTransforms);

    public void Dispose()
    {
        _shutdown.Cancel();
        _listener.Stop();
        try
        {
            _serverTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(
                    stream,
                    Encoding.ASCII,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 4096,
                    leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(cancellationToken);
                if (requestLine is null)
                {
                    return;
                }

                var parts = requestLine.Split(' ', 3);
                if (parts is not [("GET" or "HEAD"), _, _])
                {
                    await Console.Error.WriteLineAsync($"Wallpaper HTTP 405: {requestLine}");
                    await WriteStatusAsync(stream, 405, "Method Not Allowed", cancellationToken);
                    return;
                }

                string? range = null;
                while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 } header)
                {
                    if (header.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                    {
                        range = header[6..].Trim();
                    }
                }

                var requestUri = new Uri($"http://localhost{parts[1]}");
                var requestPath = requestUri.AbsolutePath;
                if (requestPath == "/__livepaper_benchmark_report")
                {
                    Console.WriteLine($"WPE benchmark report: {Uri.UnescapeDataString(requestUri.Query.TrimStart('?'))}");
                    await WriteStatusAsync(stream, 204, "No Content", cancellationToken);
                    return;
                }

                var relativePath = Uri.UnescapeDataString(requestPath).TrimStart('/');
                var filePath = Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), _root);
                var relative = Path.GetRelativePath(_root, filePath);
                if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    !File.Exists(filePath))
                {
                    await Console.Error.WriteLineAsync($"Wallpaper HTTP 404: {requestPath}");
                    await WriteStatusAsync(stream, 404, "Not Found", cancellationToken);
                    return;
                }

                if (_logRequests)
                {
                    Console.WriteLine($"Wallpaper HTTP 200: {requestPath}");
                }
                if ((_bootstrapPropertiesJson is not null || _bootstrapVisibility is not null ||
                    _bootstrapPointerPosition is not null || _force2DTransforms) && filePath == _entryPath)
                {
                    await SendEntryWithBootstrapAsync(
                        stream,
                        filePath,
                        parts[0] == "HEAD",
                        _bootstrapPropertiesJson,
                        _bootstrapVisibility,
                        _bootstrapPointerPosition,
                        _force2DTransforms,
                        cancellationToken);
                }
                else
                {
                    await SendFileAsync(stream, filePath, parts[0] == "HEAD", range, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or UriFormatException)
            {
            }
        }
    }

    private static async Task SendEntryWithBootstrapAsync(
        NetworkStream stream,
        string filePath,
        bool headersOnly,
        string? propertiesJson,
        VisibilityChanged? visibility,
        PointerPositionChanged? pointerPosition,
        bool force2DTransforms,
        CancellationToken cancellationToken)
    {
        var html = await File.ReadAllTextAsync(filePath, cancellationToken);
        var properties = propertiesJson ?? "null";
        var visibilityState = visibility?.State.ToString() ?? VisibilityState.Visible.ToString();
        var shouldRender = visibility?.ShouldRender is not false ? "true" : "false";
        var shouldMute = visibility?.ShouldMute is true ? "true" : "false";
        var initialPointerPosition = pointerPosition is null
            ? "null"
            : $"Object.freeze({{ x: {pointerPosition.X:R}, y: {pointerPosition.Y:R} }})";
        var force2D = force2DTransforms ? "true" : "false";
        var script = $$"""
            <script>
            (() => {
              if ({{force2D}}) {
                const to2D = value => typeof value === "string"
                  ? value.replace(/translate3d\(\s*([^,]+),\s*([^,]+),\s*0(?:px)?\s*\)/gi, "translate($1, $2)")
                  : value;
                const style = CSSStyleDeclaration.prototype;
                for (const property of ["transform", "webkitTransform"]) {
                  const descriptor = Object.getOwnPropertyDescriptor(style, property);
                  if (!descriptor?.set) continue;
                  Object.defineProperty(style, property, {
                    ...descriptor,
                    set(value) { descriptor.set.call(this, to2D(value)); }
                  });
                }
                const nativeSetProperty = style.setProperty;
                style.setProperty = function (property, value, priority) {
                  return nativeSetProperty.call(
                    this,
                    property,
                    property === "transform" || property === "-webkit-transform" ? to2D(value) : value,
                    priority);
                };
              }
              const properties = {{properties}};
              let visibility = Object.freeze({
                state: "{{visibilityState}}",
                shouldRender: {{shouldRender}},
                shouldMute: {{shouldMute}}
              });
              const mediaElements = new Set();
              const nativeMediaPlay = HTMLMediaElement.prototype.play;
              HTMLMediaElement.prototype.play = function (...args) {
                mediaElements.add(this);
                this.muted = visibility.shouldMute;
                return nativeMediaPlay.apply(this, args);
              };
              const livepaper = new EventTarget();
              let pointerPosition = {{initialPointerPosition}};
              Object.defineProperty(livepaper, "visibility", { get: () => visibility, enumerable: true });
              Object.defineProperty(livepaper, "pointerPosition", { get: () => pointerPosition, enumerable: true });
              Object.defineProperty(livepaper, "_setPointerPosition", { value: next => {
                pointerPosition = Object.freeze(next);
                livepaper.dispatchEvent(new CustomEvent("pointerpositionchange", { detail: pointerPosition }));
                const target = document.elementFromPoint(pointerPosition.x, pointerPosition.y) ?? window;
                target.dispatchEvent(new MouseEvent("mousemove", {
                  bubbles: true,
                  composed: true,
                  clientX: pointerPosition.x,
                  clientY: pointerPosition.y,
                  screenX: pointerPosition.x,
                  screenY: pointerPosition.y
                }));
              } });
              Object.defineProperty(livepaper, "_setVisibility", { value: next => {
                visibility = Object.freeze(next);
                for (const media of mediaElements) media.muted = visibility.shouldMute;
                livepaper.dispatchEvent(new CustomEvent("visibilitychange", { detail: visibility }));
              } });
              Object.defineProperty(window, "livepaper", { value: livepaper, enumerable: true });
              const metrics = {
                raf: 0,
                clear: 0,
                drawArrays: 0,
                drawElements: 0,
                mousemove: 0,
                mousedown: 0,
                mouseup: 0,
                click: 0,
                wheel: 0,
                mousemoveTarget: null
              };
              window.addEventListener("mousemove", event => {
                metrics.mousemove++;
                metrics.mousemoveTarget = event.target?.id || event.target?.tagName || null;
              });
              for (const type of ["mousedown", "mouseup", "click", "wheel"]) {
                window.addEventListener(type, () => metrics[type]++);
              }
              const nativeRequestAnimationFrame = window.requestAnimationFrame.bind(window);
              window.requestAnimationFrame = callback => nativeRequestAnimationFrame(timestamp => {
                metrics.raf++;
                callback(timestamp);
              });
              for (const prototype of [
                globalThis.WebGLRenderingContext?.prototype,
                globalThis.WebGL2RenderingContext?.prototype
              ]) {
                if (!prototype) continue;
                for (const method of ["clear", "drawArrays", "drawElements"]) {
                  const nativeMethod = prototype[method];
                  if (typeof nativeMethod !== "function") continue;
                  prototype[method] = function (...args) {
                    metrics[method]++;
                    return nativeMethod.apply(this, args);
                  };
                }
              }
              const apply = () => {
                if (!properties) return;
                const listener = window.wallpaperPropertyListener;
                if (typeof listener?.applyUserProperties === "function") {
                  listener.applyUserProperties(properties);
                } else {
                  window.setTimeout(apply, 0);
                }
              };
              window.addEventListener("DOMContentLoaded", () => {
                if (pointerPosition) livepaper._setPointerPosition(pointerPosition);
                apply();
              }, { once: true });
              window.setTimeout(() => {
                const canvas = document.querySelector("canvas");
                const context = canvas?.getContext("webgl") ?? canvas?.getContext("experimental-webgl");
                const report = {
                  ...metrics,
                  pointerPosition: livepaper.pointerPosition,
                  layers: Array.from(document.querySelectorAll(".layer"), layer => layer.style.transform),
                  canvas: canvas ? `${canvas.width}x${canvas.height}` : null,
                  webgl: context ? context.getParameter(context.RENDERER) : null,
                  media: Array.from(document.querySelectorAll("audio,video"), element => ({
                    paused: element.paused,
                    muted: element.muted,
                    volume: element.volume,
                    readyState: element.readyState,
                    error: element.error?.message ?? null
                  })),
                  bgm: typeof bgm === "undefined" || !bgm ? null : {
                    paused: bgm.paused,
                    muted: bgm.muted,
                    volume: bgm.volume,
                    readyState: bgm.readyState,
                    error: bgm.error?.message ?? null
                  }
                };
                fetch(`/__livepaper_benchmark_report?${encodeURIComponent(JSON.stringify(report))}`);
              }, 10000);
            })();
            </script>
            """;
        var body = Encoding.UTF8.GetBytes(script + html);
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-cache\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(response, cancellationToken);
        if (!headersOnly)
        {
            await stream.WriteAsync(body, cancellationToken);
        }
    }

    private static async Task SendFileAsync(
        NetworkStream stream,
        string filePath,
        bool headersOnly,
        string? range,
        CancellationToken cancellationToken)
    {
        await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var start = 0L;
        var end = file.Length - 1;
        var partial = TryParseRange(range, file.Length, out var rangeStart, out var rangeEnd);
        if (partial)
        {
            start = rangeStart;
            end = rangeEnd;
        }

        var length = Math.Max(0, end - start + 1);
        var response = new StringBuilder()
            .Append("HTTP/1.1 ").Append(partial ? "206 Partial Content" : "200 OK").Append("\r\n")
            .Append("Content-Type: ").Append(GetContentType(filePath)).Append("\r\n")
            .Append("Content-Length: ").Append(length).Append("\r\n")
            .Append("Accept-Ranges: bytes\r\n")
            .Append("Cache-Control: no-cache\r\n");
        if (partial)
        {
            response.Append("Content-Range: bytes ").Append(start).Append('-').Append(end).Append('/').Append(file.Length).Append("\r\n");
        }

        response.Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response.ToString()), cancellationToken);
        if (headersOnly || length == 0)
        {
            return;
        }

        file.Position = start;
        var buffer = new byte[64 * 1024];
        var remaining = length;
        while (remaining > 0)
        {
            var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }
    }

    private static bool TryParseRange(string? header, long fileLength, out long start, out long end)
    {
        start = 0;
        end = fileLength - 1;
        if (fileLength == 0 || header is null || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var bounds = header[6..].Split('-', 2);
        if (bounds.Length != 2 || !long.TryParse(bounds[0], out start) || start < 0 || start >= fileLength)
        {
            return false;
        }

        if (!long.TryParse(bounds[1], out end))
        {
            end = fileLength - 1;
        }

        end = Math.Min(end, fileLength - 1);
        return end >= start;
    }

    private static async Task WriteStatusAsync(
        NetworkStream stream,
        int status,
        string reason,
        CancellationToken cancellationToken)
    {
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response, cancellationToken);
    }

    private static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".webm" => "video/webm",
        ".mp4" => "video/mp4",
        ".ogg" or ".oga" => "audio/ogg",
        ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".wasm" => "application/wasm",
        _ => "application/octet-stream"
    };
}
