using LivePaper.Protocol;
using Xunit;

namespace LivePaper.Daemon.Tests;

public class WallpaperImporterTests
{
    [Fact]
    public void DetectsWallpaperEngineAudioListener()
    {
        var root = Path.Combine(Path.GetTempPath(), $"livepaper-import-test-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "12345");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllText(Path.Combine(source, "project.json"), """
                {
                  "type": "web",
                  "title": "Audio test",
                  "file": "index.html"
                }
                """);
            File.WriteAllText(
                Path.Combine(source, "index.html"),
                "<script>wallpaperRegisterAudioListener(samples => draw(samples));</script>");

            var imported = WallpaperImporter.Import(source, destination);
            var manifest = WallpaperManifest.Load(File.ReadAllText(Path.Combine(imported, "manifest.toml")));

            Assert.True(manifest.HasCapability(WallpaperCapabilities.AudioReaction));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ImportsWorkshopDependenciesAsSiblingDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"livepaper-import-test-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "12345");
        var dependency = Path.Combine(root, "67890");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dependency);
        try
        {
            File.WriteAllText(Path.Combine(source, "project.json"), """
                {
                  "type": "web",
                  "title": "Dependency test",
                  "file": "index.html"
                }
                """);
            File.WriteAllText(Path.Combine(source, "index.html"), "<script src=\"../67890/base.js\"></script>");
            File.WriteAllText(Path.Combine(dependency, "base.js"), "window.baseLoaded = true;");

            var imported = WallpaperImporter.Import(source, destination, [dependency]);
            var manifest = WallpaperManifest.Load(File.ReadAllText(Path.Combine(imported, "manifest.toml")));

            Assert.Equal("index.html", manifest.Entry);
            Assert.Equal("12345", manifest.WallpaperEngine?.WorkshopId);
            Assert.Equal(["67890"], Assert.IsType<WallpaperEngineCompatibility>(manifest.WallpaperEngine).Dependencies);
            Assert.True(File.Exists(Path.Combine(imported, "index.html")));
            Assert.True(File.Exists(Path.Combine(imported, "67890", "base.js")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReportsMissingWorkshopDependency()
    {
        var root = Path.Combine(Path.GetTempPath(), $"livepaper-import-test-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "12345");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllText(Path.Combine(source, "project.json"), """
                {
                  "type": "web",
                  "file": "index.html"
                }
                """);
            File.WriteAllText(Path.Combine(source, "index.html"), "<!doctype html>");

            var exception = Assert.Throws<DirectoryNotFoundException>(
                () => WallpaperImporter.Import(source, destination, [Path.Combine(root, "67890")]));

            Assert.Contains("67890", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ImportsPresetOverItsBaseDependency()
    {
        var root = Path.Combine(Path.GetTempPath(), $"livepaper-import-test-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "12345");
        var dependency = Path.Combine(root, "67890");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(source, "files"));
        Directory.CreateDirectory(dependency);
        try
        {
            File.WriteAllText(Path.Combine(source, "project.json"), """
                {
                  "dependency": "67890",
                  "title": "Preset wallpaper",
                  "preset": {
                    "enabled": true,
                    "amount": 42,
                    "image": "files/custom.png"
                  }
                }
                """);
            File.WriteAllText(Path.Combine(source, "files", "custom.png"), "preset asset");
            File.WriteAllText(Path.Combine(dependency, "project.json"), """
                {
                  "type": "web",
                  "title": "Base wallpaper",
                  "file": "index.html",
                  "general": {
                    "properties": {
                      "enabled": { "value": false },
                      "amount": { "value": 10 },
                      "image": { "type": "file" }
                    }
                  }
                }
                """);
            File.WriteAllText(Path.Combine(dependency, "index.html"), "<img src=\"files/custom.png\">");

            var imported = WallpaperImporter.Import(source, destination, [dependency]);
            var manifest = WallpaperManifest.Load(File.ReadAllText(Path.Combine(imported, "manifest.toml")));

            Assert.Equal("wallpaper-engine.preset-wallpaper", manifest.Id);
            Assert.Equal("Preset wallpaper", manifest.Name);
            Assert.Equal("index.html", manifest.Entry);
            Assert.Equal(true, manifest.WallpaperEngine?.Properties["enabled"]);
            Assert.Equal(42L, manifest.WallpaperEngine?.Properties["amount"]);
            Assert.Equal("files/custom.png", manifest.WallpaperEngine?.Properties["image"]);
            Assert.Equal(
                ["67890"],
                Assert.IsType<WallpaperEngineCompatibility>(manifest.WallpaperEngine).Dependencies);
            Assert.Equal("67890", manifest.WallpaperEngine?.BaseDependency);
            Assert.Equal(
                ["image"],
                Assert.IsType<WallpaperEngineCompatibility>(manifest.WallpaperEngine).FileProperties);
            Assert.True(File.Exists(Path.Combine(imported, "index.html")));
            Assert.Equal("preset asset", File.ReadAllText(Path.Combine(imported, "files", "custom.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Kitagawa Marin", "kitagawa-marin")]
    [InlineData("  Multiple---Spaces  ", "multiple-spaces")]
    [InlineData("", "12345")]
    public void CreatesWallpaperIdSlugFromTitle(string title, string expected)
    {
        Assert.Equal(expected, WallpaperImporter.ToIdSlug(title, "12345"));
    }
}
