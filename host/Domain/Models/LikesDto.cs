namespace BrawlEngine.Host.Domain.Models;

public sealed record LikesDto(
    IReadOnlyList<int> Maps,
    IReadOnlyList<int> Sounds,
    IReadOnlyList<int> Skins)
{
    public static LikesDto Empty { get; } = new([], [], []);
}
