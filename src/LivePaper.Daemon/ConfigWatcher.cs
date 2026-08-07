using System.Threading.Channels;

namespace LivePaper.Daemon;

public sealed class FileChangeWatcher : IDisposable
{
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(200);
    private readonly Channel<bool> _changes = Channel.CreateUnbounded<bool>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly FileSystemWatcher _watcher;

    public FileChangeWatcher(string path)
    {
        _watcher = new FileSystemWatcher(
            Path.GetDirectoryName(path)!,
            Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
    }

    public ValueTask<bool> WaitForChangeAsync(CancellationToken cancellationToken) =>
        _changes.Reader.ReadAsync(cancellationToken);

    public async Task DebounceAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(SettleTime, cancellationToken);
        while (_changes.Reader.TryRead(out _))
        {
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs args) => _changes.Writer.TryWrite(true);

    private void OnRenamed(object sender, RenamedEventArgs args) => _changes.Writer.TryWrite(true);

    public void Dispose()
    {
        _watcher.Dispose();
        _changes.Writer.TryComplete();
    }
}
