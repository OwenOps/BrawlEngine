namespace BrawlEngine.Host.Domain.Models;

public sealed record LocalDownloadDto(
    int Id,
    string Folder,
    int? MapCount = null,
    long SizeBytes = 0,
    DateTimeOffset? DownloadedUtc = null);

public sealed record LocalDownloadListDto(IReadOnlyList<LocalDownloadDto> Items);

public sealed record DownloadSummaryDto(int Count, long SizeBytes, string Folder = "", bool IsDefault = true);

public sealed record DownloadsLocationDto(
    string Path,
    bool Cancelled = false,
    string? Error = null);

public sealed record DownloadImportDto(
    int Id,
    string Folder,
    int FileCount,
    bool Cancelled = false,
    string? Error = null);
