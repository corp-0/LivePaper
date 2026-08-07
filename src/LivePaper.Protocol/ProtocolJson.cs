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
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(HostMessage))]
public sealed partial class LivePaperProtocolJsonContext : JsonSerializerContext;
