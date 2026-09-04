namespace BrawlEngine.Host.Domain.Models;

public sealed record Mp3LocationDto(string? Path, bool Found, string Source)
{
    public bool Cancelled { get; init; }

    public string? Error { get; init; }
}
