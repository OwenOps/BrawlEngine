namespace BrawlEngine.Host.Infrastructure.Storage;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BrawlEngine");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string DownloadsFolder(int modId) =>
        Path.Combine(Root, "downloads", modId.ToString());

    public static string BackupsRoot => Path.Combine(Root, "backups");
}
