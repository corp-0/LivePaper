using Xunit;

namespace LivePaper.Daemon.Tests;

public class FileChangeWatcherTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DetectsRepeatedSaves(bool symbolicLink, bool replaceFile)
    {
        var directory = Directory.CreateTempSubdirectory("livepaper-watcher-");
        try
        {
            var targetDirectory = Directory.CreateDirectory(Path.Combine(directory.FullName, "dotfiles"));
            var target = Path.Combine(targetDirectory.FullName, "livepaper.toml");
            await File.WriteAllTextAsync(target, "initial", TestContext.Current.CancellationToken);
            var watchedPath = target;
            if (symbolicLink)
            {
                watchedPath = Path.Combine(directory.FullName, "livepaper.toml");
                File.CreateSymbolicLink(watchedPath, Path.GetRelativePath(directory.FullName, target));
            }

            using var watcher = new FileChangeWatcher(watchedPath);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            for (var save = 0; save < 2; save++)
            {
                var changed = watcher.WaitForChangeAsync(timeout.Token).AsTask();
                if (replaceFile)
                {
                    var temporary = target + ".tmp";
                    await File.WriteAllTextAsync(temporary, $"save {save}", timeout.Token);
                    File.Move(temporary, target, overwrite: true);
                }
                else
                {
                    await File.WriteAllTextAsync(watchedPath, $"save {save}", timeout.Token);
                }

                Assert.True(await changed);
                await watcher.DebounceAsync(timeout.Token);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task DetectsCreationOfMissingConfig()
    {
        var directory = Directory.CreateTempSubdirectory("livepaper-watcher-");
        try
        {
            var path = Path.Combine(directory.FullName, "livepaper.toml");
            using var watcher = new FileChangeWatcher(path);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var changed = watcher.WaitForChangeAsync(timeout.Token).AsTask();

            await File.WriteAllTextAsync(path, "config", timeout.Token);

            Assert.True(await changed);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
