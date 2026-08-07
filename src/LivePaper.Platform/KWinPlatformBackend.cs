using LivePaper.Protocol;
using Tmds.DBus.Protocol;

namespace LivePaper.Platform;

public sealed class KWinPlatformBackend : IPlatformBackend, IVisibilitySource, IPointerPositionSource
{
    private const string ServiceName = "io.github.livepaper.LivePaper";
    private const string ScriptName = "livepaper-visibility";

    private readonly DBusConnection _connection;
    private readonly KWinScriptingProxy _scripting;
    private readonly string _scriptPath;
    private IDisposable? _showDesktopSubscription;
    private VisibilityState _coverageState = VisibilityState.Visible;
    private bool _showingDesktop;
    private PointerPositionChanged? _currentPointerPosition;

    private KWinPlatformBackend(
        DBusConnection connection,
        KWinScriptingProxy scripting,
        string scriptPath,
        bool showingDesktop)
    {
        _connection = connection;
        _scripting = scripting;
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

    public static async Task<IPlatformBackend> CreateAsync(
        TimeSpan pollInterval,
        bool trackPointerPosition)
    {
        var connection = new DBusConnection(DBusAddress.Session!);
        try
        {
            await connection.ConnectAsync();
            await connection.RequestNameAsync(ServiceName, RequestNameOptions.None);

            var handler = new VisibilityMethodHandler(pollInterval);
            connection.AddMethodHandler(handler);

            var scripting = new KWinScriptingProxy(connection);
            await scripting.UnloadScriptAsync(ScriptName);

            var scriptPath = WriteScript(trackPointerPosition);
            var showingDesktop = await scripting.GetShowingDesktopAsync();
            var source = new KWinPlatformBackend(connection, scripting, scriptPath, showingDesktop);
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
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _showDesktopSubscription?.Dispose();

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
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
            ?? throw new IOException("XDG_RUNTIME_DIR is not set.");
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
        var resourceName = $"{typeof(KWinPlatformBackend).Namespace}.KWin.{fileName}";
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

public class VisibilityMethodHandler(TimeSpan pollInterval) : IPathMethodHandler
{
    private const string Interface = "io.github.livepaper.Visibility";

    public event EventHandler<string>? Changed;

    public event Action<PointerPositionChanged>? PointerPositionChanged;

    public string Path => "/Visibility";

    public bool HandlesChildPaths => false;

    public async ValueTask HandleMethodAsync(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml([InterfaceXml]);
            return;
        }

        var request = context.Request;
        if (request.InterfaceAsString == Interface &&
            request.MemberAsString == "SetPointerPosition" &&
            request.SignatureAsString == "ii")
        {
            var reader = request.GetBodyReader();
            PointerPositionChanged?.Invoke(new(reader.ReadInt32(), reader.ReadInt32()));
            using var writer = context.CreateReplyWriter("");
            context.Reply(writer.CreateMessage());
            return;
        }

        if (request.InterfaceAsString == Interface &&
            request.MemberAsString == "SetVisibilityState" &&
            request.SignatureAsString == "s")
        {
            var state = request.GetBodyReader().ReadString();
            Changed?.Invoke(this, state);
            using var writer = context.CreateReplyWriter("");
            context.Reply(writer.CreateMessage());
            return;
        }

        if (request.InterfaceAsString == Interface &&
            request.MemberAsString == "NextPoll" &&
            request.SignatureAsString == "")
        {
            await Task.Delay(pollInterval);
            using var writer = context.CreateReplyWriter("");
            context.Reply(writer.CreateMessage());
            return;
        }

        context.ReplyUnknownMethodError();
    }

    private static ReadOnlyMemory<byte> InterfaceXml { get; } =
        """
        <interface name="io.github.livepaper.Visibility">
          <method name="SetVisibilityState">
            <arg direction="in" type="s"/>
          </method>
          <method name="NextPoll"/>
          <method name="SetPointerPosition">
            <arg direction="in" type="i"/>
            <arg direction="in" type="i"/>
          </method>
        </interface>
        """u8.ToArray();
}

public class KWinScriptingProxy(DBusConnection connection)
{
    private const string Destination = "org.kde.KWin";
    private const string KWinPath = "/KWin";
    private const string KWinInterface = "org.kde.KWin";
    private const string Path = "/Scripting";
    private const string Interface = "org.kde.kwin.Scripting";

    public Task<bool> GetShowingDesktopAsync()
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            Destination,
            KWinPath,
            "org.freedesktop.DBus.Properties",
            "Get",
            "ss");
        writer.WriteString(KWinInterface);
        writer.WriteString("showingDesktop");
        return connection.CallMethodAsync(
            writer.CreateMessage(),
            static (message, _) => message.GetBodyReader().ReadVariantValue().GetBool(),
            readerState: null);
    }

    public ValueTask<IDisposable> WatchShowingDesktopAsync(Action<bool> changed) =>
        connection.WatchSignalAsync(
            Destination,
            KWinPath,
            KWinInterface,
            "showingDesktopChanged",
            static (message, _) => message.GetBodyReader().ReadBool(),
            notification =>
            {
                if (notification.HasValue)
                {
                    changed(notification.Value);
                }
            },
            ObserverFlags.None,
            emitOnCapturedContext: false,
            state: null);

    public Task<int> LoadScriptAsync(string scriptPath, string pluginName)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Destination, Path, Interface, "loadScript", "ss");
        writer.WriteString(scriptPath);
        writer.WriteString(pluginName);
        return connection.CallMethodAsync(
            writer.CreateMessage(),
            static (message, _) => message.GetBodyReader().ReadInt32(),
            readerState: null);
    }

    public Task StartAsync()
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Destination, Path, Interface, signature: null, member: "start");
        return connection.CallMethodAsync(writer.CreateMessage());
    }

    public Task<bool> UnloadScriptAsync(string pluginName)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Destination, Path, Interface, "unloadScript", "s");
        writer.WriteString(pluginName);
        return connection.CallMethodAsync(
            writer.CreateMessage(),
            static (message, _) => message.GetBodyReader().ReadBool(),
            readerState: null);
    }
}
