namespace BrawlEngine.Host.Infrastructure.Storage;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BrawlEngine");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string LoadoutFile => Path.Combine(Root, "config.json");

    public static string NamedConfigsFile => Path.Combine(Root, "configs.json");

    public static string DownloadsFolder(int modId) =>
        Path.Combine(Root, "downloads", modId.ToString());

    public static string SoundDownloadsFolder(int soundId) =>
        Path.Combine(Root, "downloads", "sounds", soundId.ToString());

    public static string SkinDownloadsFolder(int skinId) =>
        Path.Combine(Root, "downloads", "skins", skinId.ToString());

    public static string BackupsRoot => Path.Combine(Root, "backups");
}
