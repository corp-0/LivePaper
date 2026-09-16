using System.Text.Json;
using Tmds.DBus.Protocol;

namespace LivePaper.Platform.KWin;

public class PlasmaShellProxy(DBusConnection connection)
{
    private const string Destination = "org.kde.plasmashell";
    private const string Path = "/PlasmaShell";
    private const string Interface = "org.kde.PlasmaShell";

    public async Task<string[]> ReadLoadedVersionsAsync(string check)
    {
        var json = await EvaluateScriptAsync($$"""
            const versions = [];
            for (const desktop of desktops()) {
                if (desktop.wallpaperPlugin !== {{SerializeString(PlasmaWallpaperPackage.PluginId)}}) {
                    continue;
                }
                desktop.currentConfigGroup = ["Wallpaper", {{SerializeString(PlasmaWallpaperPackage.PluginId)}}, "General"];
                desktop.writeConfig("VersionCheck", {{SerializeString(check)}});
                versions.push(desktop.readConfig("ReportedCheck", "") === {{SerializeString(check)}}
                    ? desktop.readConfig("LoadedVersion", "") : "");
            }
            print(JSON.stringify(versions));
            """);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(static value => value.GetString() ?? string.Empty).ToArray();
    }

    public Task ShowWallpaperAsync(string pluginId, string source)
    {
        var plugin = SerializeString(pluginId);
        var url = SerializeString(source);
        return EvaluateScriptAsync($$"""
            const livePaperPlugin = {{plugin}};
            const source = {{url}};
            for (const desktop of desktops()) {
                if (desktop.wallpaperPlugin !== livePaperPlugin) {
                    const previousPlugin = desktop.wallpaperPlugin;
                    desktop.currentConfigGroup = ["Wallpaper", livePaperPlugin, "General"];
                    desktop.writeConfig("PreviousPlugin", previousPlugin);
                }

                desktop.currentConfigGroup = ["Wallpaper", livePaperPlugin, "General"];
                desktop.writeConfig("Source", source);
                desktop.wallpaperPlugin = livePaperPlugin;
            }
            """);
    }

    public Task RestoreWallpapersAsync(string pluginId)
    {
        var plugin = SerializeString(pluginId);
        return EvaluateScriptAsync($$"""
            const livePaperPlugin = {{plugin}};
            for (const desktop of desktops()) {
                if (desktop.wallpaperPlugin !== livePaperPlugin) {
                    continue;
                }

                desktop.currentConfigGroup = ["Wallpaper", livePaperPlugin, "General"];
                desktop.wallpaperPlugin = desktop.readConfig("PreviousPlugin", "org.kde.image");
            }
            """);
    }

    private Task<string> EvaluateScriptAsync(string script)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Destination, Path, Interface, "evaluateScript", "s");
        writer.WriteString(script);
        return connection.CallMethodAsync(
            writer.CreateMessage(),
            static (message, _) => message.GetBodyReader().ReadString(),
            readerState: null);
    }

    private static string SerializeString(string value)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStringValue(value);
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }
}
