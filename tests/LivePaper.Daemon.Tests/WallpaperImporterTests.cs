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
}
