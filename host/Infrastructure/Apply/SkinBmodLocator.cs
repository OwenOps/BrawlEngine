namespace BrawlEngine.Host.Infrastructure.Apply;

public static class SkinBmodLocator
{
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z",
    };

    public static (IReadOnlyList<string> BmodPaths, string? ExtractRoot, string? Error) Locate(string downloadFolder)
    {
        if (!Directory.Exists(downloadFolder))
        {
            return ([], null, "Download this skin first, then Apply.");
        }

        var extractRoot = Path.Combine(
            Path.GetTempPath(),
            "BrawlEngine",
            "skin-bmod",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);

        try
        {
            var archives = ArchivesIn(downloadFolder);
            for (var i = 0; i < archives.Count; i++)
            {
                var dest = Path.Combine(extractRoot, "a" + i.ToString("D3"));
                Directory.CreateDirectory(dest);
                MapArtZipApplier.ExtractArchive(archives[i], dest);
            }

            var nested = ArchivesIn(extractRoot);
            for (var i = 0; i < nested.Count; i++)
            {
                var dest = Path.Combine(extractRoot, "n" + i.ToString("D3"));
                Directory.CreateDirectory(dest);
                MapArtZipApplier.ExtractArchive(nested[i], dest);
            }

            var bmods = BmodsIn(downloadFolder)
                .Concat(BmodsIn(extractRoot))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (bmods.Count == 0)
            {
                TryDelete(extractRoot);
                return ([], null, "No .bmod found. This pack can be several zip/rar files; at least one must contain a .bmod.");
            }

            return (bmods, extractRoot, null);
        }
        catch (Exception ex) when (
            ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            TryDelete(extractRoot);
            return ([], null, "Could not unpack the skin archive: " + ex.Message);
        }
    }

    private static IReadOnlyList<string> ArchivesIn(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => ArchiveExtensions.Contains(Path.GetExtension(path)))
            .ToList();
    }

    private static IReadOnlyList<string> BmodsIn(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.GetFiles(folder, "*.bmod", SearchOption.AllDirectories);
    }

    public static void TryDelete(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
