namespace BrawlEngine.Host.Domain.Models;

public sealed record LocalDownloadDto(int Id, string Folder, int? MapCount = null, long SizeBytes = 0);

public sealed record LocalDownloadListDto(IReadOnlyList<LocalDownloadDto> Items);
