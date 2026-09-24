using System.Text.Json.Serialization;

namespace BrawlEngine.Host.Domain.Models;

public sealed class StatsRecentDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
