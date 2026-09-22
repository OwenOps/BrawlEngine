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

    public static bool BackupAudioIfMissing(string audioFolder, string relativeUnderAudio)
    {
        var (gameFile, backupFile) = AudioPaths(audioFolder, relativeUnderAudio);
        return BackupIfMissing(gameFile, backupFile);
    }

    public static (string GameFile, string BackupFile) MapArtPaths(string gameRoot, string relativeUnderMapArt)
    {
        return PathsUnder(gameRoot, BrawlhallaLocator.MapArtFolder, relativeUnderMapArt);
    }

    public static (string GameFile, string BackupFile) AudioPaths(string audioFolder, string relativeUnderAudio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUnderAudio);

        var relative = relativeUnderAudio
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var folderRoot = Path.GetFullPath(audioFolder);
        var gameFile = Path.GetFullPath(Path.Combine(folderRoot, relative));
        EnsureUnder(folderRoot, gameFile);

        var name = Path.GetFileName(relative);
        var backupSub = Path.GetExtension(name).Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            ? BrawlhallaLocator.Mp3Folder
            : "audio-pc";
        var backupRoot = Path.GetFullPath(Path.Combine(AppPaths.BackupsRoot, backupSub));
        var backupFile = Path.GetFullPath(Path.Combine(backupRoot, relative));
        EnsureUnder(backupRoot, backupFile);

        return (gameFile, backupFile);
    }

    public const string SwfBackupFolder = "swf";

    /// <summary>
    /// .bmod lists a bare file name. On this game install: root for Gfx_/SFX_, bones\ for Bones_.
    /// Do not search the whole tree.
    /// </summary>
    public static string? FindSwfRelative(string gameRoot, string swfFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(swfFileName);

        var name = Path.GetFileName(swfFileName.Trim());
        if (!name.Equals(swfFileName.Trim(), StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var root = Path.GetFullPath(gameRoot);
        var atRoot = Path.GetFullPath(Path.Combine(root, name));
        if (File.Exists(atRoot))
        {
            return Path.GetRelativePath(root, atRoot);
        }

        var inBones = Path.GetFullPath(Path.Combine(root, "bones", name));
        if (File.Exists(inBones))
        {
            return Path.GetRelativePath(root, inBones);
        }

        return null;
    }

    public static bool BackupSwfIfMissing(string gameRoot, string relativeFromGame)
    {
        var (gameFile, backupFile) = SwfPaths(gameRoot, relativeFromGame);
        return BackupIfMissing(gameFile, backupFile);
    }

    public static (string GameFile, string BackupFile) SwfPaths(string gameRoot, string relativeFromGame)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFromGame);

        var relative = relativeFromGame
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var folderRoot = Path.GetFullPath(gameRoot);
        var gameFile = Path.GetFullPath(Path.Combine(folderRoot, relative));
        EnsureUnder(folderRoot, gameFile);

        var backupRoot = Path.GetFullPath(Path.Combine(AppPaths.BackupsRoot, SwfBackupFolder));
        var backupFile = Path.GetFullPath(Path.Combine(backupRoot, relative));
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
