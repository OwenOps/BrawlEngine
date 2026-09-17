namespace BrawlEngine.Host.Domain.Models;

public sealed record CrashTagDto(int Id, string? Note);

public sealed record CrashesDto(
    IReadOnlyList<CrashTagDto> Maps,
    IReadOnlyList<CrashTagDto> Sounds,
    IReadOnlyList<CrashTagDto> Skins)
{
    public static CrashesDto Empty { get; } = new([], [], []);
}
