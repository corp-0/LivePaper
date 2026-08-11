using LivePaper.Protocol;
using Xunit;

namespace LivePaper.Daemon.Tests;

public class RendererUpdateBufferTests
{
    [Fact]
    public async Task CoalescesHighFrequencyUpdatesWithoutDroppingVisibility()
    {
        var queue = new RendererUpdateBuffer();
        var firstAudio = HostMessage.ForAudioSpectrum(new AudioSpectrumChanged(new float[128]));
        var latestAudio = HostMessage.ForAudioSpectrum(new AudioSpectrumChanged(
            Enumerable.Repeat(1f, 128).ToArray()));
        var visibility = HostMessage.ForVisibility(new VisibilityChanged(
            VisibilityState.FullyCovered,
            ShouldRender: false,
            ShouldMute: true));

        queue.Write(firstAudio);
        queue.Write(latestAudio);
        queue.Write(visibility);

        Assert.Same(visibility, await queue.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Same(latestAudio, await queue.ReadAsync(TestContext.Current.CancellationToken));
    }
}
