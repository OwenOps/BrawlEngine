using System.Text.Json;
using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.Storage;

public static class NamedConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<NamedConfigDto> List()
    {
        return LoadFile().Configs;
    }

    public static NamedConfigDto SaveCurrent(string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("Name cannot be empty.");
        }

        var current = LoadoutStore.Load();
        var entry = new NamedConfigDto(
            Guid.NewGuid().ToString("N"),
            trimmed,
            current.Maps,
            current.Music);

        var file = LoadFile();
        var configs = file.Configs.ToList();
        configs.Add(entry);
        SaveFile(file with { Configs = configs });
        return entry;
    }

    public static void ApplyToCurrent(string id)
    {
        var match = List().FirstOrDefault(entry => entry.Id == id);
        if (match is null)
        {
            throw new InvalidOperationException("Config not found.");
        }

        LoadoutStore.Save(new LoadoutDto(match.Maps, match.Music));
    }

    public static void Delete(string id)
    {
        var file = LoadFile();
        var configs = file.Configs.Where(entry => entry.Id != id).ToList();
        if (configs.Count == file.Configs.Count)
        {
            throw new InvalidOperationException("Config not found.");
        }

        SaveFile(file with { Configs = configs });
    }

    private static NamedConfigListDto LoadFile()
    {
        try
        {
            if (!File.Exists(AppPaths.NamedConfigsFile))
            {
                return NamedConfigListDto.Empty;
            }

            var json = File.ReadAllText(AppPaths.NamedConfigsFile);
            var loaded = JsonSerializer.Deserialize<NamedConfigListDto>(json, JsonOptions);
            if (loaded?.Configs is null)
            {
                return NamedConfigListDto.Empty;
            }

            return loaded;
        }
        catch (IOException)
        {
            return NamedConfigListDto.Empty;
        }
        catch (JsonException)
        {
            return NamedConfigListDto.Empty;
        }
    }

    private static void SaveFile(NamedConfigListDto file)
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(
            AppPaths.NamedConfigsFile,
            JsonSerializer.Serialize(file, JsonOptions));
    }
}
