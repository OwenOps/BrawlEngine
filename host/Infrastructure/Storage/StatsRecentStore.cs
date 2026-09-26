using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.Storage;

public static class StatsRecentStore
{
    public const int Max = 8;
    public const int MaxPins = 12;
    private static readonly object Gate = new();

    public static IReadOnlyList<StatsRecentDto> List()
    {
        lock (Gate)
        {
            return Normalize(AppSettings.Load().StatsRecents);
        }
    }

    public static void Remember(int id, string name)
    {
        if (id <= 0)
        {
            return;
        }

        name = name.Trim();
        if (name.Length == 0)
        {
            return;
        }

        lock (Gate)
        {
            var settings = AppSettings.Load();
            var next = new List<StatsRecentDto> { new() { Id = id, Name = name } };
            foreach (var row in Normalize(settings.StatsRecents))
            {
                if (row.Id == id)
                {
                    continue;
                }

                next.Add(row);
                if (next.Count >= Max)
                {
                    break;
                }
            }

            settings.StatsRecents = next;
            settings.Save();
        }
    }

    public static IReadOnlyList<StatsRecentDto> Pins()
    {
        lock (Gate)
        {
            return Normalize(AppSettings.Load().StatsPins, MaxPins);
        }
    }

    public static IReadOnlyList<StatsRecentDto> TogglePin(int id, string name)
    {
        if (id <= 0)
        {
            return Pins();
        }

        name = name.Trim();
        if (name.Length == 0)
        {
            return Pins();
        }

        lock (Gate)
        {
            var settings = AppSettings.Load();
            var next = Normalize(settings.StatsPins, MaxPins).ToList();
            var at = next.FindIndex(row => row.Id == id);
            if (at >= 0)
            {
                next.RemoveAt(at);
            }
            else
            {
                next.Insert(0, new StatsRecentDto { Id = id, Name = name });
                if (next.Count > MaxPins)
                {
                    next.RemoveRange(MaxPins, next.Count - MaxPins);
                }
            }

            settings.StatsPins = next;
            settings.Save();
            return next;
        }
    }

    public static StatsRecentDto? Mine()
    {
        lock (Gate)
        {
            var row = AppSettings.Load().StatsMine;
            var name = row?.Name?.Trim() ?? "";
            return row is null || row.Id <= 0 || name.Length == 0
                ? null
                : new StatsRecentDto { Id = row.Id, Name = name };
        }
    }

    public static StatsRecentDto? SetMine(int id, string name)
    {
        lock (Gate)
        {
            var settings = AppSettings.Load();
            if (id <= 0)
            {
                settings.StatsMine = null;
                settings.Save();
                return null;
            }

            name = name.Trim();
            if (name.Length == 0)
            {
                return Mine();
            }

            var mine = new StatsRecentDto { Id = id, Name = name };
            settings.StatsMine = mine;
            settings.Save();
            return mine;
        }
    }

    public static object Snapshot()
    {
        return new
        {
            recents = List(),
            pins = Pins(),
            mine = Mine(),
        };
    }

    public static IReadOnlyList<PlayerSearchMatchDto> Matches(string query)
    {
        query = query.Trim();
        if (query.Length < 2)
        {
            return [];
        }

        return List()
            .Where(row => row.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(row => new PlayerSearchMatchDto(
                row.Id,
                row.Name,
                "",
                null,
                null,
                null,
                null,
                null,
                null,
                null))
            .ToList();
    }

    private static List<StatsRecentDto> Normalize(List<StatsRecentDto>? rows, int max)
    {
        var list = new List<StatsRecentDto>();
        var seen = new HashSet<int>();
        foreach (var row in rows ?? [])
        {
            var name = row.Name?.Trim() ?? "";
            if (row.Id <= 0 || name.Length == 0 || !seen.Add(row.Id))
            {
                continue;
            }

            list.Add(new StatsRecentDto { Id = row.Id, Name = name });
            if (list.Count >= max)
            {
                break;
            }
        }

        return list;
    }

    private static List<StatsRecentDto> Normalize(List<StatsRecentDto>? rows)
    {
        return Normalize(rows, Max);
    }
}
