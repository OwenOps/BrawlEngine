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
        var (gameFile, backupFile) = PathsUnder(gameRoot, BrawlhallaLocator.MapArtFolder, relativeUnderMapArt);
        return BackupIfMissing(gameFile, backupFile);
    }

    public static bool BackupAudioIfMissing(string audioFolder, string fileName)
    {
        var (gameFile, backupFile) = AudioPaths(audioFolder, fileName);
        return BackupIfMissing(gameFile, backupFile);
    }

    public static (string GameFile, string BackupFile) MapArtPaths(string gameRoot, string relativeUnderMapArt)
    {
        return PathsUnder(gameRoot, BrawlhallaLocator.MapArtFolder, relativeUnderMapArt);
    }

    public static (string GameFile, string BackupFile) AudioPaths(string audioFolder, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var name = Path.GetFileName(fileName);
        var folderRoot = Path.GetFullPath(audioFolder);
        var gameFile = Path.GetFullPath(Path.Combine(folderRoot, name));
        EnsureUnder(folderRoot, gameFile);

        var backupSub = Path.GetExtension(name).Equals(".wem", StringComparison.OrdinalIgnoreCase)
            ? "audio-pc"
            : BrawlhallaLocator.Mp3Folder;
        var backupRoot = Path.GetFullPath(Path.Combine(AppPaths.BackupsRoot, backupSub));
        var backupFile = Path.GetFullPath(Path.Combine(backupRoot, name));
        EnsureUnder(backupRoot, backupFile);

        return (gameFile, backupFile);
    }

    public static (string GameFile, string BackupFile) PathsUnder(
        string gameRoot,
        string gameSubfolder,
        string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameSubfolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);

        var folderRoot = Path.GetFullPath(Path.Combine(gameRoot, gameSubfolder));
        var gameFile = Path.GetFullPath(Path.Combine(folderRoot, relative));
        EnsureUnder(folderRoot, gameFile);

        var backupRoot = Path.GetFullPath(Path.Combine(AppPaths.BackupsRoot, gameSubfolder));
        var backupFile = Path.GetFullPath(Path.Combine(backupRoot, relative));
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
            throw new InvalidOperationException("Path is outside the allowed game folder.");
        }
    }
}
