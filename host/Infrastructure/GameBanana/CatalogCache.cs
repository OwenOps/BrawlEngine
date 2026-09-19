using System.Collections.Concurrent;
using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.GameBanana;

/// <summary>
/// One GameBanana catalog fetch at a time per key. Keep a short memory cache so Retry is instant.
/// Do not cancel the HTTP here: GameBanana often needs longer than a short cap, and Retry can wait on the same in-flight task.
/// </summary>
public static class CatalogCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private static readonly ConcurrentDictionary<string, Entry> Pages = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Task<CatalogPageDto>> Inflight = new(StringComparer.Ordinal);

    public static async Task<CatalogPageDto> GetOrFetchAsync(
        string key,
        Func<CancellationToken, Task<CatalogPageDto>> fetch,
        CancellationToken cancellationToken)
    {
        if (Pages.TryGetValue(key, out var entry) && entry.ExpiresUtc > DateTime.UtcNow)
        {
            return entry.Page;
        }

        var task = Inflight.GetOrAdd(key, _ => FetchAsync(key, fetch));
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CatalogPageDto> FetchAsync(
        string key,
        Func<CancellationToken, Task<CatalogPageDto>> fetch)
    {
        try
        {
            var page = await fetch(CancellationToken.None).ConfigureAwait(false);
            Pages[key] = new Entry(page, DateTime.UtcNow.Add(Ttl));
            return page;
        }
        finally
        {
            Inflight.TryRemove(key, out _);
        }
    }

    private readonly record struct Entry(CatalogPageDto Page, DateTime ExpiresUtc);
}
