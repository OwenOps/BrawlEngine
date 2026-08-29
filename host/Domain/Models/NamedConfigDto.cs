using System.Text.Json.Serialization;

namespace BrawlEngine.Host.Domain.Models;

public sealed record NamedConfigDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("maps")] IReadOnlyList<LoadoutModDto> Maps,
    [property: JsonPropertyName("music")] IReadOnlyList<LoadoutModDto> Music);

public sealed record NamedConfigListDto(
    [property: JsonPropertyName("configs")] IReadOnlyList<NamedConfigDto> Configs)
{
    public static NamedConfigListDto Empty { get; } = new([]);
}
