using System.Text.Json;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Networking;

namespace BrawlEngine.Host.Infrastructure.GameBanana;

public static class SkinCatalogClient
{
    public const int PageSize = CatalogSearch.PageSize;

    public static async Task<CatalogPageDto> ListAsync(
        int page,
        string? query,
        string? sort,
        int categoryId = 0,
        int authorId = 0,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (!GameBananaIds.IsSkinLegend(categoryId))
        {
            categoryId = 0;
        }

        if (authorId < 0)
        {
            authorId = 0;
        }

        query = query?.Trim() ?? "";
        var cacheKey = "skins|" + page + "|" + query + "|" + CatalogSearch.SortAlias(sort) + "|" + categoryId + "|" + authorId;
        return await CatalogCache
            .GetOrFetchAsync(cacheKey, ct => FetchAsync(page, query, sort, categoryId, authorId, ct), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<CatalogPageDto> FetchAsync(
        int page,
        string query,
        string? sort,
        int categoryId,
        int authorId,
        CancellationToken cancellationToken)
    {
        if (query.Length >= 2)
        {
            var found = await CatalogSearch.SearchAsync(
                    page,
                    query,
                    sort,
                    record => CatalogSearch.IsSkinsMod(record)
                        && CatalogSearch.NameContains(record, query)
                        && (categoryId == 0 || CatalogSearch.SubCategoryId(record) == categoryId)
                        && (authorId == 0 || CatalogSearch.SubmitterId(record) == authorId),
                    ToItem,
                    cancellationToken)
                .ConfigureAwait(false);
            return found;
        }

        var filterId = categoryId > 0 ? categoryId : GameBananaIds.SkinsCategoryId;
        var url =
            "https://gamebanana.com/apiv11/Mod/Index?_nPage="
            + page
            + "&_nPerpage="
            + PageSize
            + "&_aFilters%5BGeneric_Category%5D="
            + filterId
            + CatalogSearch.SubmitterFilter(authorId)
            + "&_sSort="
            + CatalogSearch.SortAlias(sort)
            + CatalogSearch.IndexCountFields;

        using var response = await AppHttp.Shared.GetAsync(url, cancellationToken).ConfigureAwait(false);
        using var doc = await GameBananaJson.ReadDocumentAsync(response, cancellationToken).ConfigureAwait(false);
        var listed = CatalogSearch.ParseIndex(doc.RootElement, page, ToItem);
        return listed;
    }

    private static CatalogItemDto ToItem(JsonElement record)
    {
        var id = record.TryGetProperty("_idRow", out var idEl) ? idEl.GetInt32() : 0;
        var name = record.TryGetProperty("_sName", out var nameEl) ? nameEl.GetString() ?? "" : "";
        var profile = record.TryGetProperty("_sProfileUrl", out var urlEl) ? urlEl.GetString() ?? "" : "";
        var (author, authorUrl, _, _) = CatalogSearch.ReadSubmitter(record);

        var category = GameBananaIds.SkinsCategoryName;
        if (record.TryGetProperty("_aSubCategory", out var sub)
            && sub.TryGetProperty("_sName", out var subName)
            && subName.GetString() is { Length: > 0 } legend)
        {
            category = legend;
        }

        var skinTarget = SkinTarget.FromRecord(record, name, category);
        var description = SkinTarget.ReadDescription(record, name);
        return CatalogSearch.AttachSocial(
            new CatalogItemDto(
                id,
                name,
                author,
                ThumbnailUrl(record),
                category,
                profile,
                skinTarget,
                description,
                CatalogSearch.IsNsfw(record),
                authorUrl),
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
