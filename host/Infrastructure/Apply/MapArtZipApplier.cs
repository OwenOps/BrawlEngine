using BrawlEngine.Host.Infrastructure.Steam;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class MapArtZipApplier
{
    private static readonly HashSet<string> SkipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".nfo", ".md", ".url", ".html", ".htm", ".exe", ".dll", ".json",
        ".mp3", ".wem", ".bnk", ".ogg", ".wav", ".flac",
    };

    /// <summary>Pack leftovers (ost in mapArt/mp3, etc.). Not vanilla mapArt folders.</summary>
    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "soundtrack", "ost", "music", "sounds", "audio", "songs",
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z",
    };

    public static int ApplyArchives(string gameRoot, IReadOnlyList<string> archivePaths)
    {
        var applied = 0;
        foreach (var archive in archivePaths)
        {
            applied += ApplyArchive(gameRoot, archive);
        }

        return applied;
    }

    public static int ApplyArchive(string gameRoot, string archivePath)
    {
        var extractRoot = Path.Combine(
            Path.GetTempPath(),
            "BrawlEngine",
            "extract",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);

        try
        {
            ExtractArchive(archivePath, extractRoot);
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

    public static (IReadOnlyList<string> Archives, string? Error) PickArchives(string downloadFolder)
    {
        var archives = Directory.GetFiles(downloadFolder)
            .Where(path => ArchiveExtensions.Contains(Path.GetExtension(path)))
            .ToList();

        if (archives.Count == 0)
        {
            return ([], "No archive found. Download this mod first, then Apply.");
        }

        var copyPaste = archives
            .Where(path => !Path.GetFileName(path).Contains("bmod", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return copyPaste.Count > 0 ? (copyPaste, null) : (archives, null);
    }

    /// <summary>
    /// Counts distinct map identifiers in a download folder's archive(s), so the UI can tell a
    /// single-map mod from a pack. Heuristic: mapArt files sharing a base name (across Backgrounds /
    /// Foregrounds / Thumbnails / BoneStructure) belong to the same map. Returns null if unknown.
    /// </summary>
    public static int? CountMaps(string downloadFolder) => CollectBaseNames(downloadFolder)?.Count;

    /// <summary>Same heuristic as <see cref="CountMaps"/>, but returns the names themselves (sorted) so
    /// the UI can list which maps a pack contains. Names are raw mapArt file names, not display titles.</summary>
    public static IReadOnlyList<string>? ListMapNames(string downloadFolder)
    {
        var baseNames = CollectBaseNames(downloadFolder);
        if (baseNames is null)
        {
            return null;
        }

        var names = baseNames.ToList();
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static HashSet<string>? CollectBaseNames(string downloadFolder)
    {
        if (!Directory.Exists(downloadFolder))
        {
            return null;
        }

        var (archives, error) = PickArchives(downloadFolder);
        if (error is not null || archives.Count == 0)
        {
            return null;
        }

        var baseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in archives)
            {
                using var archive = ArchiveFactory.OpenArchive(path);
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key))
                    {
                        continue;
                    }

                    if (IsJunkPath(entry.Key) || SkipExtensions.Contains(Path.GetExtension(entry.Key)))
                    {
                        continue;
                    }

                    baseNames.Add(Path.GetFileNameWithoutExtension(entry.Key));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ExtractionException)
        {
            return null;
        }

        return baseNames.Count == 0 ? null : baseNames;
    }

    /// <summary>Shared by <see cref="Mp3Applier"/> too — any GameBanana archive format, not just zip.</summary>
    internal static void ExtractArchive(string archivePath, string extractRoot)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            entry.WriteToDirectory(extractRoot, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true,
            });
        }
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
            var name = Path.GetFileName(dir)!;
            if (!SkipFolders.Contains(name))
            {
                names.Add(name);
            }
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
        if (IsJunkPath(relative))
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

    private static bool IsJunkPath(string relative)
    {
        if (relative.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parts = relative
            .Replace('/', Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (SkipFolders.Contains(part))
            {
                return true;
            }
        }

        return false;
    }
}
