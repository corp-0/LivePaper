using LivePaper.Protocol;

namespace LivePaper.Daemon;

public interface IAudioSpectrumSource
{
    event EventHandler<AudioSpectrumChanged>? Changed;

    Task RunAsync(CancellationToken cancellationToken);
}
