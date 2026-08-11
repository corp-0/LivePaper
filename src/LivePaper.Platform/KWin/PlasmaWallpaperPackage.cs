namespace LivePaper.Platform.KWin;

public static class PlasmaWallpaperPackage
{
    public const string PluginId = "io.github.livepaper.wallpaper";

    public static void Install()
    {
        var packageDirectory = Path.Combine(
            GetDataHome(),
            "plasma",
            "wallpapers",
            PluginId);
        WriteResource(packageDirectory, "metadata.json", "metadata.json");
        WriteResource(
            Path.Combine(packageDirectory, "contents", "config"),
            "main.xml",
            "contents.config.main.xml");
        WriteResource(
            Path.Combine(packageDirectory, "contents", "ui"),
            "main.qml",
            "contents.ui.main.qml");
    }

    private static string GetDataHome()
    {
        var configured = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured))
        {
            return configured;
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
        {
            throw new IOException("Cannot resolve the user data directory.");
        }

        return Path.Combine(profile, ".local", "share");
    }

    private static void WriteResource(string directory, string fileName, string resourceSuffix)
    {
        Directory.CreateDirectory(directory);
        var assembly = typeof(PlasmaWallpaperPackage).Assembly;
        var resourceName = $"LivePaper.Platform.KWin.PlasmaWallpaper.{resourceSuffix}";
        using var input = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded Plasma resource '{resourceSuffix}' is missing.");
        using var output = new FileStream(
            Path.Combine(directory, fileName),
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        input.CopyTo(output);
    }
}
