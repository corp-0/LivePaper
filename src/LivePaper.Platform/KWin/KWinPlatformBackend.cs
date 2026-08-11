using LivePaper.Protocol;
using Tmds.DBus.Protocol;

namespace LivePaper.Platform.KWin;

public sealed class KWinPlatformBackend :
    IPlatformBackend,
    IHostedWallpaperBackend,
    IVisibilitySource,
    IPointerPositionSource
{
    private const string ServiceName = "io.github.livepaper.LivePaper";
    private const string ScriptName = "livepaper-visibility";

    private readonly DBusConnection _connection;
    private readonly KWinScriptingProxy _scripting;
    private readonly PlasmaShellProxy _plasmaShell;
    private readonly string _scriptPath;
    private IDisposable? _showDesktopSubscription;
    private bool _wallpaperActivated;
    private VisibilityState _coverageState = VisibilityState.Visible;
    private bool _showingDesktop;
    private PointerPositionChanged? _currentPointerPosition;

    private KWinPlatformBackend(
        DBusConnection connection,
        KWinScriptingProxy scripting,
        PlasmaShellProxy plasmaShell,
        string scriptPath,
        bool showingDesktop)
    {
        _connection = connection;
        _scripting = scripting;
        _plasmaShell = plasmaShell;
        _scriptPath = scriptPath;
        _showingDesktop = showingDesktop;
    }

    public event EventHandler<VisibilityChanged>? Changed;

    event EventHandler<PointerPositionChanged>? IPointerPositionSource.Changed
    {
        add => PointerPositionChanged += value;
        remove => PointerPositionChanged -= value;
    }

    private event EventHandler<PointerPositionChanged>? PointerPositionChanged;

    public IVisibilitySource Visibility => this;

    public IPointerPositionSource? PointerPosition { get; private set; }

    PointerPositionChanged? IPointerPositionSource.Current => _currentPointerPosition;

    public VisibilityChanged Current { get; private set; } = new(
        VisibilityState.Visible,
        ShouldRender: true,
        ShouldMute: false);

    public async Task PresentAsync(Uri source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _plasmaShell.ShowWallpaperAsync(PlasmaWallpaperPackage.PluginId, source.AbsoluteUri);
        _wallpaperActivated = true;
    }

    public static async Task<IPlatformBackend> CreateAsync(
        TimeSpan pollInterval,
        bool trackPointerPosition)
    {
        var connection = new DBusConnection(DBusAddress.Session!);
        string? scriptPath = null;
        try
        {
            await connection.ConnectAsync();
            await connection.RequestNameAsync(ServiceName, RequestNameOptions.None);

            var handler = new VisibilityMethodHandler(pollInterval);
            connection.AddMethodHandler(handler);

            var scripting = new KWinScriptingProxy(connection);
            var plasmaShell = new PlasmaShellProxy(connection);
            await scripting.UnloadScriptAsync(ScriptName);

            scriptPath = WriteScript(trackPointerPosition);
            PlasmaWallpaperPackage.Install();
            var showingDesktop = await scripting.GetShowingDesktopAsync();
            var source = new KWinPlatformBackend(
                connection,
                scripting,
                plasmaShell,
                scriptPath,
                showingDesktop);
            source.PointerPosition = trackPointerPosition ? source : null;
            handler.Changed += source.OnVisibilityChanged;
            handler.PointerPositionChanged += source.OnPointerPositionChanged;
            source._showDesktopSubscription = await scripting.WatchShowingDesktopAsync(source.OnShowingDesktopChanged);

            var scriptId = await scripting.LoadScriptAsync(scriptPath, ScriptName);
            if (scriptId < 0)
            {
                throw new IOException("KWin rejected the LivePaper visibility script.");
            }

            await scripting.StartAsync();
            Console.WriteLine($"KWin visibility polling started ({pollInterval.TotalMilliseconds:0} ms).");
            return source;
        }
        catch
        {
            connection.Dispose();
            if (scriptPath is not null && File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _showDesktopSubscription?.Dispose();

        if (_wallpaperActivated)
        {
            try
            {
                await _plasmaShell.RestoreWallpapersAsync(PlasmaWallpaperPackage.PluginId);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Could not restore the Plasma wallpaper: {exception.Message}");
            }
        }

        try
        {
            await _scripting.UnloadScriptAsync(ScriptName);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Could not unload the KWin visibility script: {exception.Message}");
        }

        _connection.Dispose();
        if (File.Exists(_scriptPath))
        {
            File.Delete(_scriptPath);
        }
    }

    private static string WriteScript(bool trackPointerPosition)
    {
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Path.IsPathFullyQualified(runtimeDirectory))
        {
            throw new IOException("XDG_RUNTIME_DIR is not an absolute path.");
        }
        var directory = Path.Combine(runtimeDirectory, "livepaper");
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, $"kwin-visibility-{Guid.NewGuid():N}.js");

        var script = ReadScript("visibility.js");
        if (trackPointerPosition)
        {
            script += $"{Environment.NewLine}{ReadScript("pointer-position.js")}";
        }

        File.WriteAllText(scriptPath, script);
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return scriptPath;
    }

    private static string ReadScript(string fileName)
    {
        var assembly = typeof(KWinPlatformBackend).Assembly;
        var resourceName = $"{typeof(KWinPlatformBackend).Namespace}.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded KWin script '{fileName}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void OnVisibilityChanged(object? sender, string stateName)
    {
        if (!Enum.TryParse<VisibilityState>(stateName, out var state) ||
            state is VisibilityState.OutputDisabled or VisibilityState.SessionLocked)
        {
            Console.Error.WriteLine($"KWin sent an unknown visibility state: {stateName}");
            return;
        }

        _coverageState = state;
        PublishEffectiveVisibility();
    }

    private void OnPointerPositionChanged(PointerPositionChanged position)
    {
        _currentPointerPosition = position;
        PointerPositionChanged?.Invoke(this, position);
    }

    private void OnShowingDesktopChanged(bool showingDesktop)
    {
        _showingDesktop = showingDesktop;
        PublishEffectiveVisibility();
    }

    private void PublishEffectiveVisibility()
    {
        var state = _showingDesktop ? VisibilityState.Visible : _coverageState;
        var next = new VisibilityChanged(state, ShouldRender: true, ShouldMute: false);

        if (next == Current)
        {
            return;
        }

        Current = next;
        Console.WriteLine($"KWin visibility: {next.State}");
        Changed?.Invoke(this, next);
    }
}
