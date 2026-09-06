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
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        query = query?.Trim() ?? "";
        if (query.Length >= 2)
        {
            return await CatalogSearch.SearchAsync(
                    page,
                    query,
                    sort,
                    record => CatalogSearch.IsSkinsMod(record) && CatalogSearch.NameContains(record, query),
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
            + GameBananaIds.SkinsCategoryId
            + "&_sSort="
            + CatalogSearch.SortAlias(sort);

        using var response = await AppHttp.Shared.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
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

        var category = GameBananaIds.SkinsCategoryName;
        if (record.TryGetProperty("_aSubCategory", out var sub)
            && sub.TryGetProperty("_sName", out var subName)
            && subName.GetString() is { Length: > 0 } legend)
        {
            category = legend;
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
