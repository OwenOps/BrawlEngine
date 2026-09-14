using BrawlEngine.Host.Infrastructure.Steam;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class VanillaReset
{
    public static int RestoreMapArt(string gameRoot)
    {
        return RestoreSubfolder(gameRoot, BrawlhallaLocator.MapArtFolder);
    }

    public static int RestoreMp3()
    {
        var mp3 = Mp3Locator.Resolve();
        if (!mp3.Found || mp3.Path is null)
        {
            return 0;
        }

        var dest = mp3.Path;
        var restored = 0;
        if (Directory.EnumerateFiles(dest, "*.wem", SearchOption.AllDirectories).Any()
            || Directory.EnumerateFiles(dest, "*.bnk", SearchOption.AllDirectories).Any())
        {
            restored += RestoreIntoFolder(dest, "audio-pc");
        }
        else
        {
            restored += RestoreIntoFolder(dest, BrawlhallaLocator.Mp3Folder);
        }

        return restored;
    }

    public static int RestoreAll(string gameRoot)
    {
        return RestoreMapArt(gameRoot) + RestoreMp3() + RestoreSwf(gameRoot);
    }

    public static int RestoreSoundSwf(string gameRoot)
    {
        return RestoreSwf(gameRoot, includeSoundSwf: true, soundSwfOnly: true);
    }

    public static int RestoreSwf(string gameRoot, bool includeSoundSwf = true)
    {
        return RestoreSwf(gameRoot, includeSoundSwf, soundSwfOnly: false);
    }

    public static int RestoreListedSwf(string gameRoot, IReadOnlyList<string> relatives)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        var restored = 0;
        foreach (var relative in relatives)
        {
            if (string.IsNullOrWhiteSpace(relative)
                || SoundSwfApplier.IsSoundSwfName(Path.GetFileName(relative)))
            {
                continue;
            }

            var (gameFile, backupFile) = VanillaBackup.SwfPaths(gameRoot, relative);
            if (!File.Exists(backupFile))
            {
                continue;
            }
            var destDir = Path.GetDirectoryName(gameFile);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(backupFile, gameFile, overwrite: true);
            restored++;
        }

        return restored;
    }

    private static int RestoreSwf(string gameRoot, bool includeSoundSwf, bool soundSwfOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);

        var backupRoot = Path.GetFullPath(Path.Combine(AppPaths.BackupsRoot, VanillaBackup.SwfBackupFolder));
        if (!Directory.Exists(backupRoot))
        {
            return 0;
        }

        var restored = 0;
        foreach (var backupFile in Directory.EnumerateFiles(backupRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(backupRoot, backupFile);
            var isSound = SoundSwfApplier.IsSoundSwfName(Path.GetFileName(relative));
            if (soundSwfOnly && !isSound)
            {
                continue;
            }

            if (!includeSoundSwf && isSound)
            {
                continue;
            }

            var (gameFile, _) = VanillaBackup.SwfPaths(gameRoot, relative);
            var destDir = Path.GetDirectoryName(gameFile);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(backupFile, gameFile, overwrite: true);
            restored++;
        }

        return restored;
    }

    private static int RestoreSubfolder(string gameRoot, string gameSubfolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);

        var backupRoot = Path.GetFullPath(Path.Combine(AppPaths.BackupsRoot, gameSubfolder));
        if (!Directory.Exists(backupRoot))
        {
            return 0;
        }

        var restored = 0;
        foreach (var backupFile in Directory.EnumerateFiles(backupRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(backupRoot, backupFile);
            var (gameFile, _) = VanillaBackup.PathsUnder(gameRoot, gameSubfolder, relative);
            var destDir = Path.GetDirectoryName(gameFile);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(backupFile, gameFile, overwrite: true);
            restored++;
        }

        return restored;
    }

    private static int RestoreIntoFolder(string destFolder, string backupSubfolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destFolder);

        var backupRoot = Path.GetFullPath(Path.Combine(AppPaths.BackupsRoot, backupSubfolder));
        if (!Directory.Exists(backupRoot))
        {
            return 0;
        }

        var folderRoot = Path.GetFullPath(destFolder);
        var restored = 0;
        foreach (var backupFile in Directory.EnumerateFiles(backupRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(backupRoot, backupFile);
            var gameFile = Path.GetFullPath(Path.Combine(folderRoot, relative));
            var destDir = Path.GetDirectoryName(gameFile);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(backupFile, gameFile, overwrite: true);
            restored++;
        }

        return restored;
    }
}
