using Tmds.DBus.Protocol;

namespace LivePaper.Platform.KWin;

public record PlasmaPluginStatus(bool IsCurrent, string Message)
{
    public static PlasmaPluginStatus Evaluate(string expected, string? installed, IReadOnlyList<string> loaded)
    {
        var package = expected.Split(':')[0];
        if (installed != package)
        {
            return new(false, "Plasma plugin: the installed package does not match this LivePaper build. Check the LivePaper service logs.");
        }
        if (loaded.Count == 0)
        {
            return new(false, "Plasma plugin: installed, but no desktop is using it. Its loaded version could not be verified.");
        }
        if (loaded.Any(static version => string.IsNullOrEmpty(version)))
        {
            return new(false, "Plasma plugin: installed, but at least one desktop did not report its loaded version. It may be using cached plugin code. Restart Plasma Shell or log out and back in, then run 'livepaper check-presentation' again.");
        }
        if (loaded.Any(version => version != expected))
        {
            return new(false, $"Plasma plugin: installed version {expected}; loaded versions {string.Join(", ", loaded.Distinct())}. Restart Plasma Shell or log out and back in to load the update.");
        }
        return new(true, $"Plasma plugin: installed and loaded versions match on all {loaded.Count} desktop(s).");
    }

    public static async Task<bool> CheckAsync()
    {
        using var connection = new DBusConnection(DBusAddress.Session!);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(timeout.Token);
            var shell = new PlasmaShellProxy(connection);
            var check = Guid.NewGuid().ToString("N");
            PlasmaPluginStatus? status = null;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var loaded = await shell.ReadLoadedVersionsAsync(check).WaitAsync(timeout.Token);
                status = Evaluate(PlasmaWallpaperPackage.ExpectedLoadedVersion, PlasmaWallpaperPackage.InstalledVersion, loaded);
                if (status.IsCurrent)
                {
                    break;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
            }
            Console.WriteLine(status!.Message);
            return status.IsCurrent;
        }
        catch (Exception exception) when (exception is DBusExceptionBase or IOException or OperationCanceledException or System.Text.Json.JsonException)
        {
            await Console.Error.WriteLineAsync($"Plasma plugin: could not verify the loaded version: {exception.Message}");
            return false;
        }
    }
}
