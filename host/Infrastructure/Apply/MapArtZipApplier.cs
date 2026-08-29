using System.IO.Compression;
using BrawlEngine.Host.Infrastructure.Steam;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class MapArtZipApplier
{
    private static readonly HashSet<string> SkipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".nfo", ".md", ".url", ".html", ".htm", ".exe", ".dll", ".json",
    };

    public static int ApplyZips(string gameRoot, IReadOnlyList<string> zipPaths)
    {
        var applied = 0;
        foreach (var zip in zipPaths)
        {
            applied += ApplyZip(gameRoot, zip);
        }

        return applied;
    }

    public static int ApplyZip(string gameRoot, string zipPath)
    {
        var extractRoot = Path.Combine(
            Path.GetTempPath(),
            "BrawlEngine",
            "extract",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);
            var contentRoot = UnwrapContentRoot(extractRoot, MapArtTopLevelNames(gameRoot));
            var applied = 0;

            foreach (var file in Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories))
            {
                if (IsJunk(contentRoot, file))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(contentRoot, file);
                var mapped = ResolveMapArtRelative(gameRoot, relative);
                if (string.IsNullOrWhiteSpace(mapped))
                {
                    continue;
                }

                var (gameFile, _) = VanillaBackup.MapArtPaths(gameRoot, mapped);
                VanillaBackup.BackupMapArtIfMissing(gameRoot, mapped);
                var destDir = Path.GetDirectoryName(gameFile);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(file, gameFile, overwrite: true);
                applied++;
            }

            return applied;
        }
        finally
        {
            try
            {
                if (Directory.Exists(extractRoot))
                {
                    Directory.Delete(extractRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // Temp leftover is acceptable; do not fail Apply after a successful copy.
            }
        }
    }

    public static (IReadOnlyList<string> Zips, string? Error) PickZips(string downloadFolder)
    {
        var files = Directory.GetFiles(downloadFolder);
        var zips = files
            .Where(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var hasOtherArchive = files.Any(path =>
            path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase));

        if (zips.Count == 0)
        {
            if (hasOtherArchive)
            {
                return ([], "This archive is not a zip. v1 supports zip only.");
            }

            return ([], "No zip archive found. Download this mod first.");
        }

        var copyPaste = zips
            .Where(path => !Path.GetFileName(path).Contains("bmod", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return copyPaste.Count > 0 ? (copyPaste, null) : (zips, null);
    }

    private static HashSet<string> MapArtTopLevelNames(string gameRoot)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Backgrounds",
            "BoneStructure",
            "Foregrounds",
            "Thumbnails",
        };

        var mapArt = Path.Combine(gameRoot, BrawlhallaLocator.MapArtFolder);
        if (!Directory.Exists(mapArt))
        {
            return names;
        }

        foreach (var dir in Directory.EnumerateDirectories(mapArt))
        {
            names.Add(Path.GetFileName(dir)!);
        }

        return names;
    }

    private static string UnwrapContentRoot(string extractRoot, HashSet<string> knownTop)
    {
        var current = extractRoot;
        for (var i = 0; i < 6; i++)
        {
            var dirs = Directory.GetDirectories(current);
            var files = Directory.GetFiles(current);
            if (files.Length > 0 || dirs.Length != 1)
            {
                return current;
            }

            var only = Path.GetFileName(dirs[0])!;
            if (only.Equals(BrawlhallaLocator.MapArtFolder, StringComparison.OrdinalIgnoreCase))
            {
                current = dirs[0];
                continue;
            }

            if (knownTop.Contains(only))
            {
                return current;
            }

            current = dirs[0];
        }

        return current;
    }

    private static string? ResolveMapArtRelative(string gameRoot, string relativeFromZip)
    {
        var parts = relativeFromZip
            .Replace('/', Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        if (parts[0].Equals(BrawlhallaLocator.MapArtFolder, StringComparison.OrdinalIgnoreCase))
        {
            parts = parts[1..];
        }

        if (parts.Length == 0)
        {
            return null;
        }

        var mapped = Path.Combine(parts);
        if (parts.Length > 1)
        {
            return mapped;
        }

        var mapArt = Path.Combine(gameRoot, BrawlhallaLocator.MapArtFolder);
        if (!Directory.Exists(mapArt))
        {
            return mapped;
        }

        var matches = Directory
            .EnumerateFiles(mapArt, Path.GetFileName(mapped), SearchOption.AllDirectories)
            .ToList();
        if (matches.Count != 1)
        {
            return mapped;
        }

        return Path.GetRelativePath(mapArt, matches[0]);
    }

    private static bool IsJunk(string contentRoot, string file)
    {
        var relative = Path.GetRelativePath(contentRoot, file);
        if (relative.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = Path.GetFileName(file);
        if (name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
            || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SkipExtensions.Contains(Path.GetExtension(file));
    }
}
