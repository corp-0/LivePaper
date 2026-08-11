using System.Text.Json;
using System.Text.Json.Serialization;

namespace LivePaper.Protocol;

public static class ProtocolJson
{
    public static string Serialize(HostMessage message) =>
        JsonSerializer.Serialize(message, LivePaperProtocolJsonContext.Default.HostMessage);

    public static HostMessage DeserializeHostMessage(string json) =>
        JsonSerializer.Deserialize(json, LivePaperProtocolJsonContext.Default.HostMessage)
        ?? throw new JsonException("The protocol message is empty.");

    public static string Serialize(RendererMessage message) =>
        JsonSerializer.Serialize(message, LivePaperProtocolJsonContext.Default.RendererMessage);

    public static RendererMessage DeserializeRendererMessage(string json) =>
        JsonSerializer.Deserialize(json, LivePaperProtocolJsonContext.Default.RendererMessage)
        ?? throw new JsonException("The protocol message is empty.");
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(HostMessage))]
[JsonSerializable(typeof(RendererMessage))]
[JsonSerializable(typeof(float[]))]
public sealed partial class LivePaperProtocolJsonContext : JsonSerializerContext;
