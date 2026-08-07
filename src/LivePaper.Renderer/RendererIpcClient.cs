using System.Net.Sockets;
using System.Text;
using LivePaper.Protocol;

namespace LivePaper.Renderer;

public sealed class RendererIpcClient : IDisposable
{
    private readonly Socket _socket;
    private readonly StreamReader _reader;

    private RendererIpcClient(Socket socket)
    {
        _socket = socket;
        _reader = new StreamReader(new NetworkStream(socket, ownsSocket: false), new UTF8Encoding(false));
    }

    public VisibilityChanged? InitialVisibility { get; set; }

    public PointerPositionChanged? InitialPointerPosition { get; set; }

    public static async Task<RendererIpcClient> ConnectAsync(string socketPath, CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
            return new(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async Task<HostMessage> ReadAsync(CancellationToken cancellationToken)
    {
        var line = await _reader.ReadLineAsync(cancellationToken)
            ?? throw new EndOfStreamException("The daemon closed the protocol connection.");
        var message = ProtocolJson.DeserializeHostMessage(line);

        if (message.ProtocolVersion != ProtocolVersion.Current)
        {
            throw new InvalidDataException(
                $"Protocol version {message.ProtocolVersion} is incompatible with {ProtocolVersion.Current}.");
        }

        return message;
    }

    public async Task ListenAsync(
        Action<VisibilityChanged> onVisibility,
        Action<PointerPositionChanged> onPointerPosition,
        Action<AudioSpectrumChanged> onAudioSpectrum,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await ReadAsync(cancellationToken);
            if (message.Kind == HostMessageKind.VisibilityChanged && message.Visibility is { } visibility)
            {
                onVisibility(visibility);
            }
            else if (message.Kind == HostMessageKind.PointerPositionChanged &&
                message.PointerPosition is { } pointerPosition)
            {
                onPointerPosition(pointerPosition);
            }
            else if (message.Kind == HostMessageKind.AudioSpectrumChanged &&
                message.AudioSpectrum is { } audioSpectrum)
            {
                onAudioSpectrum(audioSpectrum);
            }
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        _socket.Dispose();
    }
}
