using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using LivePaper.Protocol;

namespace LivePaper.Renderer;

public sealed class WallpaperHttpServer : IDisposable
{
    private readonly string _root;
    private readonly string _entryPath;
    private readonly TcpListener _listener;
    private readonly bool _logRequests;
    private readonly Action<string, string>? _onConsoleMessage;
    private readonly WallpaperEventHub? _eventHub;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _serverTask;
    private readonly byte[] _bootstrapConfig;
    private static readonly byte[] HostScript = LoadHostScript();

    private WallpaperHttpServer(
        string root,
        string entry,
        bool logRequests,
        string? bootstrapPropertiesJson,
        VisibilityChanged? bootstrapVisibility,
        PointerPositionChanged? bootstrapPointerPosition,
        bool force2DTransforms,
        bool remoteEvents,
        Action<string, string>? onConsoleMessage)
    {
        _root = root;
        _entryPath = Path.GetFullPath(entry, root);
        _logRequests = logRequests;
        _onConsoleMessage = onConsoleMessage;
        _eventHub = remoteEvents
            ? new WallpaperEventHub(bootstrapVisibility, bootstrapPointerPosition)
            : null;
        _bootstrapConfig = BuildBootstrapConfig(
            root,
            bootstrapPropertiesJson,
            bootstrapVisibility,
            bootstrapPointerPosition,
            force2DTransforms,
            remoteEvents,
            logRequests);
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
        bool force2DTransforms = false,
        bool remoteEvents = false,
        Action<string, string>? onConsoleMessage = null) =>
        new(
            root,
            entry,
            logRequests,
            bootstrapPropertiesJson,
            bootstrapVisibility,
            bootstrapPointerPosition,
            force2DTransforms,
            remoteEvents,
            onConsoleMessage);

    public void DispatchVisibility(VisibilityChanged visibility)
    {
        _eventHub?.Dispatch(visibility);
    }

    public void DispatchPointerPosition(PointerPositionChanged pointerPosition)
    {
        _eventHub?.Dispatch(pointerPosition);
    }

    public void DispatchAudioSpectrum(AudioSpectrumChanged audioSpectrum)
    {
        _eventHub?.Dispatch(audioSpectrum);
    }

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

                if (requestPath == "/__livepaper/console")
                {
                    var level = GetQueryParameter(requestUri.Query, "level") ?? "log";
                    var message = GetQueryParameter(requestUri.Query, "message") ?? string.Empty;
                    _onConsoleMessage?.Invoke(level, message);
                    if (_logRequests)
                    {
                        Console.WriteLine($"Wallpaper JS {level}: {message}");
                    }
                    await WriteStatusAsync(stream, 204, "No Content", cancellationToken);
                    return;
                }

                if (requestPath == "/__livepaper/host.js")
                {
                    await SendBytesAsync(
                        stream,
                        HostScript,
                        "application/javascript; charset=utf-8",
                        parts[0] == "HEAD",
                        cancellationToken);
                    return;
                }

                if (requestPath == "/__livepaper/config.json")
                {
                    await SendBytesAsync(
                        stream,
                        _bootstrapConfig,
                        "application/json; charset=utf-8",
                        parts[0] == "HEAD",
                        cancellationToken);
                    return;
                }

                if (_eventHub is not null && requestPath == "/__livepaper/events")
                {
                    await _eventHub.WriteEventsAsync(stream, cancellationToken);
                    return;
                }

                if (_eventHub is not null && requestPath == "/__livepaper/visibility")
                {
                    await SendBytesAsync(
                        stream,
                        _eventHub.GetVisibility(),
                        "application/json; charset=utf-8",
                        parts[0] == "HEAD",
                        cancellationToken);
                    return;
                }

