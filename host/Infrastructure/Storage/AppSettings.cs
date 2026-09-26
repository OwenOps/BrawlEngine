using System.Text.Json;
using System.Text.Json.Serialization;
using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.Storage;

public sealed class AppSettings
{
    [JsonPropertyName("gamePath")]
    public string? GamePath { get; set; }

    [JsonPropertyName("mp3Path")]
    public string? Mp3Path { get; set; }

    [JsonPropertyName("javaPath")]
    public string? JavaPath { get; set; }

    [JsonPropertyName("ffdecLibPath")]
    public string? FfdecLibPath { get; set; }

    [JsonPropertyName("downloadsPath")]
    public string? DownloadsPath { get; set; }

    [JsonPropertyName("statsRecents")]
    public List<StatsRecentDto> StatsRecents { get; set; } = [];

    [JsonPropertyName("statsPins")]
    public List<StatsRecentDto> StatsPins { get; set; } = [];

    [JsonPropertyName("statsMine")]
    public StatsRecentDto? StatsMine { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(
            AppPaths.SettingsFile,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
