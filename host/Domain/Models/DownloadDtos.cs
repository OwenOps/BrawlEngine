namespace BrawlEngine.Host.Domain.Models;

public sealed record DownloadedFileDto(string Name, string Path, long Bytes);

public sealed record DownloadResultDto(int ModId, string Folder, IReadOnlyList<DownloadedFileDto> Files);
