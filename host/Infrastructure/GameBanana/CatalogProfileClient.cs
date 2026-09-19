using System.Text.Json;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Networking;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.GameBanana;

/// <summary>
/// Loads catalog cards by GameBanana id (On disk / Applied / Liked).
/// Returns disk snapshots immediately. GameBanana (thumbs, author, never-saved ids)
/// runs in the background so the library list is not waiting on the network.
/// </summary>
public static class CatalogProfileClient
{
    public const int MaxIds = 80;
    private const int MaxParallel = 4;
    private static readonly TimeSpan PerIdTimeout = TimeSpan.FromSeconds(12);

    public static Task<IReadOnlyList<CatalogItemDto>> GetAsync(
        string kind,
        string itemType,
        IReadOnlyList<int> ids,
        string defaultCategory)
    {
        var unique = ids
            .Where(id => id > 0)
            .Distinct()
            .Take(MaxIds)
            .ToList();
        var items = new CatalogItemDto[unique.Count];
        var missing = new List<int>();
        var needThumbs = new List<int>();
        var needAuthor = new List<int>();
        for (var i = 0; i < unique.Count; i++)
        {
            var local = CatalogLibrary.TryRead(kind, unique[i]);
            if (local is not null)
            {
                items[i] = local;
                if (!CatalogLibrary.HasLocalThumb(kind, unique[i]))
                {
                    needThumbs.Add(unique[i]);
                }

                if (CatalogLibrary.NeedsAuthorRefresh(local))
                {
                    needAuthor.Add(unique[i]);
                }
            }
            else
            {
                items[i] = CatalogSearch.Stub(unique[i], itemType, defaultCategory);
                missing.Add(unique[i]);
            }
        }

        if (needThumbs.Count > 0 || needAuthor.Count > 0 || missing.Count > 0)
        {
            RefreshInBackground(kind, itemType, defaultCategory, needThumbs, needAuthor, missing);
        }

        return Task.FromResult<IReadOnlyList<CatalogItemDto>>(items);
    }

    /// <summary>
    /// Fills thumbs / author / missing cards after the list is already on screen.
    /// Next On disk open reads the updated item.json.
    /// </summary>
    private static void RefreshInBackground(
        string kind,
        string itemType,
        string defaultCategory,
        IReadOnlyList<int> needThumbs,
        IReadOnlyList<int> needAuthor,
        IReadOnlyList<int> missing)
    {
        _ = Task.Run(async () =>
        {
            using var gate = new SemaphoreSlim(MaxParallel);
            var jobs = new List<Task>();
            var fetchIds = needAuthor.Concat(missing).Distinct().ToHashSet();
            foreach (var id in needThumbs)
            {
                if (fetchIds.Contains(id))
                {
                    continue;
                }

                jobs.Add(RunLimited(gate, () => CatalogLibrary.EnsureLocalThumbAsync(kind, id, CancellationToken.None)));
            }

            foreach (var id in fetchIds)
            {
                jobs.Add(RunLimited(gate, () => RefreshCardAsync(kind, itemType, id, defaultCategory)));
            }

            try
            {
                await Task.WhenAll(jobs).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException)
            {
            }
        });
    }

    private static async Task RunLimited(SemaphoreSlim gate, Func<Task> work)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task RefreshCardAsync(
        string kind,
        string itemType,
        int id,
        string defaultCategory)
    {
        using var linked = new CancellationTokenSource(PerIdTimeout);
        var item = await FetchOneAsync(itemType, id, defaultCategory, linked.Token).ConfigureAwait(false);
        if (item.Name.StartsWith("Item ", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await CatalogLibrary.SaveFetchedAsync(kind, item, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
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
