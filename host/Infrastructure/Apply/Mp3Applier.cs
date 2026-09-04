using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class Mp3Applier
{
    public static int ApplyDownloadFolder(string audioFolder, string downloadFolder)
    {
        var applied = 0;
        var (archives, _) = MapArtZipApplier.PickArchives(downloadFolder);
        foreach (var archive in archives)
        {
            applied += ApplyArchive(audioFolder, archive);
        }

        foreach (var file in Directory.EnumerateFiles(downloadFolder, "*.*"))
        {
            if (!IsAudioFile(file))
            {
                continue;
            }

            applied += ApplyAudioFile(audioFolder, file);
        }

        return applied;
    }

    public static int ApplyAudioFile(string audioFolder, string sourceFile)
    {
        var name = Path.GetFileName(sourceFile);
        var match = FindVanillaTrack(audioFolder, name);
        if (match is null || !SameFormat(sourceFile, match))
        {
            return 0;
        }

        VanillaBackup.BackupAudioIfMissing(audioFolder, match);
        var (gameFile, _) = VanillaBackup.AudioPaths(audioFolder, match);
        File.Copy(sourceFile, gameFile, overwrite: true);
        return 1;
    }

    public static int ReplaceTrack(string audioFolder, string targetFileName, string sourceFile)
    {
        var match = FindVanillaTrack(audioFolder, targetFileName);
        if (match is null)
        {
            throw new InvalidOperationException("That track is not in the game audio folder.");
        }

        EnsureSameFormat(sourceFile, match);
        VanillaBackup.BackupAudioIfMissing(audioFolder, match);
        var (gameFile, _) = VanillaBackup.AudioPaths(audioFolder, match);
        File.Copy(sourceFile, gameFile, overwrite: true);
        return 1;
    }

    public static IReadOnlyList<MusicTrackDto> ListTracks(string audioFolder)
    {
        return MusicTrackList.List(audioFolder);
    }

    private static bool IsAudioFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".wem", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameFormat(string sourceFile, string vanillaFileName)
    {
        var sourceExt = Path.GetExtension(sourceFile);
        var targetExt = Path.GetExtension(vanillaFileName);
        return string.Equals(sourceExt, targetExt, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureSameFormat(string sourceFile, string vanillaFileName)
    {
        if (SameFormat(sourceFile, vanillaFileName))
        {
            return;
        }

        if (Path.GetExtension(vanillaFileName).Equals(".wem", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "This game uses Wwise .wem files in audio\\pc. An MP3 cannot replace them. Pick a .wem with the same name, or convert it first.");
        }

        throw new InvalidOperationException(
            "The replacement file must be the same type as the in-game track ("
            + Path.GetExtension(vanillaFileName) + ").");
    }

    private static int ApplyArchive(string audioFolder, string archivePath)
    {
        var extractRoot = Path.Combine(
            Path.GetTempPath(),
            "BrawlEngine",
            "extract",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);

        try
        {
            MapArtZipApplier.ExtractArchive(archivePath, extractRoot);
            var applied = 0;
            foreach (var file in Directory.EnumerateFiles(extractRoot, "*.*", SearchOption.AllDirectories))
            {
                if (!IsAudioFile(file))
                {
                    continue;
                }

                applied += ApplyAudioFile(audioFolder, file);
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

    private static string? FindVanillaTrack(string audioFolder, string fileName)
    {
        var wanted = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(wanted) || !Directory.Exists(audioFolder))
        {
            return null;
        }

        foreach (var existing in Directory.GetFiles(audioFolder, "*.*"))
        {
            if (string.Equals(Path.GetFileName(existing), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileName(existing);
            }
        }

        return null;
    }
}
