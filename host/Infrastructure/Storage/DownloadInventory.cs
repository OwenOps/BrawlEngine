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
            _ => ListUnder(AppPaths.MapDownloadsRoot),
        };
    }

    public static string KindFolder(string kind) =>
        kind switch
        {
            "sounds" => AppPaths.SoundDownloadsRoot,
            "skins" => AppPaths.SkinDownloadsRoot,
            _ => AppPaths.MapDownloadsRoot,
        };

    public static DownloadSummaryDto Summary()
    {
        var maps = List("maps");
        var sounds = List("sounds");
        var skins = List("skins");
        var count = maps.Items.Count + sounds.Items.Count + skins.Items.Count;
        var size =
            maps.Items.Sum(item => item.SizeBytes)
            + sounds.Items.Sum(item => item.SizeBytes)
            + skins.Items.Sum(item => item.SizeBytes);
        return new DownloadSummaryDto(
            count,
            size,
            AppPaths.DownloadsRoot,
            DownloadsLocator.IsUsingDefault());
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
            if (int.TryParse(name, out var id))
            {
                var (hasFiles, size) = FolderStats(dir);
                if (hasFiles)
                {
                    items.Add(new LocalDownloadDto(
                        id,
                        dir,
                        MapCount: null,
                        SizeBytes: size,
                        DownloadedUtc: FolderWrittenUtc(dir)));
                }
            }
        }

        return new LocalDownloadListDto(items);
    }

    private static (bool HasFiles, long SizeBytes) FolderStats(string folder)
    {
        long total = 0;
        var any = false;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                any = true;
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
            return (false, 0);
        }

        return (any, total);
    }

    private static DateTimeOffset FolderWrittenUtc(string folder)
    {
        var latest = Directory.GetLastWriteTimeUtc(folder);
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var written = File.GetLastWriteTimeUtc(file);
                if (written > latest)
                {
                    latest = written;
                }
            }
        }
        catch (IOException)
        {
        }

        return new DateTimeOffset(DateTime.SpecifyKind(latest, DateTimeKind.Utc));
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

    /// <summary>Deletes GameBanana archives under mods\. Does not touch vanilla backups.</summary>
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
