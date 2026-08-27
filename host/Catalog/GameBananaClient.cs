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

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BrawlEngine", "0.1"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
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
