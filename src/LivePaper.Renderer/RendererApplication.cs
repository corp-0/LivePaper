using LivePaper.Renderer.Native;
using LivePaper.Protocol;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Tomlyn.Model;

namespace LivePaper.Renderer;

public static class RendererApplication
{
    public static int Run(string[] args)
    {
        if (args.Contains("--fallback", StringComparer.Ordinal))
        {
            return RunFallback(args);
        }

        if (args.Contains("--serve-only", StringComparer.Ordinal))
        {
            return RunServerOnly(args);
        }

        if (args.Contains("--probe", StringComparer.Ordinal))
        {
            using var presenter = new WaylandPresenter(1, 1, interactive: false);
            Console.WriteLine("The compositor supports the direct Wayland layer-shell presenter.");
            return 0;
        }

        return RunWpeDirect(args);
    }

    private static int RunFallback(string[] args)
    {
        var width = GetPositiveUIntOption(args, "--wpe-width", 3440);
        var height = GetPositiveUIntOption(args, "--wpe-height", 1440);
        var messageIndex = Array.IndexOf(args, "--message");
        var message = messageIndex >= 0 && messageIndex + 1 < args.Length
            ? args[messageIndex + 1]
            : "The wallpaper renderer stopped unexpectedly.";
        using var presenter = new WaylandPresenter(width, height, interactive: false);
        if (!presenter.PresentFallback(message))
        {
            throw new InvalidOperationException("Could not present the fallback wallpaper.");
        }

        Console.WriteLine($"Showing fallback wallpaper: {message}");
        using var mainLoop = GLib.CreateMainLoop();
        using var signals = new MainLoopSignalRegistration(mainLoop);
        mainLoop.Run();
        return 0;
    }

