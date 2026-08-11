using LivePaper.Protocol;
using LivePaper.Renderer;
using Xunit;

namespace LivePaper.Daemon.Tests;

public class RendererApplicationTests
{
    [Fact]
    public void ResolvesWallpaperEngineFilePropertyInsideWallpaper()
    {
        var root = Path.Combine(Path.GetTempPath(), $"livepaper-renderer-test-{Guid.NewGuid():N}");
        var files = Path.Combine(root, "files");
        Directory.CreateDirectory(files);
        try
        {
            var image = Path.Combine(files, "kit2.png");
            File.WriteAllText(image, "image");
            var compatibility = new WallpaperEngineCompatibility
            {
                FileProperties = ["foreground_image"]
            };

            var resolved = RendererApplication.ResolveWallpaperEnginePropertyValue(
                root,
                compatibility,
                "foreground_image",
                "files/kit2.png");

            Assert.Equal(image, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsWallpaperEngineFilePropertyOutsideWallpaper()
    {
        var root = Path.Combine(Path.GetTempPath(), $"livepaper-renderer-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var compatibility = new WallpaperEngineCompatibility
            {
                FileProperties = ["foreground_image"]
            };

            Assert.Throws<InvalidDataException>(() =>
                RendererApplication.ResolveWallpaperEnginePropertyValue(
                    root,
                    compatibility,
                    "foreground_image",
                    "../outside.png"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
