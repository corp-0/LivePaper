using Tomlyn;
using Tomlyn.Serialization;
using System.Text.Json.Serialization;

namespace LivePaper.Daemon;

public sealed record DaemonOptions(
    string ConfigPath,
    string RendererPath,
    string WallpaperDirectory,
    bool ForceDirectWpe,
    TimeSpan VisibilityPollInterval,
    CoveragePolicy DisableRenderingWhen,
    CoveragePolicy MuteAudioWhen)
{
    public static DaemonOptions Parse(string[] args)
    {
        var rendererPath = ReadOption(args, "--renderer") ?? FindRenderer();
        if (!File.Exists(rendererPath))
        {
            throw new FileNotFoundException("The renderer executable does not exist.", rendererPath);
        }

        var configPath = GetConfigPath(args);
        var config = File.Exists(configPath)
            ? LivePaperConfig.Load(File.ReadAllText(configPath))
            : new LivePaperConfig();
        var pollIntervalMs = Math.Max(config.Visibility.PollIntervalMs, 50);
        var wallpaperDirectory = Path.GetFullPath(
            ReadOption(args, "--wallpaper")
            ?? ResolveWallpaper(config.Wallpaper.Id));
        if (!File.Exists(Path.Combine(wallpaperDirectory, "manifest.toml")))
        {
            throw new FileNotFoundException("The wallpaper directory has no manifest.toml.", wallpaperDirectory);
        }

        return new DaemonOptions(
            configPath,
            Path.GetFullPath(rendererPath),
            wallpaperDirectory,
            config.ForceDirectWpe,
            TimeSpan.FromMilliseconds(pollIntervalMs),
            ParseCoveragePolicy(config.Visibility.DisableRenderingWhen, "disable_rendering_when"),
            ParseCoveragePolicy(config.Visibility.MuteAudioWhen, "mute_audio_when"));
    }

    public static string GetConfigPath(string[] args)
    {
        var configured = ReadOption(args, "--config");
        if (configured is not null)
        {
            return Path.GetFullPath(configured);
        }

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome) || !Path.IsPathFullyQualified(configHome))
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(profile))
            {
                throw new IOException("Cannot resolve the user config directory.");
            }

            configHome = Path.Combine(profile, ".config");
        }

        return Path.Combine(configHome, "livepaper", "livepaper.toml");
    }

    private static string ResolveWallpaper(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || Path.GetFileName(id) != id)
        {
            throw new InvalidDataException("wallpaper.id must be one installed wallpaper ID.");
        }

        var library = WallpaperLibrary.GetDefaultRoot();
        if (!Directory.Exists(library))
        {
            throw new DirectoryNotFoundException($"The wallpaper library does not exist: {library}");
        }

        string? match = null;
        var invalidManifests = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(library))
        {
            var manifestPath = Path.Combine(directory, "manifest.toml");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            Protocol.WallpaperManifest manifest;
            try
            {
                manifest = Protocol.WallpaperManifest.Load(File.ReadAllText(manifestPath));
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or TomlException)
            {
                invalidManifests.Add($"{manifestPath}: {exception.Message}");
                continue;
            }
            if (!string.Equals(manifest.Id, id, StringComparison.Ordinal))
            {
                continue;
            }

            if (match is not null)
            {
                throw new InvalidDataException($"Multiple installed wallpapers use the ID '{id}'.");
            }

            match = directory;
        }

        if (match is not null)
        {
            foreach (var invalidManifest in invalidManifests)
            {
                Console.Error.WriteLine($"Ignoring invalid wallpaper manifest: {invalidManifest}");
            }

            return match;
        }

        var invalidDetails = invalidManifests.Count == 0
            ? string.Empty
            : $" Invalid manifests: {string.Join("; ", invalidManifests)}";
        throw new FileNotFoundException($"Wallpaper '{id}' is not installed.{invalidDetails}");
    }

    private static CoveragePolicy ParseCoveragePolicy(string value, string setting) =>
        value.ToLowerInvariant() switch
        {
            "never" => CoveragePolicy.Never,
            "fully_covered" => CoveragePolicy.FullyCovered,
            "partially_covered" => CoveragePolicy.PartiallyCovered,
            "always" => CoveragePolicy.Always,
            _ => throw new InvalidDataException(
                $"visibility.{setting} must be never, fully_covered, partially_covered, or always.")
        };

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{name} requires a value.");
        }

        return args[index + 1];
    }

    private static string FindRenderer()
    {
        var installedRenderer = Path.Combine(AppContext.BaseDirectory, "LivePaper.Renderer");
        if (File.Exists(installedRenderer))
        {
            return installedRenderer;
        }

        var developmentRenderer = Path.Combine(
            Environment.CurrentDirectory,
            "src",
            "LivePaper.Renderer",
            "bin",
            "Debug",
            "net10.0",
            "LivePaper.Renderer");

        return developmentRenderer;
    }
}

public sealed class LivePaperConfig
{
    [JsonPropertyName("force_direct_wpe")]
    public bool ForceDirectWpe { get; init; }

    [JsonPropertyName("wallpaper")]
    public WallpaperConfig Wallpaper { get; init; } = new();

    [JsonPropertyName("visibility")]
    public VisibilityConfig Visibility { get; init; } = new();

    public static LivePaperConfig Load(string toml) =>
        TomlSerializer.Deserialize(toml, LivePaperConfigTomlContext.Default.LivePaperConfig)
        ?? throw new InvalidDataException("The LivePaper config is empty.");
}

public sealed class WallpaperConfig
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "wallpaper-engine.3650880224";

}

public sealed class VisibilityConfig
{
    [JsonPropertyName("poll_interval_ms")]
    public int PollIntervalMs { get; init; } = 250;

    [JsonPropertyName("disable_rendering_when")]
    public string DisableRenderingWhen { get; init; } = "fully_covered";

    [JsonPropertyName("mute_audio_when")]
    public string MuteAudioWhen { get; init; } = "fully_covered";
}

public enum CoveragePolicy
{
    Never,
    FullyCovered,
    PartiallyCovered,
    Always
}

[TomlSerializable(typeof(LivePaperConfig))]
public sealed partial class LivePaperConfigTomlContext : TomlSerializerContext
{
    public LivePaperConfigTomlContext()
    {
    }
}
