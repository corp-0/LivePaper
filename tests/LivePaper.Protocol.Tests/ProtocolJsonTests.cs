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
}
