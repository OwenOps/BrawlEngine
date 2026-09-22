namespace BrawlEngine.Host.Infrastructure.Storage;

public static class AppPaths
{
    private static readonly object LayoutLock = new();
    private static bool defaultRootMigrated;
    private static string? nestedMapsRoot;

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BrawlEngine");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string LoadoutFile => Path.Combine(Root, "config.json");

    public static string NamedConfigsFile => Path.Combine(Root, "configs.json");

    public static string LikesFile => Path.Combine(Root, "likes.json");

    public static string CrashesFile => Path.Combine(Root, "crashes.json");

    public static string AudioChangesFile => Path.Combine(Root, "audio-changes.json");

    public static string DefaultDownloadsRoot => Path.Combine(Root, "mods");

    public static string LegacyDefaultDownloadsRoot => Path.Combine(Root, "downloads");

    public static string DownloadsRoot
    {
        get
        {
            EnsureLayout();
            return ResolvedDownloadsRoot();
        }
    }

    public static string MapDownloadsRoot => Path.Combine(DownloadsRoot, "Maps");

    public static string SoundDownloadsRoot => Path.Combine(DownloadsRoot, "sounds");

    public static string SkinDownloadsRoot => Path.Combine(DownloadsRoot, "skins");

    public static string DownloadsFolder(int modId) =>
        Path.Combine(MapDownloadsRoot, modId.ToString());

    public static string SoundDownloadsFolder(int soundId) =>
        Path.Combine(SoundDownloadsRoot, soundId.ToString());

    public static string SkinDownloadsFolder(int skinId) =>
        Path.Combine(SkinDownloadsRoot, skinId.ToString());

    public static string BackupsRoot => Path.Combine(Root, "backups");

    public static string LibraryRoot => Path.Combine(Root, "library");

    public static string LibraryFolder(string kind, int id) =>
        Path.Combine(LibraryRoot, kind, id.ToString());

    /// <summary>After the user picks a new mods folder, nest Maps/ again.</summary>
    public static void RefreshDownloadLayout()
    {
        lock (LayoutLock)
        {
            nestedMapsRoot = null;
        }

        EnsureLayout();
    }

    /// <summary>
    /// Custom picks store archives in Mods\ inside the chosen folder, unless that folder is already named Mods.
    /// </summary>
    public static string WithModsFolder(string picked)
    {
        var full = Path.GetFullPath(picked.Trim());
        return IsModsDirectoryName(full) ? full : Path.Combine(full, "Mods");
    }

    public static bool IsModsDirectoryName(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.Equals(name, "Mods", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvedDownloadsRoot()
    {
        var saved = AppSettings.Load().DownloadsPath?.Trim();
        return string.IsNullOrWhiteSpace(saved) ? DefaultDownloadsRoot : saved;
    }

    private static void EnsureLayout()
    {
        lock (LayoutLock)
        {
            MigrateDefaultRoot();
            WrapCustomRootIntoMods();
            var root = ResolvedDownloadsRoot();
            if (nestedMapsRoot is not null && SamePath(nestedMapsRoot, root))
            {
                return;
            }

            NestMapsUnder(root);
            nestedMapsRoot = root;
        }
    }

    /// <summary>
    /// Old custom roots dumped Maps/sounds/skins in the picked folder. Move them under Mods\.
    /// </summary>
    private static void WrapCustomRootIntoMods()
    {
        var settings = AppSettings.Load();
        var saved = settings.DownloadsPath?.Trim();
        if (string.IsNullOrWhiteSpace(saved) || IsModsDirectoryName(saved))
        {
            return;
        }

        string from;
        try
        {
            from = Path.GetFullPath(saved);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        var mods = Path.Combine(from, "Mods");
        if (SamePath(from, mods))
        {
            return;
        }

        Directory.CreateDirectory(mods);
        MergeMoveDirectory(Path.Combine(from, "Maps"), Path.Combine(mods, "Maps"));
        MergeMoveDirectory(Path.Combine(from, "sounds"), Path.Combine(mods, "sounds"));
        MergeMoveDirectory(Path.Combine(from, "skins"), Path.Combine(mods, "skins"));
        settings.DownloadsPath = mods;
        settings.Save();
    }

    private static void MigrateDefaultRoot()
    {
        if (defaultRootMigrated)
        {
            return;
        }

        var settings = AppSettings.Load();
        var saved = settings.DownloadsPath?.Trim();
        var legacy = LegacyDefaultDownloadsRoot;
        var next = DefaultDownloadsRoot;
        var usingLegacySaved = !string.IsNullOrWhiteSpace(saved) && SamePath(saved, legacy);
        var usingDefault = string.IsNullOrWhiteSpace(saved);
        if ((!usingDefault && !usingLegacySaved) || !Directory.Exists(legacy) || SamePath(legacy, next))
        {
            defaultRootMigrated = true;
            return;
        }

        try
        {
            MergeMoveDirectory(legacy, next);
            if (usingLegacySaved)
            {
                settings.DownloadsPath = null;
                settings.Save();
            }

            defaultRootMigrated = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void NestMapsUnder(string root)
    {
        Directory.CreateDirectory(root);
        var mapsRoot = Path.Combine(root, "Maps");
        Directory.CreateDirectory(mapsRoot);
        Directory.CreateDirectory(Path.Combine(root, "sounds"));
        Directory.CreateDirectory(Path.Combine(root, "skins"));

        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (string.Equals(name, "Maps", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "sounds", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "skins", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(name, out var id) || id <= 0)
            {
                continue;
            }

            try
            {
                MergeMoveDirectory(dir, Path.Combine(mapsRoot, name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void MergeMoveDirectory(string from, string to)
    {
        if (!Directory.Exists(from) || SamePath(from, to))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (!Directory.Exists(to))
        {
            Directory.Move(from, to);
            return;
        }

        foreach (var source in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, source);
            var dest = Path.Combine(to, relative);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            if (File.Exists(dest))
            {
                File.Delete(dest);
            }

            try
            {
                File.Move(source, dest);
            }
            catch (IOException)
            {
                File.Copy(source, dest, overwrite: true);
                File.Delete(source);
            }
        }

        try
        {
            if (Directory.Exists(from))
            {
                Directory.Delete(from, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool SamePath(string a, string b)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
    }
}
