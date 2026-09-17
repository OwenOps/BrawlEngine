using System.Text.Json;
using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.Storage;

public static class AudioChangeStore
{
    private const int MaxEntries = 80;
    private const int MaxSource = 180;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static AudioChangesDto Load()
    {
        try
        {
            if (!File.Exists(AppPaths.AudioChangesFile))
            {
                return AudioChangesDto.Empty;
            }

            var loaded = JsonSerializer.Deserialize<AudioChangesDto>(
                File.ReadAllText(AppPaths.AudioChangesFile),
                JsonOptions);
            return Normalize(loaded);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return AudioChangesDto.Empty;
        }
    }

    public static void Record(string fileName, string label, string? source)
    {
        var key = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var list = Load().Entries
            .Where(entry => !entry.FileName.Equals(key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        list.Add(new AudioChangeEntryDto(
            key,
            string.IsNullOrWhiteSpace(label) ? key : label.Trim(),
            CleanSource(source),
            DateTime.UtcNow.ToString("o")));
        if (list.Count > MaxEntries)
        {
            list.RemoveRange(0, list.Count - MaxEntries);
        }

        Save(new AudioChangesDto(list));
    }

    public static void Remove(string fileName)
    {
        var key = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var current = Load();
        var next = current.Entries
            .Where(entry => !entry.FileName.Equals(key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (next.Count == current.Entries.Count)
        {
            return;
        }

        Save(new AudioChangesDto(next));
    }

    public static void KeepOnly(IEnumerable<string> fileNames)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in fileNames)
        {
            var key = Path.GetFileName(name);
            if (!string.IsNullOrWhiteSpace(key))
            {
                keep.Add(key);
            }
        }

        var current = Load();
        var next = current.Entries.Where(entry => keep.Contains(entry.FileName)).ToList();
        if (next.Count == current.Entries.Count)
        {
            return;
        }

        Save(new AudioChangesDto(next));
    }

    public static void Clear()
    {
        if (!File.Exists(AppPaths.AudioChangesFile))
        {
            return;
        }

        Save(AudioChangesDto.Empty);
    }

    private static void Save(AudioChangesDto changes)
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.AudioChangesFile, JsonSerializer.Serialize(Normalize(changes), JsonOptions));
    }

    private static AudioChangesDto Normalize(AudioChangesDto? changes)
    {
        if (changes?.Entries is null || changes.Entries.Count == 0)
        {
            return AudioChangesDto.Empty;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<AudioChangeEntryDto>();
        foreach (var entry in changes.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FileName) || !seen.Add(entry.FileName))
            {
                continue;
            }

            list.Add(entry with
            {
                Label = string.IsNullOrWhiteSpace(entry.Label) ? entry.FileName : entry.Label.Trim(),
                Source = CleanSource(entry.Source),
                At = string.IsNullOrWhiteSpace(entry.At) ? DateTime.UtcNow.ToString("o") : entry.At,
            });
        }

        if (list.Count > MaxEntries)
        {
            list.RemoveRange(0, list.Count - MaxEntries);
        }

        return new AudioChangesDto(list);
    }

    private static string? CleanSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var trimmed = source.Trim();
        return trimmed.Length > MaxSource ? trimmed[..MaxSource] : trimmed;
    }
}
