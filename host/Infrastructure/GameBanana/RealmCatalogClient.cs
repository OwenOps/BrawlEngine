using System.Text.Json;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Networking;

namespace BrawlEngine.Host.Infrastructure.GameBanana;

public static class RealmCatalogClient
{
    public const int PageSize = CatalogSearch.PageSize;

    public static async Task<CatalogPageDto> ListAsync(
        int page,
        string? query,
        string? sort,
        int authorId = 0,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (authorId < 0)
        {
            authorId = 0;
        }

        query = query?.Trim() ?? "";
        var cacheKey = "maps|" + page + "|" + query + "|" + CatalogSearch.SortAlias(sort) + "|" + authorId;
        return await CatalogCache
            .GetOrFetchAsync(cacheKey, ct => FetchAsync(page, query, sort, authorId, ct), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<CatalogPageDto> FetchAsync(
        int page,
        string query,
        string? sort,
        int authorId,
        CancellationToken cancellationToken)
    {
        if (query.Length >= 2)
        {
            return await CatalogSearch.SearchAsync(
                    page,
                    query,
                    sort,
                    record => CatalogSearch.IsRealmsMod(record)
                        && CatalogSearch.NameContains(record, query)
                        && (authorId == 0 || CatalogSearch.SubmitterId(record) == authorId),
                    ToItem,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var url =
            "https://gamebanana.com/apiv11/Mod/Index?_nPage="
            + page
            + "&_nPerpage="
            + PageSize
            + "&_aFilters%5BGeneric_Category%5D="
            + GameBananaIds.RealmsCategoryId
            + CatalogSearch.SubmitterFilter(authorId)
            + "&_sSort="
            + CatalogSearch.SortAlias(sort)
            + CatalogSearch.IndexCountFields;

        using var response = await AppHttp.Shared.GetAsync(url, cancellationToken).ConfigureAwait(false);
        using var doc = await GameBananaJson.ReadDocumentAsync(response, cancellationToken).ConfigureAwait(false);
        return CatalogSearch.ParseIndex(doc.RootElement, page, ToItem);
    }

    private static CatalogItemDto ToItem(JsonElement record)
    {
        var id = record.TryGetProperty("_idRow", out var idEl) ? idEl.GetInt32() : 0;
        var name = record.TryGetProperty("_sName", out var nameEl) ? nameEl.GetString() ?? "" : "";
        var profile = record.TryGetProperty("_sProfileUrl", out var urlEl) ? urlEl.GetString() ?? "" : "";
        var (author, authorUrl, _, _) = CatalogSearch.ReadSubmitter(record);

        var category = GameBananaIds.RealmsCategoryName;
        if (record.TryGetProperty("_aRootCategory", out var cat)
            && cat.TryGetProperty("_sName", out var catName)
            && catName.GetString() is { Length: > 0 } n)
        {
            category = n;
        }

        return CatalogSearch.AttachSocial(
            new CatalogItemDto(
                id,
                name,
                author,
                ThumbnailUrl(record),
                category,
                profile,
                Nsfw: CatalogSearch.IsNsfw(record),
                AuthorUrl: authorUrl),
            record);
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
