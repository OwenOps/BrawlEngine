using System.Text.Json;
using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.Storage;

public static class LikeStore
{
    private const int MaxIds = 80;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static LikesDto Load()
    {
        try
        {
            if (!File.Exists(AppPaths.LikesFile))
            {
                return LikesDto.Empty;
            }

            var loaded = JsonSerializer.Deserialize<LikesDto>(File.ReadAllText(AppPaths.LikesFile), JsonOptions);
            return Normalize(loaded);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return LikesDto.Empty;
        }
    }

    public static (LikesDto Likes, bool Liked) Toggle(string kind, int id)
    {
        if (id <= 0 || kind is not ("maps" or "sounds" or "skins"))
        {
            return (Load(), false);
        }

        var current = Load();
        var maps = current.Maps.ToList();
        var sounds = current.Sounds.ToList();
        var skins = current.Skins.ToList();
        var list = kind switch
        {
            "sounds" => sounds,
            "skins" => skins,
            _ => maps,
        };
        var liked = !list.Contains(id);
        if (liked)
        {
            list.Add(id);
            if (list.Count > MaxIds)
            {
                list.RemoveRange(0, list.Count - MaxIds);
            }
        }
        else
        {
            list.Remove(id);
        }

        var next = new LikesDto(maps, sounds, skins);
        Save(next);
        return (next, liked);
    }

    private static void Save(LikesDto likes)
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.LikesFile, JsonSerializer.Serialize(Normalize(likes), JsonOptions));
    }

    private static LikesDto Normalize(LikesDto? likes)
    {
        return new LikesDto(
            Clean(likes?.Maps),
            Clean(likes?.Sounds),
            Clean(likes?.Skins));
    }

    private static IReadOnlyList<int> Clean(IReadOnlyList<int>? ids)
    {
        if (ids is null || ids.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<int>();
        var list = new List<int>();
        foreach (var id in ids)
        {
            if (id > 0 && seen.Add(id))
            {
                list.Add(id);
            }
        }

        if (list.Count > MaxIds)
        {
            list.RemoveRange(0, list.Count - MaxIds);
        }

        return list;
    }
}
