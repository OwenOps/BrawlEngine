namespace BrawlEngine.Host.Domain.Models;

public sealed record DownloadedFileDto(string Name, string Path, long Bytes);

public sealed record DownloadResultDto(int ModId, string Folder, IReadOnlyList<DownloadedFileDto> Files);

/// <summary>Progress snapshot pushed to the UI while a download is running (bytes, not just done/not-done).</summary>
public sealed record DownloadProgressDto(
    string Kind,
    int Id,
    string FileName,
    int FileIndex,
    int FileCount,
    long BytesDownloaded,
    long TotalBytes);
