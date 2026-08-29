using System.Text.Json.Serialization;

namespace BrawlEngine.Host.Domain.Models;

public sealed record LoadoutModDto(
    [property: JsonPropertyName("modId")] int ModId);

public sealed record LoadoutDto(
    [property: JsonPropertyName("maps")] IReadOnlyList<LoadoutModDto> Maps,
    [property: JsonPropertyName("music")] IReadOnlyList<LoadoutModDto> Music)
{
    public static LoadoutDto Empty { get; } = new([], []);
}
