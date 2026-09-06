using System.Text.Json;
using System.Linq;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Networking;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.GameBanana;

public static class ModDownloadClient
{
    /// <summary>How often, at most, we call back with a progress update while streaming one file (avoids flooding the IPC channel).</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(120);

    public static async Task<DownloadResultDto> DownloadAsync(
        int id,
        string itemType = "Mod",
        string? destFolder = null,
        string progressKind = "maps",
        Action<DownloadProgressDto>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        var profileUrl = $"https://gamebanana.com/apiv11/{itemType}/{id}/ProfilePage";
        using var profileResponse = await AppHttp.Shared.GetAsync(profileUrl, cancellationToken).ConfigureAwait(false);
        profileResponse.EnsureSuccessStatusCode();
        await using var profileStream = await profileResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(profileStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var files = ListDownloadableFiles(doc.RootElement);
        if (files.Count == 0)
        {
            throw new InvalidOperationException("This item has no downloadable files.");
        }

        var folder = destFolder
            ?? (itemType == GameBananaIds.SoundItemType
                ? AppPaths.SoundDownloadsFolder(id)
                : AppPaths.DownloadsFolder(id));
        Directory.CreateDirectory(folder);

        var totalBytes = files.Sum(file => file.Size);
        var bytesBeforeCurrentFile = 0L;
        var saved = new List<DownloadedFileDto>();
        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            var file = files[fileIndex];
            var dest = Path.Combine(folder, SafeFileName(file.Name));
            try
            {
                if (File.Exists(dest) && new FileInfo(dest).Length == file.Size && file.Size > 0)
                {
                    saved.Add(new DownloadedFileDto(file.Name, dest, file.Size));
                    bytesBeforeCurrentFile += file.Size;
                    continue;
                }

                using var download = await AppHttp.Shared
                    .GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                download.EnsureSuccessStatusCode();
                await using var input = await download.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
                await CopyWithProgressAsync(
                    input,
                    output,
                    onProgress is null
                        ? null
                        : downloadedInFile => onProgress(new DownloadProgressDto(
                            progressKind,
                            id,
                            file.Name,
                            fileIndex + 1,
                            files.Count,
                            bytesBeforeCurrentFile + downloadedInFile,
                            totalBytes)),
                    cancellationToken).ConfigureAwait(false);
                var savedBytes = new FileInfo(dest).Length;
                saved.Add(new DownloadedFileDto(file.Name, dest, savedBytes));
                bytesBeforeCurrentFile += savedBytes;
            }
            catch
            {
                if (File.Exists(dest))
                {
                    File.Delete(dest);
                }

                throw;
            }
        }

        return new DownloadResultDto(id, folder, saved);
    }

    private static async Task CopyWithProgressAsync(
        Stream input,
        Stream output,
        Action<long>? onProgress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var downloaded = 0L;
        var lastReport = DateTime.UtcNow;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            downloaded += read;

            var now = DateTime.UtcNow;
            if (onProgress is not null && now - lastReport >= ProgressInterval)
            {
                lastReport = now;
                onProgress(downloaded);
            }
        }

        onProgress?.Invoke(downloaded);
    }

    private static List<(string Name, string Url, long Size)> ListDownloadableFiles(JsonElement root)
    {
        var list = new List<(string, string, long)>();
        if (!root.TryGetProperty("_aFiles", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var file in files.EnumerateArray())
        {
            if (!file.TryGetProperty("_sDownloadUrl", out var urlEl))
            {
                continue;
            }

            var url = urlEl.GetString();
            if (string.IsNullOrEmpty(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = file.TryGetProperty("_sFile", out var nameEl) ? nameEl.GetString() ?? "mod.bin" : "mod.bin";
            var size = file.TryGetProperty("_nFilesize", out var sizeEl) && sizeEl.TryGetInt64(out var n) ? n : 0;
            list.Add((name, url, size));
        }

        return list;
    }

    private static string SafeFileName(string name)
    {
        var file = Path.GetFileName(name);
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            file = file.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(file) ? "mod.bin" : file;
    }
}
