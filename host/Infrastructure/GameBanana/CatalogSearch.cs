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
    public const string IndexCountFields = "&_csvProperties=_nLikeCount%2C_nDownloadCount";
    private const int MaxSearchPages = 8;
    private static readonly string[] NsfwHints =
    [
        "nsfw",
        "nude",
        "naked",
        "hentai",
        "lewd",
        "porn",
        "r-18",
        "r18",
        "18+",
        "nsfl",
        "explicit",
    ];

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

    public static int SubCategoryId(JsonElement record)
    {
        if (!record.TryGetProperty("_aSubCategory", out var cat)
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

    public static (string Name, string Url, int Id, string? AvatarUrl) ReadSubmitter(JsonElement record)
    {
        if (!record.TryGetProperty("_aSubmitter", out var submitter))
        {
            return ("", "", 0, null);
        }

        var name = submitter.TryGetProperty("_sName", out var nameEl)
            ? nameEl.GetString() ?? ""
            : "";
        var id = 0;
        if (submitter.TryGetProperty("_idRow", out var idEl) && idEl.TryGetInt32(out var parsed) && parsed > 0)
        {
            id = parsed;
        }

        var url = "";
        if (submitter.TryGetProperty("_sProfileUrl", out var urlEl)
            && urlEl.GetString() is { Length: > 0 } profile)
        {
            url = profile;
        }
        else if (id > 0)
        {
            url = "https://gamebanana.com/members/" + id;
        }

        var avatar = submitter.TryGetProperty("_sAvatarUrl", out var avatarEl)
            ? avatarEl.GetString()
            : null;
        return (name, url, id, string.IsNullOrWhiteSpace(avatar) ? null : avatar);
    }

    public static int SubmitterId(JsonElement record)
    {
        return ReadSubmitter(record).Id;
    }

    public static string SubmitterFilter(int authorId)
    {
        return authorId > 0 ? "&_aFilters%5BGeneric_Submitter%5D=" + authorId : "";
    }

    public static CatalogItemDto AttachSocial(CatalogItemDto item, JsonElement record)
    {
        var (name, url, id, avatar) = ReadSubmitter(record);
        return item with
        {
            Author = name.Length > 0 ? name : item.Author,
            AuthorUrl = url.Length > 0 ? url : item.AuthorUrl,
            AuthorId = id > 0 ? id : item.AuthorId,
            AuthorAvatarUrl = avatar ?? item.AuthorAvatarUrl,
            LikeCount = ReadInt(record, "_nLikeCount"),
            DownloadCount = ReadInt(record, "_nDownloadCount"),
        };
    }

    public static CatalogItemDto FromRecord(JsonElement record, string defaultCategory)
    {
        var id = record.TryGetProperty("_idRow", out var idEl) ? idEl.GetInt32() : 0;
        var name = record.TryGetProperty("_sName", out var nameEl) ? nameEl.GetString() ?? "" : "";
        var profile = record.TryGetProperty("_sProfileUrl", out var urlEl) ? urlEl.GetString() ?? "" : "";
        var (author, authorUrl, _, _) = ReadSubmitter(record);

        var category = ReadCategoryName(record) ?? defaultCategory;
        string? skinTarget = null;
        string? description = null;
        if (string.Equals(defaultCategory, GameBananaIds.SkinsCategoryName, StringComparison.Ordinal))
        {
            skinTarget = SkinTarget.FromRecord(record, name, category);
            description = SkinTarget.ReadDescription(record, name);
        }

        return AttachSocial(
            new CatalogItemDto(
                id,
                name,
                author,
                ThumbnailUrl(record),
                category,
                profile,
                skinTarget,
                description,
                IsNsfw(record),
                authorUrl),
            record);
    }

    /// <summary>
    /// GameBanana <c>_bIsNsfw</c> is often unset. Also treat obvious title / description / tags.
    /// </summary>
    public static bool IsNsfw(JsonElement record)
    {
        if (FlagTrue(record, "_bIsNsfw"))
        {
            return true;
        }

        if (LooksNsfw(ReadString(record, "_sName"), ReadString(record, "_sDescription")))
        {
            return true;
        }

        if (record.TryGetProperty("_aTags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tags.EnumerateArray())
            {
                var label = tag.ValueKind == JsonValueKind.String
                    ? tag.GetString()
                    : ReadNestedName(tag);
                if (LooksNsfw(label, null))
                {
                    return true;
                }
            }
        }

        if (record.TryGetProperty("_aPreviewMedia", out var media)
            && media.TryGetProperty("_aImages", out var images)
            && images.ValueKind == JsonValueKind.Array)
        {
            foreach (var image in images.EnumerateArray())
            {
                if (FlagTrue(image, "_bIsNsfw"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool LooksNsfw(string? name, string? extra)
    {
        return HasNsfwHint(name) || HasNsfwHint(extra);
    }

    private static bool HasNsfwHint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var hint in NsfwHints)
        {
            if (text.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FlagTrue(JsonElement record, string name)
    {
        if (!record.TryGetProperty(name, out var flag))
        {
            return false;
        }

        if (flag.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (flag.ValueKind == JsonValueKind.Number && flag.TryGetInt32(out var n))
        {
            return n != 0;
        }

        if (flag.ValueKind == JsonValueKind.String)
        {
            var raw = flag.GetString();
            return raw is "1" or "true" or "True";
        }

        return false;
    }

    private static string? ReadNestedName(JsonElement tag)
    {
        return tag.ValueKind == JsonValueKind.Object
            && tag.TryGetProperty("_sName", out var name)
            ? name.GetString()
            : null;
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

    private static string? ReadString(JsonElement record, string name)
    {
        return record.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
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
