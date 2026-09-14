using System.Text.Json.Serialization;

namespace BrawlEngine.Host.Domain.Models;

public sealed record LoadoutModDto(
    [property: JsonPropertyName("modId")] int ModId,
    [property: JsonPropertyName("swfs")] IReadOnlyList<string>? Swfs = null);

public sealed record LoadoutDto(
    [property: JsonPropertyName("maps")] IReadOnlyList<LoadoutModDto> Maps,
    [property: JsonPropertyName("music")] IReadOnlyList<LoadoutModDto> Music,
    [property: JsonPropertyName("skins")] IReadOnlyList<LoadoutModDto>? Skins)
{
    public static LoadoutDto Empty { get; } = new([], [], []);
}
