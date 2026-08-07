using System.Text.Json.Serialization;

namespace LivePaper.Protocol;

[JsonConverter(typeof(JsonStringEnumConverter<HostMessageKind>))]
public enum HostMessageKind
{
    VisibilityChanged,
    PointerPositionChanged
}

public record HostMessage(
    int ProtocolVersion,
    HostMessageKind Kind,
    VisibilityChanged? Visibility,
    PointerPositionChanged? PointerPosition)
{
    public static HostMessage ForVisibility(VisibilityChanged visibility) =>
        new(Protocol.ProtocolVersion.Current, HostMessageKind.VisibilityChanged, visibility, null);

    public static HostMessage ForInitialState(
        VisibilityChanged visibility,
        PointerPositionChanged? pointerPosition) =>
        new(Protocol.ProtocolVersion.Current, HostMessageKind.VisibilityChanged, visibility, pointerPosition);

    public static HostMessage ForPointerPosition(PointerPositionChanged pointerPosition) =>
        new(Protocol.ProtocolVersion.Current, HostMessageKind.PointerPositionChanged, null, pointerPosition);
}

public record PointerPositionChanged(double X, double Y);
