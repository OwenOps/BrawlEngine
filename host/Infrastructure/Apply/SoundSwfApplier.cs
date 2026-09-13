namespace BrawlEngine.Host.Infrastructure.Apply;

/// <summary>
/// Old UI / Weapon / announcer packs drop Sound.swf / Sound02.swf.
/// Copy only if that file still exists in the game (current Brawlhalla dropped them).
/// Never copy Gfx_ / Bones_ / SFX_.
/// </summary>
public static class SoundSwfApplier
{
    public readonly record struct Result(int Applied, int FoundInPack);

    public static bool IsSoundSwfName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        return name.StartsWith("Sound", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".swf", StringComparison.OrdinalIgnoreCase);
    }

    public static Result ApplyDownloadFolder(string? gameRoot, string downloadFolder)
    {
        var applied = 0;
        var found = 0;
        var (archives, _) = MapArtZipApplier.PickArchives(downloadFolder);
        foreach (var archive in archives)
        {
            var fromArchive = ApplyArchive(gameRoot, archive);
            applied += fromArchive.Applied;
            found += fromArchive.FoundInPack;
        }

        foreach (var file in Directory.EnumerateFiles(downloadFolder, "*.*", SearchOption.AllDirectories))
        {
            if (!IsSoundSwfName(file))
            {
                continue;
            }

            found++;
            if (!string.IsNullOrWhiteSpace(gameRoot))
            {
                applied += ApplySwfFile(gameRoot, file);
            }
        }

        return new Result(applied, found);
    }

    public static int ApplySwfFile(string gameRoot, string sourceFile)
    {
        if (!IsSoundSwfName(sourceFile))
        {
            return 0;
        }

        var relative = FindSoundSwfRelative(gameRoot, Path.GetFileName(sourceFile));
        if (relative is null)
        {
            return 0;
        }

        VanillaBackup.BackupSwfIfMissing(gameRoot, relative);
        var (gameFile, _) = VanillaBackup.SwfPaths(gameRoot, relative);
        var destDir = Path.GetDirectoryName(gameFile);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        File.Copy(sourceFile, gameFile, overwrite: true);
        return 1;
    }

    private static string? FindSoundSwfRelative(string gameRoot, string swfFileName)
    {
        var relative = VanillaBackup.FindSwfRelative(gameRoot, swfFileName);
        if (relative is not null)
        {
            return relative;
        }

        var root = Path.GetFullPath(gameRoot);
        var inSounds = Path.GetFullPath(Path.Combine(root, "sounds", Path.GetFileName(swfFileName)));
        if (File.Exists(inSounds))
        {
            return Path.GetRelativePath(root, inSounds);
        }

        return null;
    }

    private static Result ApplyArchive(string? gameRoot, string archivePath)
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
            var found = 0;
            foreach (var file in Directory.EnumerateFiles(extractRoot, "*.*", SearchOption.AllDirectories))
            {
                if (!IsSoundSwfName(file))
                {
                    continue;
                }

                found++;
                if (!string.IsNullOrWhiteSpace(gameRoot))
                {
                    applied += ApplySwfFile(gameRoot, file);
                }
            }

            return new Result(applied, found);
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
}
