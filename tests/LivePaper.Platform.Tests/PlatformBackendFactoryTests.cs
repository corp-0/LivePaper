using Xunit;

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
}
