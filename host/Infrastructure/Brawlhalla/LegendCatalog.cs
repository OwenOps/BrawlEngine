using System.Text.Json;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Brawlhalla;

/// <summary>
/// Static legend names. Disk file so the first profile is the only slow fetch.
/// </summary>
public static class LegendCatalog
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly object Gate = new();
    private static IReadOnlyDictionary<int, string>? memory;
    private static Task<IReadOnlyDictionary<int, string>>? inflight;

    public static Task<IReadOnlyDictionary<int, string>> GetAsync(CancellationToken cancellationToken)
    {
        Task<IReadOnlyDictionary<int, string>> task;
        lock (Gate)
        {
            if (memory is not null)
            {
                return Task.FromResult(memory);
            }

            inflight ??= LoadAsync(CancellationToken.None);
            task = inflight;
        }

        return task.WaitAsync(cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<int, string>> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var fromDisk = ReadDisk();
            if (fromDisk is not null)
            {
                SetMemory(fromDisk);
                return fromDisk;
            }

            try
            {
                var fetched = await FetchAsync(cancellationToken).ConfigureAwait(false);
                WriteDisk(fetched);
                SetMemory(fetched);
                return fetched;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                var stale = ReadDisk(allowStale: true);
                if (stale is not null)
                {
                    SetMemory(stale);
                    return stale;
                }

                throw;
            }
        }
        finally
        {
            lock (Gate)
            {
                inflight = null;
            }
        }
    }

    private static async Task<IReadOnlyDictionary<int, string>> FetchAsync(CancellationToken cancellationToken)
    {
        var names = new Dictionary<int, string>();
        var totalPages = 1;
        for (var page = 1; page <= totalPages && page <= 4; page++)
        {
            var url =
                "https://api.brawlhalla.com/v1/static/legends?max_results=100&page="
                + page;
            using var doc = await BrawlhallaJson.GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            if (doc is null)
            {
                break;
            }

            var root = doc.RootElement;
            totalPages = BrawlhallaJson.ReadInt(root, "total_pages") ?? 1;
            if (!root.TryGetProperty("legends", out var legends) || legends.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            foreach (var legend in legends.EnumerateArray())
            {
                var id = BrawlhallaJson.ReadInt(legend, "legend_id") ?? 0;
                var name = BrawlhallaJson.ReadStringOrNull(legend, "bio_name")
                    ?? BrawlhallaJson.ReadStringOrNull(legend, "legend_name");
                if (id > 0 && name is not null)
                {
                    names[id] = name;
                }
            }
        }

        return names;
    }

    private static IReadOnlyDictionary<int, string>? ReadDisk(bool allowStale = false)
    {
        try
        {
            var path = AppPaths.BrawlhallaLegendsFile;
            if (!File.Exists(path))
            {
                return null;
            }

            var loaded = JsonSerializer.Deserialize<LegendFile>(File.ReadAllText(path), JsonOptions);
            if (loaded?.Names is null || (!allowStale && loaded.FetchedUtc.Add(Ttl) < DateTime.UtcNow))
            {
                return null;
            }

            var map = new Dictionary<int, string>();
            foreach (var (key, name) in loaded.Names)
            {
                if (int.TryParse(key, out var id) && id > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    map[id] = name.Trim();
                }
            }

            return map.Count == 0 ? null : map;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteDisk(IReadOnlyDictionary<int, string> names)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var file = new LegendFile(
                DateTime.UtcNow,
                names.ToDictionary(row => row.Key.ToString(), row => row.Value));
            File.WriteAllText(AppPaths.BrawlhallaLegendsFile, JsonSerializer.Serialize(file, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void SetMemory(IReadOnlyDictionary<int, string> names)
    {
        lock (Gate)
        {
            memory = names;
        }
    }

    private sealed record LegendFile(DateTime FetchedUtc, Dictionary<string, string>? Names);
}
