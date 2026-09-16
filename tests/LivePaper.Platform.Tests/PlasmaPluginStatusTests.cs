using LivePaper.Platform.KWin;
using Xunit;

namespace LivePaper.Platform.Tests;

public class PlasmaPluginStatusTests
{
    [Fact]
    public void RequiresCurrentQmlAndNativeVersionsOnEveryDesktop()
    {
        Assert.True(PlasmaPluginStatus.Evaluate("package:native", "package", ["package:native", "package:native"]).IsCurrent);
        Assert.False(PlasmaPluginStatus.Evaluate("package:native", "package", ["old:native"]).IsCurrent);
        var staleNative = PlasmaPluginStatus.Evaluate("package:native", "package", ["package:native", "package:old"]);
        Assert.False(staleNative.IsCurrent);
        Assert.Contains("Restart Plasma Shell", staleNative.Message);
    }

    [Fact]
    public void MissingReportsNeverCountAsSuccess()
    {
        var status = PlasmaPluginStatus.Evaluate("package:native", "package", ["package:native", ""]);
        Assert.False(status.IsCurrent);
        Assert.Contains("did not report", status.Message);
        Assert.Contains("cached", status.Message);
    }

    [Fact]
    public void DistinguishesNotInstalledAndNotActive()
    {
        var missing = PlasmaPluginStatus.Evaluate("package:native", null, ["package:native"]);
        Assert.False(missing.IsCurrent);
        Assert.Contains("installed package", missing.Message);
        var inactive = PlasmaPluginStatus.Evaluate("package:native", "package", []);
        Assert.False(inactive.IsCurrent);
        Assert.Contains("no desktop", inactive.Message);
    }

    [Fact]
    public void InstallationStampsTheVersionIntoTheQml()
    {
        var directory = Directory.CreateTempSubdirectory("livepaper-package-");
        var previous = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", directory.FullName);
            PlasmaWallpaperPackage.Install();
            var qml = File.ReadAllText(Path.Combine(directory.FullName, "plasma", "wallpapers",
                PlasmaWallpaperPackage.PluginId, "contents", "ui", "main.qml"));
            Assert.DoesNotContain("@LIVEPAPER_PACKAGE_VERSION@", qml);
            Assert.Contains(PlasmaWallpaperPackage.Version, qml);
            Assert.Equal(PlasmaWallpaperPackage.Version, PlasmaWallpaperPackage.InstalledVersion);
            Assert.Matches("^[0-9a-f]{64}:[0-9a-f]{64}$", PlasmaWallpaperPackage.ExpectedLoadedVersion);
            PlasmaWallpaperPackage.Install();
            Assert.Equal(PlasmaWallpaperPackage.Version, PlasmaWallpaperPackage.InstalledVersion);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previous);
            directory.Delete(recursive: true);
        }
    }
}