                var relativePath = Uri.UnescapeDataString(requestPath).TrimStart('/');
                var filePath = Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), _root);
                var relative = Path.GetRelativePath(_root, filePath);
                if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    await Console.Error.WriteLineAsync($"Wallpaper HTTP 404: {requestPath}");
                    await WriteStatusAsync(stream, 404, "Not Found", cancellationToken);
                    return;
                }

                if (!File.Exists(filePath))
                {
                    filePath = FindBundledAsset(relativePath) ?? filePath;
                }

                if (!File.Exists(filePath))
                {
                    await Console.Error.WriteLineAsync($"Wallpaper HTTP 404: {requestPath}");
                    await WriteStatusAsync(stream, 404, "Not Found", cancellationToken);
                    return;
                }

                if (_logRequests)
                {
                    Console.WriteLine($"Wallpaper HTTP 200: {requestPath}");
                }
                if (filePath == _entryPath)
                {
                    await SendEntryWithBootstrapAsync(
                        stream,
                        filePath,
                        parts[0] == "HEAD",
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

    private string? FindBundledAsset(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var releaseSuffix = $"{Path.DirectorySeparatorChar}release{Path.DirectorySeparatorChar}{normalized}";
        string? match = null;
        foreach (var candidate in Directory.EnumerateFiles(
                     _root,
                     Path.GetFileName(normalized),
                     SearchOption.AllDirectories))
        {
            if (!candidate.EndsWith(releaseSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = candidate;
        }

        return match;
    }

    private static string? GetQueryParameter(string query, string name)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            var key = equals < 0 ? part : part[..equals];
            if (string.Equals(Uri.UnescapeDataString(key), name, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(equals < 0 ? string.Empty : part[(equals + 1)..]);
            }
        }

        return null;
    }

    private static async Task SendEntryWithBootstrapAsync(
        NetworkStream stream,
        string filePath,
        bool headersOnly,
        CancellationToken cancellationToken)
    {
        var html = await File.ReadAllTextAsync(filePath, cancellationToken);
        const string bootstrap = "<script src=\"/__livepaper/host.js\"></script>";
        var body = Encoding.UTF8.GetBytes(bootstrap + html);
        await SendBytesAsync(
            stream,
            body,
            "text/html; charset=utf-8",
            headersOnly,
            cancellationToken);
    }

    private static async Task SendBytesAsync(
        NetworkStream stream,
        byte[] body,
        string contentType,
        bool headersOnly,
        CancellationToken cancellationToken)
    {
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-cache\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(response, cancellationToken);
        if (!headersOnly)
        {
            await stream.WriteAsync(body, cancellationToken);
        }
    }

    private static byte[] BuildBootstrapConfig(
        string root,
        string? propertiesJson,
        VisibilityChanged? visibility,
        PointerPositionChanged? pointerPosition,
        bool force2DTransforms,
        bool remoteEvents,
        bool diagnostics)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("wallpaperRoot", Path.GetFullPath(root).Replace('\\', '/'));
        writer.WritePropertyName("properties");
        writer.WriteRawValue(propertiesJson ?? "null");
        writer.WritePropertyName("visibility");
        writer.WriteStartObject();
        writer.WriteString("state", (visibility?.State ?? VisibilityState.Visible).ToString());
        writer.WriteBoolean("shouldRender", visibility?.ShouldRender is not false);
        writer.WriteBoolean("shouldMute", visibility?.ShouldMute is true);
        writer.WriteEndObject();
        writer.WritePropertyName("pointerPosition");
        if (pointerPosition is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", pointerPosition.X);
            writer.WriteNumber("y", pointerPosition.Y);
            writer.WriteEndObject();
        }
        writer.WriteBoolean("force2DTransforms", force2DTransforms);
        writer.WriteBoolean("remoteEvents", remoteEvents);
        writer.WriteBoolean("diagnostics", diagnostics);
        writer.WriteEndObject();
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] LoadHostScript()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "LivePaper.Renderer.Web.host.js")
            ?? throw new InvalidOperationException("The LivePaper web host script is missing.");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
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
