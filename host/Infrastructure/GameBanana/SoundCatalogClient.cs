using System.Text.Json;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Networking;

namespace BrawlEngine.Host.Infrastructure.GameBanana;

public static class SoundCatalogClient
{
    public const int PageSize = 15;

    private const string IndexUrl =
        "https://gamebanana.com/apiv11/Sound/Index?_nPage={0}&_nPerpage=15&_aFilters%5BGeneric_Game%5D=5704";

    public static async Task<CatalogPageDto> ListAsync(int page, CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        using var response = await AppHttp.Shared.GetAsync(string.Format(IndexUrl, page), cancellationToken)
            .ConfigureAwait(false);
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
        }
        else
        {
            complete = true;
        }

        return new CatalogPageDto(items, page + 1, complete);
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
