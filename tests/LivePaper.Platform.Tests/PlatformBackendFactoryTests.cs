using Xunit;
using LivePaper.Platform.KWin;

namespace LivePaper.Platform.Tests;

public class PlatformBackendFactoryTests
{
    [Theory]
    [InlineData("KDE", true)]
    [InlineData("plasma", true)]
    [InlineData("ubuntu:KDE", true)]
    [InlineData("KDE:ubuntu", true)]
    [InlineData(" GNOME : Plasma ", true)]
    [InlineData("GNOME", false)]
    [InlineData("KDEConnect", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SelectBackendUsesDesktopSession(
        string? desktop,
        bool isKWinExpected)
    {
        var expected = isKWinExpected
            ? PlatformBackendKind.KWin
            : PlatformBackendKind.Unsupported;

        Assert.Equal(expected, PlatformBackendFactory.SelectBackend(desktop));
    }

    [Fact]
    public void BackendInfoDescribesPresentationWithoutExposingImplementationTypes()
    {
        var backend = PlatformBackendFactory.GetBackendInfo("KDE");

        Assert.NotNull(backend);
        Assert.Equal("KWin/Plasma", backend.DisplayName);
        Assert.True(backend.HostsWebContent);
        Assert.Null(PlatformBackendFactory.GetBackendInfo("GNOME"));
    }

    [Fact]
    public void KWinResourcesFollowBackendNamespace()
    {
        var resources = typeof(KWinPlatformBackend).Assembly.GetManifestResourceNames();

        Assert.Contains("LivePaper.Platform.KWin.visibility.js", resources);
        Assert.Contains("LivePaper.Platform.KWin.pointer-position.js", resources);
        Assert.Contains(
            "LivePaper.Platform.KWin.PlasmaWallpaper.contents.ui.main.qml",
            resources);
        Assert.Contains(
            "LivePaper.Platform.KWin.PlasmaWallpaper.contents.ui.input.qmldir",
            resources);
        Assert.Contains(
            "LivePaper.Platform.KWin.PlasmaWallpaper.contents.ui.input.liblivepaperinput.so",
            resources);
    }
}
