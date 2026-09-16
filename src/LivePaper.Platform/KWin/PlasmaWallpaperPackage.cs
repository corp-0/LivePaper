using System.Security.Cryptography;
using System.Text;

namespace LivePaper.Platform.KWin;

public static class PlasmaWallpaperPackage
{
    public const string PluginId = "io.github.livepaper.wallpaper";
    public static string Version { get; } = ComputeVersion();
    public static string ExpectedLoadedVersion => Version + ":" + ReadResource("input-version.txt").Trim();

    public static string? InstalledVersion => File.Exists(Path.Combine(PackageDirectory, "version"))
        ? File.ReadAllText(Path.Combine(PackageDirectory, "version")).Trim()
        : null;

    private static string PackageDirectory => Path.Combine(GetDataHome(), "plasma", "wallpapers", PluginId);

    public static void Install()
    {
        var packageDirectory = PackageDirectory;
        WriteResource(packageDirectory, "metadata.json", "metadata.json");
        WriteResource(
            Path.Combine(packageDirectory, "contents", "config"),
            "main.xml",
            "contents.config.main.xml");
        WriteResource(
            Path.Combine(packageDirectory, "contents", "ui"),
            "main.qml",
            "contents.ui.main.qml");
        var inputDirectory = Path.Combine(packageDirectory, "contents", "ui", "input");
        WriteResource(inputDirectory, "qmldir", "contents.ui.input.qmldir");
        WriteResource(inputDirectory, "liblivepaperinput.so", "contents.ui.input.liblivepaperinput.so");
        File.WriteAllText(Path.Combine(packageDirectory, "version"), Version);
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
        var temporary = Path.Combine(directory, Path.GetRandomFileName());
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (resourceSuffix == "contents.ui.main.qml")
                {
                    using var reader = new StreamReader(input);
                    var qml = reader.ReadToEnd().Replace("@LIVEPAPER_PACKAGE_VERSION@", Version, StringComparison.Ordinal);
                    output.Write(Encoding.UTF8.GetBytes(qml));
                }
                else
                {
                    input.CopyTo(output);
                }
            }
            // Plasma may still have the previous native plugin mapped into memory.
            File.Move(temporary, Path.Combine(directory, fileName), overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static string ReadResource(string suffix)
    {
        using var input = typeof(PlasmaWallpaperPackage).Assembly.GetManifestResourceStream(
            $"LivePaper.Platform.KWin.PlasmaWallpaper.{suffix}")
            ?? throw new InvalidOperationException($"Missing Plasma resource: {suffix}");
        using var reader = new StreamReader(input);
        return reader.ReadToEnd();
    }

    private static string ComputeVersion()
    {
        var assembly = typeof(PlasmaWallpaperPackage).Assembly;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(static name => name.StartsWith("LivePaper.Platform.KWin.PlasmaWallpaper.", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(name));
            using var input = assembly.GetManifestResourceStream(name)!;
            hash.AppendData(SHA256.HashData(input));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
