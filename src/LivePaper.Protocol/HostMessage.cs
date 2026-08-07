using System.Text.Json.Serialization;

namespace LivePaper.Protocol;

[JsonConverter(typeof(JsonStringEnumConverter<HostMessageKind>))]
public enum HostMessageKind
{
    VisibilityChanged,
    PointerPositionChanged,
    AudioSpectrumChanged
}

public record HostMessage(
    int ProtocolVersion,
    HostMessageKind Kind,
    VisibilityChanged? Visibility,
    PointerPositionChanged? PointerPosition,
    AudioSpectrumChanged? AudioSpectrum)
{
    public static HostMessage ForVisibility(VisibilityChanged visibility) =>
        new(Protocol.ProtocolVersion.Current, HostMessageKind.VisibilityChanged, visibility, null, null);

    public static HostMessage ForInitialState(
        VisibilityChanged visibility,
        PointerPositionChanged? pointerPosition) =>
        new(Protocol.ProtocolVersion.Current, HostMessageKind.VisibilityChanged, visibility, pointerPosition, null);

    public static HostMessage ForPointerPosition(PointerPositionChanged pointerPosition) =>
        new(Protocol.ProtocolVersion.Current, HostMessageKind.PointerPositionChanged, null, pointerPosition, null);

    public static HostMessage ForAudioSpectrum(AudioSpectrumChanged audioSpectrum) =>
        new(Protocol.ProtocolVersion.Current, HostMessageKind.AudioSpectrumChanged, null, null, audioSpectrum);
}

public record PointerPositionChanged(double X, double Y);

public record AudioSpectrumChanged(float[] Samples)
{
    public const int SampleCount = 128;
}
