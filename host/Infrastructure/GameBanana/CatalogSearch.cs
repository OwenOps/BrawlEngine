using System.Text.Json;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Networking;

namespace BrawlEngine.Host.Infrastructure.GameBanana;

/// <summary>
/// GameBanana Index does not accept a plain Generic_Name string (400 / empty body).
/// Site search works: Util/Search/Results, then keep rows whose name contains the query.
/// </summary>
public static class CatalogSearch
{
    public const int PageSize = 15;
    private const int MaxSearchPages = 8;

    public static string SortAlias(string? sort)
    {
        return sort switch
        {
            "liked" => "Generic_MostLiked",
            "downloaded" => "Generic_MostDownloaded",
            _ => "Generic_Newest",
        };
    }

    public static async Task<CatalogPageDto> SearchAsync(
        int page,
        string query,
        string? sort,
        Func<JsonElement, bool> keep,
        Func<JsonElement, CatalogItemDto> toItem,
        CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            page = 1;
        }

        var ranked = new List<RankedItem>();
        for (var apiPage = 1; apiPage <= MaxSearchPages; apiPage++)
        {
            try
            {
                var url =
                    "https://gamebanana.com/apiv11/Util/Search/Results?_sSearchString="
                    + Uri.EscapeDataString(query)
                    + "&_nPage="
                    + apiPage
                    + "&_nPerpage=15&_idGameRow="
                    + GameBananaIds.BrawlhallaGameId;

                using var response = await AppHttp.Shared.GetAsync(url, cancellationToken).ConfigureAwait(false);
                using var doc = await GameBananaJson.ReadDocumentAsync(response, cancellationToken).ConfigureAwait(false);
                var root = doc.RootElement;

                var complete = true;
                if (root.TryGetProperty("_aMetadata", out var meta)
                    && meta.TryGetProperty("_bIsComplete", out var done))
                {
                    complete = done.GetBoolean();
                }

                if (root.TryGetProperty("_aRecords", out var records) && records.ValueKind == JsonValueKind.Array)
                {
                    foreach (var record in records.EnumerateArray())
                    {
                        if (!keep(record))
                        {
                            continue;
                        }

                        ranked.Add(new RankedItem(
                            toItem(record),
                            ReadInt64(record, "_tsDateAdded"),
                            ReadInt(record, "_nLikeCount"),
                            ReadInt(record, "_nDownloadCount")));
                    }
                }

                if (complete)
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (ranked.Count > 0)
            {
                break;
            }
        }

        Sort(ranked, sort);
        var total = ranked.Count;
        var slice = ranked
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(row => row.Item)
            .ToList();
        var completePage = page * PageSize >= total || slice.Count == 0;
        return new CatalogPageDto(slice, page, page + 1, completePage, total, PageSize);
    }

    public static CatalogPageDto ParseIndex(JsonElement root, int page, Func<JsonElement, CatalogItemDto> toItem)
    {
        var totalCount = 0;
        var apiComplete = false;
        if (root.TryGetProperty("_aMetadata", out var meta))
        {
            if (meta.TryGetProperty("_nRecordCount", out var countEl) && countEl.TryGetInt32(out var count))
            {
                totalCount = count;
            }

            if (meta.TryGetProperty("_bIsComplete", out var done))
            {
                apiComplete = done.GetBoolean();
            }
        }

        var items = new List<CatalogItemDto>();
        if (root.TryGetProperty("_aRecords", out var records) && records.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in records.EnumerateArray())
            {
                items.Add(toItem(record));
            }
        }

        var complete = totalCount > 0
            ? page * PageSize >= totalCount
            : apiComplete || items.Count == 0;

        return new CatalogPageDto(items, page, page + 1, complete, totalCount, PageSize);
    }

    public static bool NameContains(JsonElement record, string query)
    {
        var name = record.TryGetProperty("_sName", out var nameEl) ? nameEl.GetString() ?? "" : "";
        return name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsModel(JsonElement record, string model)
    {
        return record.TryGetProperty("_sModelName", out var modelEl)
            && string.Equals(modelEl.GetString(), model, StringComparison.Ordinal);
    }

    public static int RootCategoryId(JsonElement record)
    {
        if (!record.TryGetProperty("_aRootCategory", out var cat)
            || !cat.TryGetProperty("_sProfileUrl", out var urlEl))
        {
            return 0;
        }

        var url = urlEl.GetString() ?? "";
        var slash = url.LastIndexOf('/');
        if (slash < 0 || slash == url.Length - 1)
        {
            return 0;
        }

        return int.TryParse(url[(slash + 1)..], out var id) ? id : 0;
    }

    public static bool IsRealmsMod(JsonElement record)
    {
        if (!IsModel(record, "Mod"))
        {
            return false;
        }

        if (!record.TryGetProperty("_aRootCategory", out var cat)
            || !cat.TryGetProperty("_sName", out var catName))
        {
            return false;
        }

        return string.Equals(catName.GetString(), GameBananaIds.RealmsCategoryName, StringComparison.Ordinal);
    }

    public static bool IsSkinsMod(JsonElement record)
    {
        if (!IsModel(record, "Mod"))
        {
            return false;
        }

        if (!record.TryGetProperty("_aRootCategory", out var cat)
            || !cat.TryGetProperty("_sName", out var catName))
        {
            return false;
        }

        return string.Equals(catName.GetString(), GameBananaIds.SkinsCategoryName, StringComparison.Ordinal);
    }

    public static string? ThumbnailUrl(JsonElement record)
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

    public static CatalogItemDto FromRecord(JsonElement record, string defaultCategory)
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

        var category = ReadCategoryName(record) ?? defaultCategory;

        return new CatalogItemDto(id, name, author, ThumbnailUrl(record), category, profile);
    }

    /// <summary>Index uses _aRootCategory; ProfilePage uses _aCategory.</summary>
    public static string? ReadCategoryName(JsonElement record)
    {
        if (TryName(record, "_aRootCategory", out var root))
        {
            return root;
        }

        if (TryName(record, "_aCategory", out var category))
        {
            return category;
        }

        return null;
    }

    private static bool TryName(JsonElement record, string property, out string name)
    {
        name = "";
        if (!record.TryGetProperty(property, out var cat)
            || !cat.TryGetProperty("_sName", out var catName)
            || catName.GetString() is not { Length: > 0 } n)
        {
            return false;
        }

        name = n;
        return true;
    }

    public static CatalogItemDto Stub(int id, string itemType, string defaultCategory)
    {
        var path = string.Equals(itemType, GameBananaIds.SoundItemType, StringComparison.Ordinal)
            ? "sounds"
            : "mods";
        return new CatalogItemDto(
            id,
            "Item " + id,
            "",
            null,
            defaultCategory,
            "https://gamebanana.com/" + path + "/" + id);
    }

    private static void Sort(List<RankedItem> ranked, string? sort)
    {
        ranked.Sort((a, b) => sort switch
        {
            "liked" => b.Likes.CompareTo(a.Likes),
            "downloaded" => b.Downloads.CompareTo(a.Downloads),
            _ => b.Added.CompareTo(a.Added),
        });
    }

    private static int ReadInt(JsonElement record, string name)
    {
        return record.TryGetProperty(name, out var el) && el.TryGetInt32(out var value) ? value : 0;
    }

    private static long ReadInt64(JsonElement record, string name)
    {
        return record.TryGetProperty(name, out var el) && el.TryGetInt64(out var value) ? value : 0;
    }

    private sealed record RankedItem(CatalogItemDto Item, long Added, int Likes, int Downloads);
}
