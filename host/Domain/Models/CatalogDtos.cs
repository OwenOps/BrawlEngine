namespace BrawlEngine.Host.Domain.Models;

public sealed record CatalogItemDto(
    int Id,
    string Name,
    string Author,
    string? ThumbnailUrl,
    string Category,
    string ProfileUrl,
    string? SkinTarget = null,
    string? Description = null,
    bool Nsfw = false,
    string? AuthorUrl = null,
    int? AuthorId = null,
    string? AuthorAvatarUrl = null,
    int LikeCount = 0,
    int DownloadCount = 0);

public sealed record CatalogPageDto(
    IReadOnlyList<CatalogItemDto> Items,
    int Page,
    int NextApiPage,
    bool Complete,
    int TotalCount,
    int PageSize);

public sealed record SkinTargetRowDto(int Id, string? SkinTarget, string? Description = null);
