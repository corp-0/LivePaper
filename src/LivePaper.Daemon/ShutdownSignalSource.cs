using System.Runtime.InteropServices;

namespace LivePaper.Daemon;

public sealed class ShutdownSignalSource : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly PosixSignalRegistration _sigterm;
    private readonly PosixSignalRegistration _sigint;

    public ShutdownSignalSource()
    {
        _sigterm = Register(PosixSignal.SIGTERM);
        _sigint = Register(PosixSignal.SIGINT);
    }

    public CancellationToken Token => _source.Token;

    public bool IsCancellationRequested => _source.IsCancellationRequested;

    public void Dispose()
    {
        _sigint.Dispose();
        _sigterm.Dispose();
        _source.Dispose();
    }

    private PosixSignalRegistration Register(PosixSignal signal) =>
        PosixSignalRegistration.Create(signal, context =>
        {
            context.Cancel = true;
            _source.Cancel();
        });
}
