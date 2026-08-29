using System.Text.Json;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Networking;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.GameBanana;

public static class ModDownloadClient
{
    public static async Task<DownloadResultDto> DownloadAsync(
        int id,
        string itemType = "Mod",
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

        var folder = itemType == GameBananaIds.SoundItemType
            ? AppPaths.SoundDownloadsFolder(id)
            : AppPaths.DownloadsFolder(id);
        Directory.CreateDirectory(folder);

        var saved = new List<DownloadedFileDto>();
        foreach (var file in files)
        {
            var dest = Path.Combine(folder, SafeFileName(file.Name));
            try
            {
                if (File.Exists(dest) && new FileInfo(dest).Length == file.Size && file.Size > 0)
                {
                    saved.Add(new DownloadedFileDto(file.Name, dest, file.Size));
                    continue;
                }

                using var download = await AppHttp.Shared
                    .GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                download.EnsureSuccessStatusCode();
                await using var input = await download.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                saved.Add(new DownloadedFileDto(file.Name, dest, new FileInfo(dest).Length));
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
