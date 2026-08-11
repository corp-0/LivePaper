using System.Text.Json.Serialization;

namespace LivePaper.Protocol;

[JsonConverter(typeof(JsonStringEnumConverter<RendererMessageKind>))]
public enum RendererMessageKind
{
    PresentationReady
}

public record RendererMessage(
    int ProtocolVersion,
    RendererMessageKind Kind,
    string? Source)
{
    public static RendererMessage ForPresentation(Uri source) => new(
        Protocol.ProtocolVersion.Current,
        RendererMessageKind.PresentationReady,
        source.AbsoluteUri);
}
