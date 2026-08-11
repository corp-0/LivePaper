using System.Net.Sockets;
using System.Text;
using LivePaper.Protocol;
using Xunit;

namespace LivePaper.Daemon.Tests;

public class RendererSupervisorTests
{
    [Fact]
    public async Task HandshakeTimeoutStopsApplyingAfterConnection()
    {
        using var ipc = RendererIpcServer.Create();
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var daemonCancellation = new CancellationTokenSource();
        var timeout = TimeSpan.FromMilliseconds(50);

        var accept = RendererSupervisor.AcceptRendererAsync(ipc, timeout, daemonCancellation.Token);
        await client.ConnectAsync(
            new UnixDomainSocketEndPoint(ipc.SocketPath),
            TestContext.Current.CancellationToken);
        await accept;
        await Task.Delay(timeout * 2, TestContext.Current.CancellationToken);

        Assert.False(daemonCancellation.IsCancellationRequested);
        await ipc.SendAsync(HostMessage.ForInitialState(
            new VisibilityChanged(
                VisibilityState.Visible,
                ShouldRender: true,
                ShouldMute: false),
            pointerPosition: null));
    }

    [Fact]
    public async Task HandshakeTimesOutBeforeConnection()
    {
        using var ipc = RendererIpcServer.Create();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RendererSupervisor.AcceptRendererAsync(
                ipc,
                TimeSpan.FromMilliseconds(20),
                CancellationToken.None));
    }

    [Fact]
    public async Task ReadsLoopbackRendererPresentation()
    {
        using var ipc = RendererIpcServer.Create();
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await client.ConnectAsync(
            new UnixDomainSocketEndPoint(ipc.SocketPath),
            TestContext.Current.CancellationToken);
        await ipc.AcceptAsync(TestContext.Current.CancellationToken);
        using var writer = new StreamWriter(
            new NetworkStream(client, ownsSocket: false),
            new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        var expected = new Uri("http://127.0.0.1:12345/index.html");

        await writer.WriteLineAsync(ProtocolJson.Serialize(RendererMessage.ForPresentation(expected)));
        var actual = await RendererSupervisor.ReadPresentationAsync(
            ipc,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
    }
}
