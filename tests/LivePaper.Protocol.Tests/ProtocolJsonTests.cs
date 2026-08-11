using Xunit;

namespace LivePaper.Protocol.Tests;

public class ProtocolJsonTests
{
    [Fact]
    public void VisibilityMessageRoundTrips()
    {
        var message = HostMessage.ForVisibility(new VisibilityChanged(
            VisibilityState.FullyCovered,
            ShouldRender: false,
            ShouldMute: true));

        var decoded = ProtocolJson.DeserializeHostMessage(ProtocolJson.Serialize(message));

        Assert.Equal(ProtocolVersion.Current, decoded.ProtocolVersion);
        Assert.NotNull(decoded.Visibility);
        Assert.False(decoded.Visibility.ShouldRender);
        Assert.True(decoded.Visibility.ShouldMute);
    }

    [Fact]
    public void PointerPositionMessageRoundTrips()
    {
        var position = new PointerPositionChanged(123.5, -42.25);
        var message = HostMessage.ForPointerPosition(position);

        var decoded = ProtocolJson.DeserializeHostMessage(ProtocolJson.Serialize(message));

        Assert.Equal(HostMessageKind.PointerPositionChanged, decoded.Kind);
        Assert.Equal(position, decoded.PointerPosition);
    }

    [Fact]
    public void AudioSpectrumMessageRoundTrips()
    {
        var samples = Enumerable.Range(0, AudioSpectrumChanged.SampleCount)
            .Select(index => index / 127f)
            .ToArray();
        var message = HostMessage.ForAudioSpectrum(new AudioSpectrumChanged(samples));

        var decoded = ProtocolJson.DeserializeHostMessage(ProtocolJson.Serialize(message));

        Assert.Equal(HostMessageKind.AudioSpectrumChanged, decoded.Kind);
        Assert.Equal(samples, decoded.AudioSpectrum?.Samples);
    }

    [Fact]
    public void RendererPresentationMessageRoundTrips()
    {
        var source = new Uri("http://127.0.0.1:12345/index.html");

        var decoded = ProtocolJson.DeserializeRendererMessage(
            ProtocolJson.Serialize(RendererMessage.ForPresentation(source)));

        Assert.Equal(ProtocolVersion.Current, decoded.ProtocolVersion);
        Assert.Equal(RendererMessageKind.PresentationReady, decoded.Kind);
        Assert.Equal(source.AbsoluteUri, decoded.Source);
    }
}
