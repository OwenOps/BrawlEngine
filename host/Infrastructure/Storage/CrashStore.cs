using System.Text.Json;
using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.Storage;

public static class CrashStore
{
    private const int MaxIds = 80;
    private const int MaxNote = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static CrashesDto Load()
    {
        try
        {
            if (!File.Exists(AppPaths.CrashesFile))
            {
                return CrashesDto.Empty;
            }

            var loaded = JsonSerializer.Deserialize<CrashesDto>(File.ReadAllText(AppPaths.CrashesFile), JsonOptions);
            return Normalize(loaded);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return CrashesDto.Empty;
        }
    }

    public static CrashesDto Toggle(string kind, int id, string? note)
    {
        if (id <= 0 || kind is not ("maps" or "sounds" or "skins"))
        {
            return Load();
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

        var index = list.FindIndex(tag => tag.Id == id);
        if (index >= 0)
        {
            list.RemoveAt(index);
        }
        else
        {
            list.Add(new CrashTagDto(id, CleanNote(note)));
            if (list.Count > MaxIds)
            {
                list.RemoveRange(0, list.Count - MaxIds);
            }
        }

        var next = new CrashesDto(maps, sounds, skins);
        Save(next);
        return next;
    }

    private static void Save(CrashesDto crashes)
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.CrashesFile, JsonSerializer.Serialize(Normalize(crashes), JsonOptions));
    }

    private static CrashesDto Normalize(CrashesDto? crashes)
    {
        return new CrashesDto(
            Clean(crashes?.Maps),
            Clean(crashes?.Sounds),
            Clean(crashes?.Skins));
    }

    private static IReadOnlyList<CrashTagDto> Clean(IReadOnlyList<CrashTagDto>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<int>();
        var list = new List<CrashTagDto>();
        foreach (var tag in tags)
        {
            if (tag.Id > 0 && seen.Add(tag.Id))
            {
                list.Add(tag with { Note = CleanNote(tag.Note) });
            }
        }

        if (list.Count > MaxIds)
        {
            list.RemoveRange(0, list.Count - MaxIds);
        }

        return list;
    }

    private static string? CleanNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();
        return trimmed.Length > MaxNote ? trimmed[..MaxNote] : trimmed;
    }
}
