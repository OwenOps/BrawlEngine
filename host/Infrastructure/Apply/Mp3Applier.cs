using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Steam;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class Mp3Applier
{
    public static int ApplyDownloadFolder(
        string audioFolder,
        string downloadFolder,
        string? category = null,
        string? targetFileName = null)
    {
        var preferSubfolder = SoundApplyKind.PreferSubfolder(category);
        var applied = 0;
        var (archives, _) = MapArtZipApplier.PickArchives(downloadFolder);
        foreach (var archive in archives)
        {
            applied += ApplyArchive(audioFolder, archive, preferSubfolder, targetFileName, downloadFolder);
        }

        if (applied > 0)
        {
            return applied;
        }

        string? looseConvertible = null;
        foreach (var file in Directory.EnumerateFiles(downloadFolder, "*.*", SearchOption.AllDirectories))
        {
            if (WemEncoder.IsConvertible(file) && looseConvertible is null)
            {
                looseConvertible = file;
            }

            if (!IsCopyAudio(file))
            {
                continue;
            }

            applied += ApplyAudioFile(audioFolder, file, preferSubfolder);
        }

        if (applied > 0)
        {
            return applied;
        }

        return EncodeFirst(audioFolder, downloadFolder, looseConvertible, targetFileName) ? 1 : 0;
    }

    public static int ApplyAudioFile(string audioFolder, string sourceFile, string? preferSubfolder = null)
    {
        var name = Path.GetFileName(sourceFile);
        var match = FindVanillaTrack(audioFolder, name, preferSubfolder);
        if (match is not null && SameFormat(sourceFile, match))
        {
            CopyOnto(audioFolder, match, sourceFile, encode: false);
            return 1;
        }

        if (!WemEncoder.IsConvertible(sourceFile))
        {
            return 0;
        }

        var legacy = MusicTrackList.MapLegacyFileName(audioFolder, name);
        if (legacy is null)
        {
            return 0;
        }

        CopyOnto(audioFolder, legacy, sourceFile, encode: true);
        return 1;
    }

    public static int ReplaceTrack(
        string audioFolder,
        string targetFileName,
        string sourceFile,
        string? sourceLabel = null)
    {
        var wanted = Path.GetFileName(targetFileName);
        var match = FindVanillaTrack(audioFolder, wanted, preferSubfolder: null);
        if (match is null
            && wanted.EndsWith(".wem", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(Path.GetFileNameWithoutExtension(wanted), out _))
        {
            match = wanted;
        }

        if (match is null)
        {
            throw new InvalidOperationException("That track is not in the game audio folder.");
        }

        var encode = !SameFormat(sourceFile, match);
        if (encode && !CanEncodeOnto(sourceFile, match))
        {
            throw new InvalidOperationException(
                "The replacement file must be the same type as the in-game track ("
                + Path.GetExtension(match)
                + "), or an MP3 / WAV / OGG / FLAC to convert.");
        }

        CopyOnto(audioFolder, match, sourceFile, encode);
        RecordThemeChange(audioFolder, match, sourceLabel ?? Path.GetFileName(sourceFile));
        return 1;
    }

    public static int RestoreTheme(string audioFolder, string targetFileName)
    {
        if (!uint.TryParse(Path.GetFileNameWithoutExtension(targetFileName), out var mediaId))
        {
            throw new InvalidOperationException("That track is not in the game audio folder.");
        }

        var restored = 0;
        foreach (var bank in ThemeBanks(audioFolder, mediaId))
        {
            restored += RestoreAudioFile(audioFolder, bank);
        }

        foreach (var part in ThemeParts(audioFolder, mediaId))
        {
            restored += RestoreAudioFile(audioFolder, part.FileName);
        }

        if (restored == 0)
        {
            throw new InvalidOperationException(
                "No vanilla backup for this theme yet. Replace once to create one, or use Reset all.");
        }

        return restored;
    }

    public static MusicTracksDto ListTracks(string audioFolder)
    {
        var tracks = MusicTrackList.List(audioFolder);
        var changedFiles = FilesDifferingFromBackup(audioFolder);
        var bankIds = MusBankMedia(audioFolder);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var history = AudioChangeStore.Load().Entries
            .GroupBy(entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        var annotated = new List<MusicTrackDto>(tracks.Count);
        foreach (var track in tracks)
        {
            var files = ThemeFiles(track.FileName, bankIds);
            foreach (var file in files)
            {
                claimed.Add(file);
            }

            var isChanged = files.Any(changedFiles.Contains) || changedFiles.Contains(track.FileName);
            string? source = null;
            string? at = null;
            if (isChanged && history.TryGetValue(track.FileName, out var entry))
            {
                source = entry.Source;
                at = entry.At;
            }

            annotated.Add(track with { Changed = isChanged, Source = source, ChangedAt = at });
        }

        AudioChangeStore.KeepOnly(annotated.Where(track => track.Changed).Select(track => track.FileName));

        var other = changedFiles
            .Where(name => !claimed.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        const int otherCap = 40;
        return new MusicTracksDto(annotated, other.Take(otherCap).ToList(), other.Count);
    }

    public static string NeedsTrackMessage()
    {
        return "This pack has no .bnk / .wem / .mp3 whose name matches a vanilla file. Pick a track under Custom audio, then Apply (MP3 / WAV / OGG will convert).";
    }

    private static bool EncodeFirst(
        string audioFolder,
        string downloadFolder,
        string? sourceFile,
        string? targetFileName)
    {
        if (string.IsNullOrWhiteSpace(targetFileName) || string.IsNullOrWhiteSpace(sourceFile))
        {
            return false;
        }

        var match = FindVanillaTrack(audioFolder, targetFileName, preferSubfolder: null);
        if (match is null
            || !match.EndsWith(".wem", StringComparison.OrdinalIgnoreCase)
            || !WemEncoder.IsConvertible(sourceFile))
        {
            return false;
        }

        CopyOnto(audioFolder, match, sourceFile, encode: true);
        KeepConvertedWem(downloadFolder, match, audioFolder);
        RecordThemeChange(audioFolder, match, Path.GetFileName(sourceFile));
        return true;
    }

    private static void KeepConvertedWem(string downloadFolder, string vanillaRelative, string audioFolder)
    {
        var (gameFile, _) = VanillaBackup.AudioPaths(audioFolder, vanillaRelative);
        if (!File.Exists(gameFile))
        {
            return;
        }

        Directory.CreateDirectory(downloadFolder);
        File.Copy(gameFile, Path.Combine(downloadFolder, Path.GetFileName(vanillaRelative)), overwrite: true);
    }

    private static void CopyOnto(string audioFolder, string vanillaRelative, string sourceFile, bool encode)
    {
        if (encode)
        {
            EncodeTheme(audioFolder, vanillaRelative, sourceFile);
            ApplyProgress.Note("Copied into the game.");
            return;
        }

        VanillaBackup.BackupAudioIfMissing(audioFolder, vanillaRelative);
        var (gameFile, _) = VanillaBackup.AudioPaths(audioFolder, vanillaRelative);
        var destDir = Path.GetDirectoryName(gameFile);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        ApplyProgress.Note("Copying into the game…");
        File.Copy(sourceFile, gameFile, overwrite: true);
        PatchOwningBanks(audioFolder, vanillaRelative, gameFile);
        ApplyProgress.Note("Copied into the game.");
    }

    private static void EncodeTheme(string audioFolder, string vanillaRelative, string sourceFile)
    {
        if (!uint.TryParse(Path.GetFileNameWithoutExtension(vanillaRelative), out var mediaId))
        {
            ConvertAndPatch(audioFolder, vanillaRelative, sourceFile);
            return;
        }

        var parts = ThemeParts(audioFolder, mediaId);
        if (parts.Count == 0)
        {
            ConvertAndPatch(audioFolder, vanillaRelative, sourceFile);
            return;
        }

        ThemePart loop = parts[0];
        foreach (var part in parts)
        {
            if (part.IsLongest)
            {
                loop = part;
                break;
            }
        }

        ApplyProgress.Note("Encoding " + loop.FileName + "…");
        ConvertOne(audioFolder, loop.FileName, sourceFile);
        var (loopFile, _) = VanillaBackup.AudioPaths(audioFolder, loop.FileName);
        var wem = File.ReadAllBytes(loopFile);
        var durationMs = WemEncoder.ReadDuration(loopFile).TotalMilliseconds;
        PatchOwningBanks(audioFolder, loop.FileName, loopFile, durationMs);

        foreach (var part in parts)
        {
            if (part.FileName.Equals(loop.FileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ApplyProgress.Note("Writing " + part.FileName + "…");
            VanillaBackup.BackupAudioIfMissing(audioFolder, part.FileName);
            var (gameFile, _) = VanillaBackup.AudioPaths(audioFolder, part.FileName);
            var destDir = Path.GetDirectoryName(gameFile);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.WriteAllBytes(gameFile, wem);
            PatchOwningBanks(audioFolder, part.FileName, gameFile, part.Duration.TotalMilliseconds);
        }
    }

    private static void ConvertAndPatch(string audioFolder, string vanillaRelative, string sourceFile)
    {
        ConvertOne(audioFolder, vanillaRelative, sourceFile);
        var (gameFile, _) = VanillaBackup.AudioPaths(audioFolder, vanillaRelative);
        PatchOwningBanks(audioFolder, vanillaRelative, gameFile, WemEncoder.ReadDuration(gameFile).TotalMilliseconds);
    }

    private static void ConvertOne(
        string audioFolder,
        string vanillaRelative,
        string sourceFile,
        TimeSpan? skip = null,
        TimeSpan? take = null)
    {
        VanillaBackup.BackupAudioIfMissing(audioFolder, vanillaRelative);
        var (gameFile, backupFile) = VanillaBackup.AudioPaths(audioFolder, vanillaRelative);
        var destDir = Path.GetDirectoryName(gameFile);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        var template = ResolveTemplate(audioFolder, vanillaRelative);
        WemEncoder.ConvertToWem(sourceFile, gameFile, template, skip, take);
    }

    private static string ResolveTemplate(string audioFolder, string vanillaRelative)
    {
        var (gameFile, backupFile) = VanillaBackup.AudioPaths(audioFolder, vanillaRelative);
        if (File.Exists(backupFile))
        {
            return backupFile;
        }

        if (File.Exists(gameFile))
        {
            return gameFile;
        }

        if (!uint.TryParse(Path.GetFileNameWithoutExtension(vanillaRelative), out var mediaId))
        {
            return gameFile;
        }

        foreach (var bank in ThemeBanks(audioFolder, mediaId))
        {
            var (bnkGame, bnkBackup) = VanillaBackup.AudioPaths(audioFolder, bank);
            var bnk = File.Exists(bnkBackup) ? bnkBackup : bnkGame;
            if (!File.Exists(bnk))
            {
                continue;
            }

            var blob = WwiseBankPatch.ReadMedia(bnk, mediaId);
            if (blob is null || blob.Length < 64)
            {
                continue;
            }

            var tmp = Path.Combine(AppPaths.Root, "tmp", mediaId + ".vanilla.wem");
            Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
            File.WriteAllBytes(tmp, blob);
            return tmp;
        }

        return gameFile;
    }

    private static int RestoreAudioFile(string audioFolder, string relative)
    {
        var (gameFile, backupFile) = VanillaBackup.AudioPaths(audioFolder, relative);
        if (!File.Exists(backupFile))
        {
            return 0;
        }

        var destDir = Path.GetDirectoryName(gameFile);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        File.Copy(backupFile, gameFile, overwrite: true);
        return 1;
    }

    private static IReadOnlyList<string> ThemeBanks(string audioFolder, uint mediaId)
    {
        var banks = new List<string>();
        foreach (var bnk in Directory.GetFiles(audioFolder, "MUS_*.bnk"))
        {
            IReadOnlyList<uint> ids;
            try
            {
                ids = WwiseDidx.ReadMediaIds(bnk);
            }
            catch (IOException)
            {
                continue;
            }

            if (ids.Contains(mediaId))
            {
                banks.Add(Path.GetFileName(bnk));
            }
        }

        return banks;
    }

    private readonly record struct ThemePart(string FileName, TimeSpan Duration, bool IsLongest, uint Size);

    private static List<ThemePart> ThemeParts(string audioFolder, uint mediaId)
    {
        var ids = new HashSet<uint>();
        foreach (var bnk in Directory.GetFiles(audioFolder, "MUS_*.bnk"))
        {
            IReadOnlyList<uint> bankIds;
            try
            {
                bankIds = WwiseDidx.ReadMediaIds(bnk);
            }
            catch (IOException)
            {
                continue;
            }

            if (!bankIds.Contains(mediaId))
            {
                continue;
            }

            foreach (var id in bankIds)
            {
                ids.Add(id);
            }
        }

        var parts = new List<ThemePart>();
        foreach (var id in ids)
        {
            var name = id + ".wem";
            var (gameFile, backupFile) = VanillaBackup.AudioPaths(audioFolder, name);
            var duration = TimeSpan.Zero;
            uint size = 0;
            if (File.Exists(backupFile))
            {
                duration = WemEncoder.ReadDuration(backupFile);
                size = (uint)new FileInfo(backupFile).Length;
            }
            else if (File.Exists(gameFile))
            {
                duration = WemEncoder.ReadDuration(gameFile);
                size = (uint)new FileInfo(gameFile).Length;
            }
            else
            {
                foreach (var bank in ThemeBanks(audioFolder, id))
                {
                    var (bnkGame, bnkBackup) = VanillaBackup.AudioPaths(audioFolder, bank);
                    var bnk = File.Exists(bnkBackup) ? bnkBackup : bnkGame;
                    if (!File.Exists(bnk))
                    {
                        continue;
                    }

                    var blob = WwiseBankPatch.ReadMedia(bnk, id);
                    if (blob is null)
                    {
                        continue;
                    }

                    duration = WemEncoder.ReadDuration(blob);
                    size = (uint)blob.Length;
                    break;
                }
            }

            if (duration <= TimeSpan.Zero && size == 0)
            {
                continue;
            }

            if (duration <= TimeSpan.Zero)
            {
                duration = TimeSpan.FromSeconds(1);
            }

            parts.Add(new ThemePart(name, duration, false, size));
        }

        if (parts.Count == 0)
        {
            return parts;
        }

        var longest = 0;
        for (var i = 1; i < parts.Count; i++)
        {
            if (parts[i].Duration > parts[longest].Duration
                || (parts[i].Duration == parts[longest].Duration && parts[i].Size > parts[longest].Size))
            {
                longest = i;
            }
        }

        parts[longest] = parts[longest] with { IsLongest = true };
        return parts;
    }

    private static void PatchOwningBanks(
        string audioFolder,
        string vanillaRelative,
        string wemPath,
        double durationMs = 0)
    {
        if (!vanillaRelative.EndsWith(".wem", StringComparison.OrdinalIgnoreCase)
            || !uint.TryParse(Path.GetFileNameWithoutExtension(vanillaRelative), out var mediaId)
            || !File.Exists(wemPath))
        {
            return;
        }

        var wem = File.ReadAllBytes(wemPath);
        foreach (var bnk in Directory.GetFiles(audioFolder, "*.bnk"))
        {
            if (!WwiseDidx.ReadMediaIds(bnk).Contains(mediaId))
            {
                continue;
            }

            var name = Path.GetFileName(bnk);
            VanillaBackup.BackupAudioIfMissing(audioFolder, name);
            ApplyProgress.Note("Writing " + name + "…");
            WwiseBankPatch.ReplaceMedia(bnk, mediaId, wem, durationMs);
        }
    }

    private static bool CanEncodeOnto(string sourceFile, string vanillaFileName)
    {
        return vanillaFileName.EndsWith(".wem", StringComparison.OrdinalIgnoreCase)
            && WemEncoder.IsConvertible(sourceFile);
    }

    private static bool IsCopyAudio(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".wem", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bnk", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".flac", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameFormat(string sourceFile, string vanillaFileName)
    {
        var sourceExt = Path.GetExtension(sourceFile);
        var targetExt = Path.GetExtension(vanillaFileName);
        return string.Equals(sourceExt, targetExt, StringComparison.OrdinalIgnoreCase);
    }

    private static int ApplyArchive(
        string audioFolder,
        string archivePath,
        string? preferSubfolder,
        string? targetFileName,
        string downloadFolder)
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
            string? convertible = null;
            foreach (var file in Directory.EnumerateFiles(extractRoot, "*.*", SearchOption.AllDirectories))
            {
                if (WemEncoder.IsConvertible(file) && convertible is null)
                {
                    convertible = file;
                }

                if (!IsCopyAudio(file))
                {
                    continue;
                }

                applied += ApplyAudioFile(audioFolder, file, preferSubfolder);
            }

            if (applied > 0)
            {
                return applied;
            }

            return EncodeFirst(audioFolder, downloadFolder, convertible, targetFileName) ? 1 : 0;
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

    private static string? FindVanillaTrack(string audioFolder, string fileName, string? preferSubfolder)
    {
        var wanted = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(wanted) || !Directory.Exists(audioFolder))
        {
            return null;
        }

        var matches = Directory.GetFiles(audioFolder, wanted, SearchOption.AllDirectories);
        if (matches.Length == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferSubfolder))
        {
            foreach (var path in matches)
            {
                var relative = Path.GetRelativePath(audioFolder, path);
                var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                if (first.Equals(preferSubfolder, StringComparison.OrdinalIgnoreCase))
                {
                    return relative;
                }
            }
        }

        var shortest = matches[0];
        var shortestLen = Path.GetRelativePath(audioFolder, shortest).Length;
        foreach (var path in matches)
        {
            var len = Path.GetRelativePath(audioFolder, path).Length;
            if (len < shortestLen)
            {
                shortest = path;
                shortestLen = len;
            }
        }

        return Path.GetRelativePath(audioFolder, shortest);
    }

    private static void RecordThemeChange(string audioFolder, string vanillaRelative, string? source)
    {
        var fileName = Path.GetFileName(vanillaRelative);
        var label = fileName;
        foreach (var track in MusicTrackList.List(audioFolder))
        {
            if (track.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                label = track.Label;
                break;
            }
        }

        AudioChangeStore.Record(fileName, label, source);
    }

    private static HashSet<string> FilesDifferingFromBackup(string audioFolder)
    {
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDifferingBackupFiles(changed, audioFolder, "audio-pc");
        AddDifferingBackupFiles(changed, audioFolder, BrawlhallaLocator.Mp3Folder);
        return changed;
    }

    private static void AddDifferingBackupFiles(HashSet<string> changed, string audioFolder, string backupSub)
    {
        var backupRoot = Path.GetFullPath(Path.Combine(AppPaths.BackupsRoot, backupSub));
        if (!Directory.Exists(backupRoot))
        {
            return;
        }

        var folderRoot = Path.GetFullPath(audioFolder);
        foreach (var backupFile in Directory.EnumerateFiles(backupRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(backupRoot, backupFile);
            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                continue;
            }

            var gameFile = Path.GetFullPath(Path.Combine(folderRoot, relative));
            if (!gameFile.StartsWith(folderRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!File.Exists(gameFile) || new FileInfo(gameFile).Length != new FileInfo(backupFile).Length)
            {
                changed.Add(relative);
            }
        }
    }

    private static Dictionary<string, IReadOnlyList<uint>> MusBankMedia(string audioFolder)
    {
        var map = new Dictionary<string, IReadOnlyList<uint>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(audioFolder))
        {
            return map;
        }

        foreach (var bnk in Directory.GetFiles(audioFolder, "MUS_*.bnk"))
        {
            try
            {
                map[Path.GetFileName(bnk)] = WwiseDidx.ReadMediaIds(bnk);
            }
            catch (IOException)
            {
            }
        }

        return map;
    }

    private static List<string> ThemeFiles(string trackFileName, Dictionary<string, IReadOnlyList<uint>> bankIds)
    {
        var files = new List<string>();
        if (!trackFileName.EndsWith(".wem", StringComparison.OrdinalIgnoreCase)
            || !uint.TryParse(Path.GetFileNameWithoutExtension(trackFileName), out var mediaId))
        {
            files.Add(trackFileName);
            return files;
        }

        foreach (var pair in bankIds)
        {
            if (!pair.Value.Contains(mediaId))
            {
                continue;
            }

            files.Add(pair.Key);
            foreach (var id in pair.Value)
            {
                files.Add(id + ".wem");
            }
        }

        if (files.Count == 0)
        {
            files.Add(trackFileName);
        }

        return files;
    }
}
