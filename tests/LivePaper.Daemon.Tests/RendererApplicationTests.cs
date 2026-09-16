using LivePaper.Protocol;
using LivePaper.Renderer;
using Xunit;

namespace LivePaper.Daemon.Tests;

public class RendererApplicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedConfigReportsPointerInput(bool pointerInput)
    {
        var directory = Directory.CreateTempSubdirectory("livepaper-input-config-");
        try
        {
            using var server = WallpaperHttpServer.Start(directory.FullName, "index.html", pointerInput: pointerInput);
            using var client = new HttpClient();
            var json = await client.GetStringAsync(
                new Uri(server.EntryUri, "/__livepaper/config.json"),
                TestContext.Current.CancellationToken);
            using var config = System.Text.Json.JsonDocument.Parse(json);

            Assert.Equal(pointerInput, config.RootElement.GetProperty("pointerInput").GetBoolean());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void HostedFallbackEscapesRendererError()
    {
        string? path = null;
        using (var directory = TemporaryWallpaperDirectory.CreateFallback("failed <script>alert(1)</script>"))
        {
            path = directory.Path;
            var html = File.ReadAllText(Path.Combine(path, "index.html"));

            Assert.Contains("failed &lt;script&gt;alert(1)&lt;/script&gt;", html);
            Assert.DoesNotContain("failed <script>", html);
        }

        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task HostedServerPublishesCurrentVisibility()
    {
        var root = Path.Combine(Path.GetTempPath(), $"livepaper-server-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "<!doctype html>", TestContext.Current.CancellationToken);
            using var server = WallpaperHttpServer.Start(root, "index.html", remoteEvents: true);
            server.DispatchVisibility(new VisibilityChanged(
                VisibilityState.FullyCovered,
                ShouldRender: false,
                ShouldMute: true));
            using var client = new HttpClient();

            var json = await client.GetStringAsync(
                new Uri(server.EntryUri, "/__livepaper/visibility"),
                TestContext.Current.CancellationToken);

            Assert.Contains("\"state\":\"FullyCovered\"", json);
            Assert.Contains("\"shouldRender\":false", json);
            Assert.Contains("\"shouldMute\":true", json);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
    public void MissingWallpaperEngineFilePropertyPreservesResolvedPath()
    {
        var root = Directory.CreateTempSubdirectory("livepaper-missing-file-");
        try
        {
            var compatibility = new WallpaperEngineCompatibility { FileProperties = ["background_image"] };

            Assert.Equal(Path.Combine(root.FullName, "files", "missing.jpg"), RendererApplication.ResolveWallpaperEnginePropertyValue(
                root.FullName, compatibility, "background_image", "files/missing.jpg"));
            Assert.Equal("files/missing.jpg", RendererApplication.ResolveWallpaperEnginePropertyValue(
                root.FullName, compatibility, "text", "files/missing.jpg"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HostedConfigIncludesBundledSlideshowFiles()
    {
        var root = Directory.CreateTempSubdirectory("livepaper-slideshow-");
        try
        {
            var wallpaper = Directory.CreateDirectory(Path.Combine(root.FullName, "wallpaper"));
            var slides = Directory.CreateDirectory(Path.Combine(wallpaper.FullName, "directories", "slides"));
            var image = Path.Combine(slides.FullName, "slide 1.jpg");
            File.WriteAllText(image, "image");
            Directory.CreateDirectory(Path.Combine(slides.FullName, "empty"));
            var properties = System.Text.Json.JsonSerializer.Serialize(new
            {
                slideshow = new { value = slides.FullName },
                missing = new { value = "directories/missing" },
                outside = new { value = root.FullName },
                amount = new { value = 42 }
            });
            using var server = WallpaperHttpServer.Start(wallpaper.FullName, "index.html", bootstrapPropertiesJson: properties);
            using var client = new HttpClient();
            using var config = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync(
                new Uri(server.EntryUri, "/__livepaper/config.json"), TestContext.Current.CancellationToken));

            var directories = config.RootElement.GetProperty("directoryFiles");
            Assert.Equal(image, Assert.Single(directories.GetProperty("slideshow").EnumerateArray()).GetString());
            Assert.Single(directories.EnumerateObject());
            Assert.Equal("image", await client.GetStringAsync(
                new Uri(server.EntryUri, "/directories/slides/slide%201.jpg"), TestContext.Current.CancellationToken));
        }
        finally
        {
            root.Delete(recursive: true);
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
