using System.Text.Json;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Networking;

namespace BrawlEngine.Host.Infrastructure.GameBanana;

public static class SoundCatalogClient
{
    public const int PageSize = CatalogSearch.PageSize;

    public static async Task<CatalogPageDto> ListAsync(
        int page,
        string? query,
        string? sort,
        int categoryId = 0,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (!GameBananaIds.IsSoundCategory(categoryId))
        {
            categoryId = 0;
        }

        query = query?.Trim() ?? "";
        var cacheKey = "sounds|" + page + "|" + query + "|" + CatalogSearch.SortAlias(sort) + "|" + categoryId;
        return await CatalogCache
            .GetOrFetchAsync(cacheKey, ct => FetchAsync(page, query, sort, categoryId, ct), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<CatalogPageDto> FetchAsync(
        int page,
        string query,
        string? sort,
        int categoryId,
        CancellationToken cancellationToken)
    {
        if (query.Length >= 2)
        {
            return await CatalogSearch.SearchAsync(
                    page,
                    query,
                    sort,
                    record => CatalogSearch.IsModel(record, GameBananaIds.SoundItemType)
                        && CatalogSearch.NameContains(record, query)
                        && (categoryId == 0 || CatalogSearch.RootCategoryId(record) == categoryId),
                    ToItem,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var url =
            "https://gamebanana.com/apiv11/Sound/Index?_nPage="
            + page
            + "&_nPerpage="
            + PageSize
            + "&_sSort="
            + CatalogSearch.SortAlias(sort);
        if (categoryId > 0)
        {
            url += "&_aFilters%5BGeneric_Category%5D=" + categoryId;
        }
        else
        {
            url += "&_aFilters%5BGeneric_Game%5D=" + GameBananaIds.BrawlhallaGameId;
        }

        using var response = await AppHttp.Shared.GetAsync(url, cancellationToken).ConfigureAwait(false);
        using var doc = await GameBananaJson.ReadDocumentAsync(response, cancellationToken).ConfigureAwait(false);
        return CatalogSearch.ParseIndex(doc.RootElement, page, ToItem);
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

        var category = "Sounds";
        if (record.TryGetProperty("_aRootCategory", out var cat)
            && cat.TryGetProperty("_sName", out var catName)
            && catName.GetString() is { Length: > 0 } n)
        {
            category = n;
        }

        return new CatalogItemDto(id, name, author, null, category, profile);
    }
}
