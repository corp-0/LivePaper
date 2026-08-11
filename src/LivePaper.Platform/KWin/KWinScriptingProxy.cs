using Tmds.DBus.Protocol;

namespace LivePaper.Platform.KWin;

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
