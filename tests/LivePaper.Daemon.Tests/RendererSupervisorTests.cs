using System.Net.Sockets;
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
}
