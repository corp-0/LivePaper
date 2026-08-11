using System.Threading.Channels;
using LivePaper.Protocol;

namespace LivePaper.Daemon;

public sealed class RendererUpdateBuffer
{
    private readonly object _lock = new();
    private readonly Channel<byte> _changed = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });
    private HostMessage? _visibility;
    private HostMessage? _pointerPosition;
    private HostMessage? _audioSpectrum;

    public void Write(HostMessage message)
    {
        lock (_lock)
        {
            switch (message.Kind)
            {
                case HostMessageKind.VisibilityChanged:
                    _visibility = message;
                    break;
                case HostMessageKind.PointerPositionChanged:
                    _pointerPosition = message;
                    break;
                case HostMessageKind.AudioSpectrumChanged:
                    _audioSpectrum = message;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(message), message.Kind, "Unknown host message kind.");
            }

            _changed.Writer.TryWrite(0);
        }
    }

    public async ValueTask<HostMessage> ReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _changed.Reader.ReadAsync(cancellationToken);
            lock (_lock)
            {
                var message = _visibility ?? _pointerPosition ?? _audioSpectrum;
                if (message is null)
                {
                    continue;
                }

                if (ReferenceEquals(message, _visibility))
                {
                    _visibility = null;
                }
                else if (ReferenceEquals(message, _pointerPosition))
                {
                    _pointerPosition = null;
                }
                else
                {
                    _audioSpectrum = null;
                }

                if (_visibility is not null || _pointerPosition is not null || _audioSpectrum is not null)
                {
                    _changed.Writer.TryWrite(0);
                }

                return message;
            }
        }
    }
}
