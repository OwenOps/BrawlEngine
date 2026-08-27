namespace BrawlEngine.Host.Domain.Models;

public sealed record GameLocationDto(
    string? Path,
    bool Found,
    string Source,
    bool HasMapArt)
{
    public bool Cancelled { get; init; }

    public string? Error { get; init; }
}
