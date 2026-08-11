using Xunit;

namespace LivePaper.Protocol.Tests;

public class WallpaperManifestTests
{
    private static readonly string MinimalManifest = $"""
        format_version = {ProtocolVersion.Current}
        id = "livepaper.minimal"
        name = "Minimal"
        entry = "index.html"
        capabilities = ["pointer_input"]
        """;

    [Fact]
    public void LoadParsesMinimalManifest()
    {
        var manifest = WallpaperManifest.Load(MinimalManifest);

        Assert.Equal(ProtocolVersion.Current, manifest.FormatVersion);
        Assert.Equal("index.html", manifest.Entry);
        Assert.Null(manifest.WallpaperEngine);
    }

    [Fact]
    public void LoadParsesWallpaperEngineProperties()
    {
        var manifest = WallpaperManifest.Load("""
            format_version = 1
            id = "wallpaper-engine.test"
            name = "Imported"
            entry = "index.html"
            capabilities = []

            [wallpaper_engine]
            force_2d_transforms = true
            file_properties = ["background_image"]

            [wallpaper_engine.properties]
            targetfps = 60
            audio = true
            """);

        Assert.NotNull(manifest.WallpaperEngine);
        Assert.True(manifest.WallpaperEngine.Force2DTransforms);
        Assert.Equal(["background_image"], manifest.WallpaperEngine.FileProperties);
        Assert.True(manifest.WallpaperEngine.Properties["targetfps"] is long or int);
        Assert.Equal(true, manifest.WallpaperEngine.Properties["audio"]);
    }

    [Fact]
    public void LoadParsesNativePropertyDefinitions()
    {
        var manifest = WallpaperManifest.Load("""
            format_version = 1
            id = "livepaper.test"
            name = "Native"
            entry = "index.html"
            capabilities = []

            [properties.volume]
            type = "slider"
            label = "Volume"
            value = 0.25
            min = 0.0
            max = 1.0
            step = 0.05
            """);

        var volume = Assert.IsType<Tomlyn.Model.TomlTable>(manifest.Properties["volume"]);
        Assert.Equal("slider", volume["type"]);
        Assert.Equal(0.25, volume["value"]);
    }

    [Fact]
    public void LoadRejectsNativePropertyWithoutValue()
    {
        var invalidManifest = MinimalManifest + """

            [properties.volume]
            type = "slider"
            """;

        var error = Assert.Throws<InvalidDataException>(() => WallpaperManifest.Load(invalidManifest));
        Assert.Contains("must define a value", error.Message);
    }

    [Fact]
    public void LoadRejectsUnknownCapabilities()
    {
        var invalidManifest = MinimalManifest.Replace(
            "pointer_input",
            "unknown",
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => WallpaperManifest.Load(invalidManifest));
    }
}
