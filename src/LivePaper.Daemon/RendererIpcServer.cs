using System.Net.Sockets;
using System.Text;
using LivePaper.Protocol;

namespace LivePaper.Daemon;

public sealed class RendererIpcServer : IDisposable
{
    private readonly Socket _listener;
    private Socket? _connection;
    private StreamWriter? _writer;

    private RendererIpcServer(string socketPath, Socket listener)
    {
        SocketPath = socketPath;
        _listener = listener;
    }

    public string SocketPath { get; }

    public static RendererIpcServer Create()
    {
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            throw new IOException("XDG_RUNTIME_DIR is not set.");
        }

        var livePaperDirectory = Path.Combine(runtimeDirectory, "livepaper");
        Directory.CreateDirectory(livePaperDirectory);
        File.SetUnixFileMode(livePaperDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var socketPath = Path.Combine(livePaperDirectory, $"renderer-{Guid.NewGuid():N}.sock");
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        listener.Listen(1);
        return new(socketPath, listener);
    }

    public async Task AcceptAsync(CancellationToken cancellationToken)
    {
        _connection = await _listener.AcceptAsync(cancellationToken);
        var stream = new NetworkStream(_connection, ownsSocket: false);
        _writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };
    }

    public Task SendAsync(HostMessage message) =>
        _writer is null
            ? throw new InvalidOperationException("The renderer has not connected.")
            : _writer.WriteLineAsync(ProtocolJson.Serialize(message));

    public void Dispose()
    {
        _writer?.Dispose();
        _connection?.Dispose();
        _listener.Dispose();

        if (File.Exists(SocketPath))
        {
            File.Delete(SocketPath);
        }
    }
}
