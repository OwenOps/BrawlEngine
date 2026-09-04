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
        if (Directory.EnumerateFiles(dest, "*.wem").Any())
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
        return RestoreMapArt(gameRoot) + RestoreMp3();
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
