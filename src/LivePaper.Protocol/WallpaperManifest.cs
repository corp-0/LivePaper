using System.Text.Json.Serialization;
using Tomlyn;
using Tomlyn.Serialization;
using Tomlyn.Model;

namespace LivePaper.Protocol;

public class WallpaperManifest
{
    [JsonPropertyName("format_version")]
    public required int FormatVersion { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("entry")]
    public required string Entry { get; init; }

    [JsonPropertyName("capabilities")]
    public string[] Capabilities { get; init; } = [];

    [JsonPropertyName("properties")]
    public TomlTable Properties { get; init; } = [];

    [JsonPropertyName("wallpaper_engine")]
    public WallpaperEngineCompatibility? WallpaperEngine { get; init; }

    public bool HasCapability(string capability) =>
        Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase);

    public static WallpaperManifest Load(string toml)
    {
        var manifest = TomlSerializer.Deserialize(toml, LivePaperTomlContext.Default.WallpaperManifest)
            ?? throw new InvalidDataException("The wallpaper manifest is empty.");
        var invalid = manifest.Capabilities.FirstOrDefault(static capability =>
            string.IsNullOrWhiteSpace(capability) || !WallpaperCapabilities.IsKnown(capability));
        if (invalid is not null)
        {
            throw new InvalidDataException($"Unknown wallpaper capability: {invalid}");
        }

        if (manifest.Capabilities.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            manifest.Capabilities.Length)
        {
            throw new InvalidDataException("Wallpaper capabilities must not contain duplicates.");
        }

        foreach (var (name, value) in manifest.Properties)
        {
            if (value is not TomlTable definition || !definition.ContainsKey("value"))
            {
                throw new InvalidDataException($"Wallpaper property '{name}' must define a value.");
            }

            if (!definition.TryGetValue("type", out var type) ||
                type is not string typeName ||
                typeName is not ("slider" or "toggle" or "text" or "color" or "select"))
            {
                throw new InvalidDataException(
                    $"Wallpaper property '{name}' has an unknown or missing control type.");
            }
        }

        return manifest;
    }
}

public class WallpaperEngineCompatibility
{
    [JsonPropertyName("workshop_id")]
    public string? WorkshopId { get; init; }

    [JsonPropertyName("dependencies")]
    public string[] Dependencies { get; init; } = [];

    [JsonPropertyName("base_dependency")]
    public string? BaseDependency { get; init; }

    [JsonPropertyName("file_properties")]
    public string[] FileProperties { get; init; } = [];

    [JsonPropertyName("force_2d_transforms")]
    public bool Force2DTransforms { get; init; }

    [JsonPropertyName("properties")]
    public TomlTable Properties { get; init; } = [];
}

[TomlSerializable(typeof(WallpaperManifest))]
public sealed partial class LivePaperTomlContext : TomlSerializerContext
{
    public LivePaperTomlContext()
    {
    }
}
