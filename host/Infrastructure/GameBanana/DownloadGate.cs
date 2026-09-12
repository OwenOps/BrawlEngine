using System.Collections.Concurrent;

namespace BrawlEngine.Host.Infrastructure.GameBanana;

/// <summary>At most two GameBanana downloads at once. Cancel is per item (queued or in flight).</summary>
public static class DownloadGate
{
    public const int MaxParallel = 2;

    private static readonly SemaphoreSlim Slots = new(MaxParallel, MaxParallel);
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> Running = new();

    public static bool TryCancel(string kind, int id)
    {
        if (!Running.TryGetValue(Key(kind, id), out var cts))
        {
            return false;
        }

        cts.Cancel();
        return true;
    }

    public static async Task<T> RunAsync<T>(string kind, int id, Func<CancellationToken, Task<T>> work)
    {
        var key = Key(kind, id);
        var cts = new CancellationTokenSource();
        if (!Running.TryAdd(key, cts))
        {
            cts.Dispose();
            throw new InvalidOperationException("This item is already downloading.");
        }

        try
        {
            await Slots.WaitAsync(cts.Token).ConfigureAwait(false);
            try
            {
                return await work(cts.Token).ConfigureAwait(false);
            }
            finally
            {
                Slots.Release();
            }
        }
        finally
        {
            Running.TryRemove(key, out var removed);
            removed?.Dispose();
        }
    }

    private static string Key(string kind, int id)
    {
        return kind + ":" + id;
    }
}
