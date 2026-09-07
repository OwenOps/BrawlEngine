namespace BrawlEngine.Host.Domain.Models;

public sealed record CatalogItemDto(
    int Id,
    string Name,
    string Author,
    string? ThumbnailUrl,
    string Category,
    string ProfileUrl);

public sealed record CatalogPageDto(
    IReadOnlyList<CatalogItemDto> Items,
    int Page,
    int NextApiPage,
    bool Complete,
    int TotalCount,
    int PageSize);
