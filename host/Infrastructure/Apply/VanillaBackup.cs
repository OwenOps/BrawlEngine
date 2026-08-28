using BrawlEngine.Host.Infrastructure.Steam;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class VanillaBackup
{
    public static bool BackupIfMissing(string gameFile, string backupFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFile);

        if (!File.Exists(gameFile) || File.Exists(backupFile))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(backupFile);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(gameFile, backupFile, overwrite: false);
        return true;
    }

    public static bool BackupMapArtIfMissing(string gameRoot, string relativeUnderMapArt)
    {
        var (gameFile, backupFile) = MapArtPaths(gameRoot, relativeUnderMapArt);
        return BackupIfMissing(gameFile, backupFile);
    }

    public static (string GameFile, string BackupFile) MapArtPaths(string gameRoot, string relativeUnderMapArt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUnderMapArt);

        var mapArtRoot = Path.GetFullPath(Path.Combine(gameRoot, BrawlhallaLocator.MapArtFolder));
        var gameFile = Path.GetFullPath(Path.Combine(mapArtRoot, relativeUnderMapArt));
        EnsureUnder(mapArtRoot, gameFile);

        var backupRoot = Path.GetFullPath(Path.Combine(AppPaths.BackupsRoot, BrawlhallaLocator.MapArtFolder));
        var backupFile = Path.GetFullPath(Path.Combine(backupRoot, relativeUnderMapArt));
        EnsureUnder(backupRoot, backupFile);

        return (gameFile, backupFile);
    }

    private static void EnsureUnder(string root, string path)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path is outside mapArt.");
        }
    }
}