    private static int RunWpeDirect(string[] args)
    {
        var wallpaper = ResolveWallpaper(args);
        var diagnosticsEnabled = args.Contains("--diagnostics", StringComparer.Ordinal);
        var width = GetPositiveUIntOption(args, "--wpe-width", 3440);
        var height = GetPositiveUIntOption(args, "--wpe-height", 1440);
        using var ipc = ConnectToDaemon(args);
        var initialVisibility = ipc?.InitialVisibility
            ?? new VisibilityChanged(VisibilityState.Visible, ShouldRender: true, ShouldMute: false);
        using var wallpaperServer = WallpaperHttpServer.Start(
            wallpaper.Directory,
            wallpaper.Manifest.Entry,
            diagnosticsEnabled,
            wallpaper.InitialPropertiesJson,
            initialVisibility,
            ipc?.InitialPointerPosition,
            wallpaper.Manifest.WallpaperEngine?.Force2DTransforms is true);
        var interactive = wallpaper.Manifest.HasCapability(WallpaperCapabilities.PointerInput);
        using var renderer = new WpeDirectRenderer(width, height, diagnosticsEnabled, interactive);
        renderer.Load(wallpaperServer.EntryUri.AbsoluteUri);
        renderer.DispatchVisibility(initialVisibility);
        Console.WriteLine($"Direct WPE/FDO bottom-layer presenter: {width}x{height}.");
        Console.WriteLine($"Loading wallpaper: {wallpaper.EntryPath}");

        using var mainLoop = GLib.CreateMainLoop();
        using var ipcShutdown = new CancellationTokenSource();
        var dispatcher = new RendererDispatcher(renderer);
        var ipcTask = ipc?.ListenAsync(
            dispatcher.DispatchVisibility,
            dispatcher.DispatchPointerPosition,
            ipcShutdown.Token);
        using var signals = new MainLoopSignalRegistration(mainLoop);
        mainLoop.Run();
        ipcShutdown.Cancel();
        if (ipcTask is not null)
        {
            try { ipcTask.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
        }

        return 0;
    }

    private static uint GetPositiveUIntOption(string[] args, string option, uint defaultValue)
    {
        var index = Array.IndexOf(args, option);
        if (index < 0)
        {
            return defaultValue;
        }

        if (index + 1 >= args.Length || !uint.TryParse(args[index + 1], out var value) || value == 0)
        {
            throw new ArgumentException($"{option} requires a positive integer.");
        }

        return value;
    }

    private static int RunServerOnly(string[] args)
    {
        var wallpaper = ResolveWallpaper(args);
        using var wallpaperServer = WallpaperHttpServer.Start(
            wallpaper.Directory,
            wallpaper.Manifest.Entry,
            logRequests: args.Contains("--diagnostics", StringComparer.Ordinal),
            bootstrapPropertiesJson: wallpaper.InitialPropertiesJson,
            force2DTransforms: wallpaper.Manifest.WallpaperEngine?.Force2DTransforms is true);
        using var mainLoop = GLib.CreateMainLoop();
        using var signals = new MainLoopSignalRegistration(mainLoop);

        Console.WriteLine($"Wallpaper benchmark server: {wallpaperServer.EntryUri.AbsoluteUri}");
        mainLoop.Run();
        return 0;
    }

    private static RendererIpcClient? ConnectToDaemon(string[] args)
    {
        var socketIndex = Array.IndexOf(args, "--socket");
        if (socketIndex < 0)
        {
            return null;
        }

        if (socketIndex + 1 >= args.Length)
        {
            throw new ArgumentException("--socket requires a path.");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var client = RendererIpcClient.ConnectAsync(args[socketIndex + 1], timeout.Token)
            .GetAwaiter()
            .GetResult();

        try
        {
            var message = client.ReadAsync(timeout.Token).GetAwaiter().GetResult();
            if (message.Kind != HostMessageKind.VisibilityChanged || message.Visibility is null)
            {
                throw new InvalidDataException("The daemon did not send an initial visibility message.");
            }

            Console.WriteLine(
                $"Host visibility: {message.Visibility.State}, render: {message.Visibility.ShouldRender}, " +
                $"mute: {message.Visibility.ShouldMute}");
            client.InitialVisibility = message.Visibility;
            client.InitialPointerPosition = message.PointerPosition;
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static ResolvedWallpaper ResolveWallpaper(string[] args)
    {
        var optionIndex = Array.IndexOf(args, "--wallpaper");
        if (optionIndex >= 0 && optionIndex + 1 >= args.Length)
        {
            throw new ArgumentException("--wallpaper requires a directory path.");
        }

        var wallpaperPath = optionIndex >= 0
            ? args[optionIndex + 1]
            : Path.Combine(Environment.CurrentDirectory, "samples", "minimal");

        var wallpaperDirectory = Path.GetFullPath(wallpaperPath);
        var manifestPath = Path.Combine(wallpaperDirectory, "manifest.toml");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                "The wallpaper has no manifest.toml. Import external wallpapers before running them.",
                manifestPath);
        }

        var manifest = WallpaperManifest.Load(File.ReadAllText(manifestPath));
        var initialPropertiesJson = SerializeInitialProperties(manifest, args);
        var entryPath = Path.GetFullPath(manifest.Entry, wallpaperDirectory);
        var relativeEntry = Path.GetRelativePath(wallpaperDirectory, entryPath);

        if (relativeEntry == ".." || relativeEntry.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The wallpaper entry must stay inside its wallpaper directory.");
        }

        if (!File.Exists(entryPath))
        {
            throw new FileNotFoundException("The wallpaper entry does not exist.", entryPath);
        }

        return new(wallpaperDirectory, entryPath, manifest, initialPropertiesJson);
    }

    private static Dictionary<string, string> GetWallpaperEnginePropertyOverrides(string[] args)
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        AddOption("--target-fps", "targetfps");
        AddOption("--model-resolution", "modelresolution");
        AddOption("--bgm-volume", "bgmvolume");
        return overrides;

        void AddOption(string option, string property)
        {
            var index = Array.IndexOf(args, option);
            if (index >= 0)
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"{option} requires a value.");
                }

                overrides[property] = args[index + 1];
            }
        }
    }

    private static string? SerializeInitialProperties(WallpaperManifest manifest, string[] args)
    {
        var properties = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, value) in manifest.Properties)
        {
            var definition = (TomlTable)value;
            properties.Add(name, definition["value"]);
        }

        if (manifest.WallpaperEngine is not null)
        {
            foreach (var property in manifest.WallpaperEngine.Properties)
            {
                properties.TryAdd(property.Key, property.Value);
            }
        }

        if (properties.Count == 0)
        {
            return null;
        }

        var wallpaperEngineOverrides = GetWallpaperEnginePropertyOverrides(args);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in properties)
            {
                writer.WritePropertyName(property.Key);
                writer.WriteStartObject();
                writer.WritePropertyName("value");
                if (wallpaperEngineOverrides.TryGetValue(property.Key, out var overrideValue))
                {
                    WriteOverrideValue(writer, property.Value, overrideValue);
                }
                else
                {
                    WritePropertyValue(writer, property.Value);
                }
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WritePropertyValue(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case string text: writer.WriteStringValue(text); break;
            case bool boolean: writer.WriteBooleanValue(boolean); break;
            case byte number: writer.WriteNumberValue(number); break;
            case sbyte number: writer.WriteNumberValue(number); break;
            case short number: writer.WriteNumberValue(number); break;
            case ushort number: writer.WriteNumberValue(number); break;
            case int number: writer.WriteNumberValue(number); break;
            case uint number: writer.WriteNumberValue(number); break;
            case long number: writer.WriteNumberValue(number); break;
            case ulong number: writer.WriteNumberValue(number); break;
            case float number: writer.WriteNumberValue(number); break;
            case double number: writer.WriteNumberValue(number); break;
            default: throw new InvalidDataException($"Unsupported wallpaper property type '{value.GetType().Name}'.");
        }
    }

    private static void WriteOverrideValue(Utf8JsonWriter writer, object originalValue, string value)
    {
        if (originalValue is not string and not bool && double.TryParse(value, out var number))
        {
            writer.WriteNumberValue(number);
        }
        else if (originalValue is bool && bool.TryParse(value, out var boolean))
        {
            writer.WriteBooleanValue(boolean);
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }

    private sealed record ResolvedWallpaper(
        string Directory,
        string EntryPath,
        WallpaperManifest Manifest,
        string? InitialPropertiesJson);

    private sealed class RendererDispatcher(WpeDirectRenderer renderer)
    {
        public void DispatchVisibility(VisibilityChanged visibility) =>
            GLib.Invoke(() => renderer.DispatchVisibility(visibility));

        public void DispatchPointerPosition(PointerPositionChanged pointerPosition) =>
            GLib.Invoke(() => renderer.DispatchPointerPosition(pointerPosition));
    }

    private sealed class MainLoopSignalRegistration : IDisposable
    {
        private readonly GLib.MainLoop _mainLoop;
        private readonly PosixSignalRegistration _sigterm;
        private readonly PosixSignalRegistration _sigint;

        public MainLoopSignalRegistration(GLib.MainLoop mainLoop)
        {
            _mainLoop = mainLoop;
            _sigterm = Register(PosixSignal.SIGTERM);
            _sigint = Register(PosixSignal.SIGINT);
        }

        public void Dispose()
        {
            _sigint.Dispose();
            _sigterm.Dispose();
        }

        private PosixSignalRegistration Register(PosixSignal signal) =>
            PosixSignalRegistration.Create(signal, context =>
            {
                context.Cancel = true;
                _mainLoop.Quit();
            });
    }
}
