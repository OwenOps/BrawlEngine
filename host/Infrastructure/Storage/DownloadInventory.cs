using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.Storage;

public static class DownloadInventory
{
    public static LocalDownloadListDto List(string kind)
    {
        return kind switch
        {
            "sounds" => ListUnder(AppPaths.SoundDownloadsRoot),
            "skins" => ListUnder(AppPaths.SkinDownloadsRoot),
            _ => ListMaps(),
        };
    }

    public static string KindFolder(string kind) =>
        kind switch
        {
            "sounds" => AppPaths.SoundDownloadsRoot,
            "skins" => AppPaths.SkinDownloadsRoot,
            _ => AppPaths.DownloadsRoot,
        };

    private static LocalDownloadListDto ListMaps()
    {
        var root = AppPaths.DownloadsRoot;
        if (!Directory.Exists(root))
        {
            return new LocalDownloadListDto([]);
        }

        var items = new List<LocalDownloadDto>();
        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (string.Equals(name, "sounds", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "skins", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(name, out var id) && HasFiles(dir))
            {
                items.Add(ToItem(id, dir));
            }
        }

        return new LocalDownloadListDto(items);
    }

    private static LocalDownloadListDto ListUnder(string root)
    {
        if (!Directory.Exists(root))
        {
            return new LocalDownloadListDto([]);
        }

        var items = new List<LocalDownloadDto>();
        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (int.TryParse(name, out var id) && HasFiles(dir))
            {
                items.Add(ToItem(id, dir));
            }
        }

        return new LocalDownloadListDto(items);
    }

    private static LocalDownloadDto ToItem(int id, string folder)
    {
        return new LocalDownloadDto(id, folder, MapCount: null, SizeBytes: FolderSize(folder));
    }

    private static long FolderSize(string folder)
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                }
            }
        }
        catch (IOException)
        {
            return 0;
        }

        return total;
    }

    private static bool HasFiles(string folder)
    {
        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any();
    }

    /// <summary>Frees disk space for a mod already applied — the game files it copied stay untouched.</summary>
    public static bool Delete(string kind, int id)
    {
        var folder = kind switch
        {
            "sounds" => AppPaths.SoundDownloadsFolder(id),
            "skins" => AppPaths.SkinDownloadsFolder(id),
            _ => AppPaths.DownloadsFolder(id),
        };

        if (!Directory.Exists(folder))
        {
            return false;
        }

        Directory.Delete(folder, recursive: true);
        return true;
    }

    /// <summary>Deletes GameBanana archives under downloads\. Does not touch vanilla backups.</summary>
    public static void DeleteAllDownloads()
    {
        var root = AppPaths.DownloadsRoot;
        if (!Directory.Exists(root))
        {
            return;
        }

        Directory.Delete(root, recursive: true);
    }
}
