using System.Text.Json;
using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.Storage;

public static class LoadoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static LoadoutDto Load()
    {
        try
        {
            if (!File.Exists(AppPaths.LoadoutFile))
            {
                return LoadoutDto.Empty;
            }

            var json = File.ReadAllText(AppPaths.LoadoutFile);
            var loaded = JsonSerializer.Deserialize<LoadoutDto>(json, JsonOptions);
            return Normalize(loaded);
        }
        catch (IOException)
        {
            return LoadoutDto.Empty;
        }
        catch (JsonException)
        {
            return LoadoutDto.Empty;
        }
    }

    public static void Save(LoadoutDto loadout)
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(
            AppPaths.LoadoutFile,
            JsonSerializer.Serialize(Normalize(loadout), JsonOptions));
    }

    public static LoadoutDto RecordMapApplied(int modId)
    {
        var current = Load();
        var maps = current.Maps.Where(entry => entry.ModId != modId).ToList();
        maps.Add(new LoadoutModDto(modId));
        var next = current with { Maps = maps };
        Save(next);
        return next;
    }

    public static LoadoutDto RecordMusicApplied(int soundId)
    {
        var current = Load();
        var music = current.Music.Where(entry => entry.ModId != soundId).ToList();
        music.Add(new LoadoutModDto(soundId));
        var next = current with { Music = music };
        Save(next);
        return next;
    }

    public static void RemoveMap(int modId)
    {
        var current = Load();
        Save(current with { Maps = current.Maps.Where(entry => entry.ModId != modId).ToList() });
    }

    public static void ClearMaps()
    {
        var current = Load();
        Save(current with { Maps = [] });
    }

    public static void Clear()
    {
        Save(LoadoutDto.Empty);
    }

    private static LoadoutDto Normalize(LoadoutDto? loadout)
    {
        if (loadout is null)
        {
            return LoadoutDto.Empty;
        }

        return loadout with
        {
            Maps = loadout.Maps ?? [],
            Music = loadout.Music ?? [],
        };
    }
}
