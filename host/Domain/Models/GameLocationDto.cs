namespace BrawlEngine.Host.Domain.Models;

public sealed record GameLocationDto(
    string? Path,
    bool Found,
    string Source,
    bool HasMapArt)
{
    public bool Cancelled { get; init; }

    public string? Error { get; init; }

    public string? Mp3Path { get; init; }

    public bool HasMp3 { get; init; }

    public string Mp3Source { get; init; } = "none";
}
