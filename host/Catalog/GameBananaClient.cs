using System.Net.Http.Headers;
using System.Text.Json;

namespace BrawlEngine.Host.Catalog;

public static class GameBananaClient
{
    public const int BrawlhallaGameId = 5704;
    public const int RealmsCategoryId = 6463;
    public const string RealmsCategoryName = "Realms";
    public const int PageSize = 15;

    private const string IndexUrl =
        "https://gamebanana.com/apiv11/Mod/Index?_nPage={0}&_nPerpage=15&_aFilters%5BGeneric_Category%5D=6463";

    private static readonly HttpClient Http = CreateHttp();

    public static async Task<CatalogPageDto> ListRealmsAsync(int page, CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        using var response = await Http.GetAsync(string.Format(IndexUrl, page), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        var complete = false;
        if (root.TryGetProperty("_aMetadata", out var meta)
            && meta.TryGetProperty("_bIsComplete", out var done))
        {
            complete = done.GetBoolean();
        }

        var items = new List<CatalogItemDto>();
        if (root.TryGetProperty("_aRecords", out var records) && records.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in records.EnumerateArray())
            {
                items.Add(ToItem(record));
            }

            if (items.Count < PageSize)
            {
                complete = true;
            }
        }
        else
        {
            complete = true;
        }

        return new CatalogPageDto(items, page + 1, complete);
    }

    public static async Task<DownloadResultDto> DownloadModAsync(int modId, CancellationToken cancellationToken = default)
    {
        if (modId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modId));
        }

        var profileUrl = $"https://gamebanana.com/apiv11/Mod/{modId}/ProfilePage";
        using var profileResponse = await Http.GetAsync(profileUrl, cancellationToken).ConfigureAwait(false);
        profileResponse.EnsureSuccessStatusCode();
        await using var profileStream = await profileResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(profileStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var files = ListDownloadableFiles(doc.RootElement);
        if (files.Count == 0)
        {
            throw new InvalidOperationException("This mod has no downloadable files.");
        }

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrawlEngine",
            "downloads",
            modId.ToString());
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

                using var download = await Http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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

        return new DownloadResultDto(modId, folder, saved);
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

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BrawlEngine", "0.1"));
        return http;
    }

    private static CatalogItemDto ToItem(JsonElement record)
    {
        var id = record.TryGetProperty("_idRow", out var idEl) ? idEl.GetInt32() : 0;
        var name = record.TryGetProperty("_sName", out var nameEl) ? nameEl.GetString() ?? "" : "";
        var profile = record.TryGetProperty("_sProfileUrl", out var urlEl) ? urlEl.GetString() ?? "" : "";
        var author = "";
        if (record.TryGetProperty("_aSubmitter", out var submitter)
            && submitter.TryGetProperty("_sName", out var authorEl))
        {
            author = authorEl.GetString() ?? "";
        }

        var category = RealmsCategoryName;
        if (record.TryGetProperty("_aRootCategory", out var cat)
            && cat.TryGetProperty("_sName", out var catName)
            && catName.GetString() is { Length: > 0 } n)
        {
            category = n;
        }

        return new CatalogItemDto(id, name, author, ThumbnailUrl(record), category, profile);
    }

    private static string? ThumbnailUrl(JsonElement record)
    {
        if (!record.TryGetProperty("_aPreviewMedia", out var media)
            || !media.TryGetProperty("_aImages", out var images)
            || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var image in images.EnumerateArray())
        {
            if (!image.TryGetProperty("_sBaseUrl", out var baseUrl))
            {
                continue;
            }

            var file = image.TryGetProperty("_sFile220", out var f220)
                ? f220.GetString()
                : image.TryGetProperty("_sFile100", out var f100)
                    ? f100.GetString()
                    : image.TryGetProperty("_sFile", out var f)
                        ? f.GetString()
                        : null;

            if (string.IsNullOrEmpty(file))
            {
                continue;
            }

            return baseUrl.GetString()?.TrimEnd('/') + "/" + file;
        }

        return null;
    }
}

public sealed record CatalogItemDto(
    int Id,
    string Name,
    string Author,
    string? ThumbnailUrl,
    string Category,
    string ProfileUrl);

public sealed record CatalogPageDto(
    IReadOnlyList<CatalogItemDto> Items,
    int NextApiPage,
    bool Complete);

public sealed record DownloadedFileDto(string Name, string Path, long Bytes);

public sealed record DownloadResultDto(int ModId, string Folder, IReadOnlyList<DownloadedFileDto> Files);
