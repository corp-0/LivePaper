using LivePaper.Protocol;
using Xunit;

namespace LivePaper.Daemon.Tests;

public class WallpaperImporterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportsSteamItemWithAutomaticDependency(bool hasDependency)
    {
        var root = Directory.CreateTempSubdirectory("livepaper-steam-import-");
        try
        {
            var steam = Path.Combine(root.FullName, "Steam library");
            var workshop = Path.Combine(steam, "steamapps", "workshop", "content", "431960");
            var source = Directory.CreateDirectory(Path.Combine(workshop, "12345")).FullName;
            var runnable = hasDependency
                ? Directory.CreateDirectory(Path.Combine(workshop, "67890")).FullName
                : source;
            File.WriteAllText(Path.Combine(runnable, "project.json"), """
                { "type": "web", "title": "Steam wallpaper", "file": "index.html",
                  "general": { "properties": { "amount": { "value": 10 } } } }
                """);
            File.WriteAllText(Path.Combine(runnable, "index.html"), "<!doctype html>");
            if (hasDependency)
            {
                File.WriteAllText(Path.Combine(source, "project.json"), """
                    { "title": "Steam preset", "dependency": "67890", "preset": { "amount": 42 } }
                    """);
                File.WriteAllText(Path.Combine(source, "preset.txt"), "preset asset");
            }

            var destination = Path.Combine(root.FullName, "destination");
            var imported = WallpaperImporter.ImportSteam("12345", destination, steam);
            var manifest = WallpaperManifest.Load(File.ReadAllText(Path.Combine(imported, "manifest.toml")));
            var compatibility = Assert.IsType<WallpaperEngineCompatibility>(manifest.WallpaperEngine);

            Assert.Equal("12345", compatibility.WorkshopId);
            Assert.Equal(hasDependency ? "67890" : null, compatibility.BaseDependency);
            Assert.Equal(hasDependency ? ["67890"] : Array.Empty<string>(), compatibility.Dependencies);
            Assert.Equal(hasDependency ? 42L : 10L, compatibility.Properties["amount"]);
            Assert.Equal("<!doctype html>", File.ReadAllText(Path.Combine(imported, "index.html")));
            Assert.Equal(File.ReadAllText(Path.Combine(runnable, "project.json")),
                File.ReadAllText(Path.Combine(imported, "project.json")));
            if (hasDependency)
            {
                Assert.Equal("preset asset", File.ReadAllText(Path.Combine(imported, "preset.txt")));
            }
            Assert.True(File.Exists(Path.Combine(source, "project.json")));
            Assert.Throws<IOException>(() => WallpaperImporter.ImportSteam("12345", destination, steam));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("../12345")]
    [InlineData("abc")]
    [InlineData("")]
    public void RejectsInvalidSteamWorkshopId(string id)
    {
        Assert.Throws<InvalidDataException>(() => WallpaperImporter.ImportSteam(id, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("67890")]
    [InlineData("../67890")]
    public void ReportsUnavailableSteamItemOrInvalidDependency(string? dependencyId)
    {
        var root = Directory.CreateTempSubdirectory("livepaper-steam-import-");
        try
        {
            var destination = Path.Combine(root.FullName, "destination");
            if (dependencyId is not null)
            {
                var source = Directory.CreateDirectory(Path.Combine(
                    root.FullName, "steamapps", "workshop", "content", "431960", "12345"));
                File.WriteAllText(Path.Combine(source.FullName, "project.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { dependency = dependencyId }));
            }

            if (dependencyId == "../67890")
            {
                Assert.Throws<InvalidDataException>(() => WallpaperImporter.ImportSteam("12345", destination, root.FullName));
            }
            else
            {
                var error = Assert.Throws<DirectoryNotFoundException>(() =>
                    WallpaperImporter.ImportSteam("12345", destination, root.FullName));
                Assert.Contains(dependencyId ?? "12345", error.Message, StringComparison.Ordinal);
                Assert.Contains("Steam", error.Message, StringComparison.Ordinal);
            }

            Assert.False(Directory.Exists(destination));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

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
