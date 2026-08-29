using System.IO.Compression;
using BrawlEngine.Host.Infrastructure.Steam;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class Mp3Applier
{
    public static int ApplyDownloadFolder(string gameRoot, string downloadFolder)
    {
        var applied = 0;
        var (zips, zipError) = MapArtZipApplier.PickZips(downloadFolder);
        if (zips.Count > 0)
        {
            foreach (var zip in zips)
            {
                applied += ApplyZip(gameRoot, zip);
            }
        }

        foreach (var mp3 in Directory.EnumerateFiles(downloadFolder, "*.mp3"))
        {
            applied += ApplyMp3File(gameRoot, mp3);
        }

        if (applied == 0 && zips.Count == 0 && zipError is not null
            && zipError.Contains("not a zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(zipError);
        }

        return applied;
    }

    public static int ApplyMp3File(string gameRoot, string sourceMp3)
    {
        var name = Path.GetFileName(sourceMp3);
        var match = FindVanillaTrack(gameRoot, name);
        if (match is null)
        {
            return 0;
        }

        VanillaBackup.BackupMp3IfMissing(gameRoot, match);
        var (gameFile, _) = VanillaBackup.Mp3Paths(gameRoot, match);
        File.Copy(sourceMp3, gameFile, overwrite: true);
        return 1;
    }

    public static int ReplaceTrack(string gameRoot, string targetFileName, string sourceMp3)
    {
        var match = FindVanillaTrack(gameRoot, targetFileName);
        if (match is null)
        {
            throw new InvalidOperationException("That track is not in the game mp3 folder.");
        }

        VanillaBackup.BackupMp3IfMissing(gameRoot, match);
        var (gameFile, _) = VanillaBackup.Mp3Paths(gameRoot, match);
        File.Copy(sourceMp3, gameFile, overwrite: true);
        return 1;
    }

    public static IReadOnlyList<string> ListTracks(string gameRoot)
    {
        var folder = Path.Combine(gameRoot, BrawlhallaLocator.Mp3Folder);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.GetFiles(folder, "*.mp3")
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ApplyZip(string gameRoot, string zipPath)
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
            var applied = 0;
            foreach (var file in Directory.EnumerateFiles(extractRoot, "*.mp3", SearchOption.AllDirectories))
            {
                applied += ApplyMp3File(gameRoot, file);
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
            }
        }
    }

    private static string? FindVanillaTrack(string gameRoot, string fileName)
    {
        var wanted = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(wanted))
        {
            return null;
        }

        var folder = Path.Combine(gameRoot, BrawlhallaLocator.Mp3Folder);
        if (!Directory.Exists(folder))
        {
            return null;
        }

        foreach (var existing in Directory.GetFiles(folder, "*.mp3"))
        {
            if (string.Equals(Path.GetFileName(existing), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileName(existing);
            }
        }

        return null;
    }
}
