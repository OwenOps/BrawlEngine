using System.Text.Json;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Networking;

namespace BrawlEngine.Host.Infrastructure.GameBanana;

/// <summary>
/// Loads catalog cards by GameBanana id (local library: on disk / applied).
/// Failures become a stub so Apply/Delete still work.
/// </summary>
public static class CatalogProfileClient
{
    public const int MaxIds = 80;
    private const int MaxParallel = 4;
    private static readonly TimeSpan PerIdTimeout = TimeSpan.FromSeconds(12);

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
        var items = new CatalogItemDto[unique.Count];
        using var gate = new SemaphoreSlim(MaxParallel);
        var tasks = unique.Select(async (id, index) =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linked.CancelAfter(PerIdTimeout);
                items[index] = await FetchOneAsync(itemType, id, defaultCategory, linked.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
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
            using var doc = await GameBananaJson.ReadDocumentAsync(response, cancellationToken).ConfigureAwait(false);
            var item = CatalogSearch.FromRecord(doc.RootElement, defaultCategory);
            return item.Id > 0 ? item : CatalogSearch.Stub(id, itemType, defaultCategory);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or OperationCanceledException)
        {
            return CatalogSearch.Stub(id, itemType, defaultCategory);
        }
    }
}
