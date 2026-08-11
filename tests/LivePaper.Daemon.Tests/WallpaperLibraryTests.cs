using LivePaper.Protocol;
using Xunit;

namespace LivePaper.Daemon.Tests;

public class WallpaperLibraryTests
{
    [Fact]
    public void FindsExecutableAudioDriverOnPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"livepaper-driver-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "pw-record");
            File.WriteAllText(executable, string.Empty);
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            Assert.Equal(executable, WallpaperLibrary.FindExecutable("pw-record", root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FixDependenciesFindsWallpaperByManifestId()
    {
        var root = Path.Combine(Path.GetTempPath(), $"livepaper-library-test-{Guid.NewGuid():N}");
        var library = Path.Combine(root, "library");
        var wallpaper = Path.Combine(library, "unrelated-directory-name");
        var dependency = Path.Combine(root, "workshop", "67890");
        Directory.CreateDirectory(wallpaper);
        Directory.CreateDirectory(dependency);
        try
        {
            File.WriteAllText(Path.Combine(wallpaper, "index.html"), "<!doctype html>");
            File.WriteAllText(Path.Combine(wallpaper, "manifest.toml"), """
                format_version = 1
                id = "wallpaper-engine.kei"
                name = "Kei"
                entry = "index.html"
                capabilities = []
                """);
            File.WriteAllText(Path.Combine(dependency, "base.js"), "window.loaded = true;");

            var result = WallpaperLibrary.FixDependencies(
                "wallpaper-engine.kei",
                [dependency],
                library);
            var manifest = WallpaperManifest.Load(File.ReadAllText(Path.Combine(wallpaper, "manifest.toml")));

            Assert.Equal(wallpaper, result);
            Assert.Equal(["67890"], WallpaperLibrary.LoadRecordedDependencies(wallpaper, manifest));
            Assert.True(File.Exists(Path.Combine(wallpaper, "67890", "base.js")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DoctorReportsMissingDependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), $"livepaper-library-test-{Guid.NewGuid():N}");
        var wallpaper = Path.Combine(root, "wallpaper");
        Directory.CreateDirectory(wallpaper);
        try
        {
            File.WriteAllText(Path.Combine(wallpaper, "index.html"), "<!doctype html>");
            File.WriteAllText(Path.Combine(wallpaper, "manifest.toml"), """
                format_version = 1
                id = "wallpaper.test"
                name = "Test"
                entry = "index.html"
                capabilities = []

                [wallpaper_engine]
                dependencies = ["67890"]
                """);

            Assert.NotEqual(0, WallpaperLibrary.Doctor(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InvalidTomlDiagnosticNamesWallpaperKeyValueAndExpectedType()
    {
        var diagnostic = WallpaperLibrary.FormatInvalidToml(
            "/library/unrelated-directory",
            """
                format_version = 1
                id = "wallpaper-engine.kei"

                [wallpaper_engine.properties]
                "mouseactions" = tue
                "mousetracking" = true
                """,
            new InvalidDataException(
                "(5,1) : error : Reason: (5,18) : error : Unexpected token `tue` while parsing a value."));

        Assert.Equal(
            """
            wallpaper-engine.kei:
              Invalid TOML
              Key: wallpaper_engine.properties.mouseactions
              Value: tue
              Expected: boolean (`true` or `false`)
            """.ReplaceLineEndings(),
            diagnostic.ReplaceLineEndings());
    }
}
