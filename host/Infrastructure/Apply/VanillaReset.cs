using BrawlEngine.Host.Infrastructure.Steam;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class VanillaReset
{
    public static int RestoreMapArt(string gameRoot)
    {
        return RestoreSubfolder(gameRoot, BrawlhallaLocator.MapArtFolder);
    }

    public static int RestoreMp3(string gameRoot)
    {
        return RestoreSubfolder(gameRoot, BrawlhallaLocator.Mp3Folder);
    }

    public static int RestoreAll(string gameRoot)
    {
        return RestoreMapArt(gameRoot) + RestoreMp3(gameRoot);
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
}
