using System.Text.Json;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Networking;

namespace BrawlEngine.Host.Infrastructure.GameBanana;

/// <summary>
/// Loads catalog cards by GameBanana id (local library: on disk / applied).
/// One ProfilePage per id. Failures become a stub so Apply/Delete still work.
/// </summary>
public static class CatalogProfileClient
{
    public const int MaxIds = 80;

    public static async Task<IReadOnlyList<CatalogItemDto>> GetAsync(
        string itemType,
        IReadOnlyList<int> ids,
        string defaultCategory,
        CancellationToken cancellationToken = default)
    {
        var unique = ids
            .Where(id => id > 0)
            .Distinct()
            .Take(MaxIds)
            .ToList();
        var items = new List<CatalogItemDto>(unique.Count);
        foreach (var id in unique)
        {
            items.Add(await FetchOneAsync(itemType, id, defaultCategory, cancellationToken).ConfigureAwait(false));
        }

        return items;
    }

    private static async Task<CatalogItemDto> FetchOneAsync(
        string itemType,
        int id,
        string defaultCategory,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = "https://gamebanana.com/apiv11/" + itemType + "/" + id + "/ProfilePage";
            using var response = await AppHttp.Shared.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var item = CatalogSearch.FromRecord(doc.RootElement, defaultCategory);
            return item.Id > 0 ? item : CatalogSearch.Stub(id, itemType, defaultCategory);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return CatalogSearch.Stub(id, itemType, defaultCategory);
        }
    }
}
