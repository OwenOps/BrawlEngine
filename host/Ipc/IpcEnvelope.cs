using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrawlEngine.Host.Ipc;

public sealed class IpcEnvelope
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }
}
