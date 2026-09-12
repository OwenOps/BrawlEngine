using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Ffdec;
using BrawlEngine.Host.Infrastructure.GameBanana;
using BrawlEngine.Host.Infrastructure.Processes;
using BrawlEngine.Host.Infrastructure.Steam;
using BrawlEngine.Host.Infrastructure.Storage;
using System.Text.Json;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class ModApplyService
{
    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryApply(
        int modId,
        bool downloadIfMissing = false)
    {
        var blocked = EnsureCanWrite();
        if (blocked.Error is not null)
        {
            return (false, blocked.Error, null);
        }

        if (!HasFolder(blocked.GameRoot!, BrawlhallaLocator.MapArtFolder))
        {
            return (false, "mapArt folder missing. Choose the Brawlhalla game folder in the sidebar (the folder that contains mapArt).", null);
        }

        return ApplyMapMod(blocked.GameRoot!, modId, downloadIfMissing);
    }

    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryApplySound(
        int soundId,
        bool downloadIfMissing = false,
        string? category = null)
    {
        var musicWrite = EnsureCanWriteMusic();
        if (musicWrite.Error is not null)
        {
            return (false, musicWrite.Error, null);
        }

        return ApplySound(musicWrite.Mp3Folder!, soundId, downloadIfMissing, category);
    }

    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryApplySkin(int skinId)
    {
        var blocked = EnsureCanWrite();
        if (blocked.Error is not null)
        {
            return (false, blocked.Error, null);
        }

        try
        {
            FfdecLibFetch.Ensure();
        }
        catch (Exception ex) when (
            ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException
            or UnauthorizedAccessException)
        {
            return (false, "Could not download ffdec_lib.jar. Check your connection and Retry on the Skins tab.", null);
        }

        var tools = FfdecLocator.Resolve();
        if (!tools.Ready || tools.JavaPath is null || tools.JarPath is null)
        {
            return (false, tools.Error ?? "Java or ffdec_lib.jar is missing.", null);
        }

        return ApplySkin(blocked.GameRoot!, skinId, tools.JavaPath, tools.JarPath, downloadIfMissing: false);
    }

    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryReplaceTrack(
        string targetFileName,
        string? sourceUrl = null)
    {
        var musicWrite = EnsureCanWriteMusic();
        if (musicWrite.Error is not null)
        {
            return (false, musicWrite.Error, null);
        }

        if (!string.IsNullOrWhiteSpace(sourceUrl)
            && Path.GetExtension(targetFileName).Equals(".wem", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Direct MP3 links cannot replace Wwise .wem tracks. Pick a .wem with the same name, or convert it first.", null);
        }

        string? picked = null;
        var downloaded = false;
        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            try
            {
                picked = Mp3UrlFetch.DownloadToTempAsync(sourceUrl).GetAwaiter().GetResult();
                downloaded = true;
            }
            catch (Exception ex) when (
                ex is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException)
            {
                return (false, "Download failed: " + ex.Message, null);
            }
        }
        else
        {
            picked = Mp3FilePicker.PickAudio(targetFileName);
            if (string.IsNullOrEmpty(picked))
            {
                return (false, "No audio file selected.", null);
            }
        }

        try
        {
            Mp3Applier.ReplaceTrack(musicWrite.Mp3Folder!, targetFileName, picked);
            return (true, null, new ApplyAttemptDto(true, "Replaced " + targetFileName + "."));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (false, "Replace failed: " + ex.Message, null);
        }
        finally
        {
            if (downloaded && picked is not null)
            {
                Mp3UrlFetch.TryDelete(picked);
            }
        }
    }

    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryResetAll(bool deleteDownloads = false)
    {
        var blocked = EnsureCanWrite();
        if (blocked.Error is not null)
        {
            return (false, blocked.Error, null);
        }

        try
        {
            var restored = VanillaReset.RestoreAll(blocked.GameRoot!);
            if (restored == 0)
            {
                return (false, "Nothing to restore. Apply a mod first so vanilla files are backed up.", null);
            }

            LoadoutStore.Clear();
            var reason = "Restored " + restored + " vanilla file(s).";
            if (deleteDownloads)
            {
                DownloadInventory.DeleteAllDownloads();
                reason += " Deleted downloaded archives. Vanilla backups were kept.";
            }

            return (true, null, new ApplyAttemptDto(true, reason));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (false, "Reset failed: " + ex.Message, null);
        }
    }

    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryRankedSafe()
    {
        var blocked = EnsureCanWrite();
        if (blocked.Error is not null)
        {
            return (false, blocked.Error, null);
        }

        try
        {
            var restoredMaps = VanillaReset.RestoreMapArt(blocked.GameRoot!);
            var restoredSwf = VanillaReset.RestoreSwf(blocked.GameRoot!);
            if (restoredMaps == 0 && restoredSwf == 0)
            {
                return (false, "Nothing to restore in mapArt or SWF. Apply a map or skin first so vanilla files are backed up.", null);
            }

            LoadoutStore.ClearMaps();
            LoadoutStore.ClearSkins();
            return (true, null, new ApplyAttemptDto(
                true,
                "Ranked/safe: restored "
                    + restoredMaps
                    + " mapArt and "
                    + restoredSwf
                    + " SWF file(s). Music left unchanged."));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (false, "Ranked/safe failed: " + ex.Message, null);
        }
    }

    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryResetMap(int modId)
    {
        var blocked = EnsureCanWrite();
        if (blocked.Error is not null)
        {
            return (false, blocked.Error, null);
        }

        var loadout = LoadoutStore.Load();
        if (loadout.Maps.All(entry => entry.ModId != modId))
        {
            return (false, "This map is not in the current loadout.", null);
        }

        try
        {
            var restored = VanillaReset.RestoreMapArt(blocked.GameRoot!);
            if (restored == 0)
            {
                return (false, "Nothing to restore in mapArt. Apply a map first so vanilla files are backed up.", null);
            }

            LoadoutStore.RemoveMap(modId);
            var remaining = LoadoutStore.Load().Maps;
            foreach (var entry in remaining)
            {
                var result = ApplyMapMod(blocked.GameRoot!, entry.ModId, downloadIfMissing: true);
                if (!result.Ok)
                {
                    return (false, result.Error ?? "Could not reapply the other maps.", null);
                }
            }

            var extra = remaining.Count == 0
                ? " No other maps left."
                : " Reapplied " + remaining.Count + " other map(s).";
            return (true, null, new ApplyAttemptDto(true, "Removed this map." + extra));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (false, "Reset failed: " + ex.Message, null);
        }
    }

    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryResetSound(int soundId)
    {
        var musicWrite = EnsureCanWriteMusic();
        if (musicWrite.Error is not null)
        {
            return (false, musicWrite.Error, null);
        }

        var loadout = LoadoutStore.Load();
        if (loadout.Music.All(entry => entry.ModId != soundId))
        {
            return (false, "This sound is not in the current loadout.", null);
        }

        try
        {
            var restored = VanillaReset.RestoreMp3();
            if (restored == 0)
            {
                return (false, "Nothing to restore in audio. Apply a sound first so vanilla files are backed up.", null);
            }

            LoadoutStore.RemoveMusic(soundId);
            var remaining = LoadoutStore.Load().Music;
            foreach (var entry in remaining)
            {
                var result = ApplySound(musicWrite.Mp3Folder!, entry.ModId, downloadIfMissing: true, category: null);
                if (!result.Ok)
                {
                    return (false, result.Error ?? "Could not reapply the other sounds.", null);
                }
            }

            var extra = remaining.Count == 0
                ? " No other sounds left."
                : " Reapplied " + remaining.Count + " other sound(s).";
            return (true, null, new ApplyAttemptDto(true, "Removed this sound." + extra));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (false, "Reset failed: " + ex.Message, null);
        }
    }

    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryResetSkin(int skinId)
    {
        var blocked = EnsureCanWrite();
        if (blocked.Error is not null)
        {
            return (false, blocked.Error, null);
        }

        var loadout = LoadoutStore.Load();
        if ((loadout.Skins ?? []).All(entry => entry.ModId != skinId))
        {
            return (false, "This skin is not in the current loadout.", null);
        }

        try
        {
            var restored = VanillaReset.RestoreSwf(blocked.GameRoot!);
            if (restored == 0)
            {
                return (false, "Nothing to restore in SWF. Apply a skin first so vanilla files are backed up.", null);
            }

            LoadoutStore.RemoveSkin(skinId);
            var remaining = LoadoutStore.Load().Skins ?? [];
            if (remaining.Count > 0)
            {
                try
                {
                    FfdecLibFetch.Ensure();
                }
                catch (Exception ex) when (
                    ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException
                    or UnauthorizedAccessException)
                {
                    return (false, "Could not download ffdec_lib.jar. Check your connection and Retry on the Skins tab.", null);
                }

                var tools = FfdecLocator.Resolve();
                if (!tools.Ready || tools.JavaPath is null || tools.JarPath is null)
                {
                    return (false, tools.Error ?? "Java or ffdec_lib.jar is missing.", null);
                }

                foreach (var entry in remaining)
                {
                    var result = ApplySkin(blocked.GameRoot!, entry.ModId, tools.JavaPath, tools.JarPath, downloadIfMissing: false);
                    if (!result.Ok)
                    {
                        return (false, result.Error ?? "Could not reapply the other skins.", null);
                    }
                }
            }

            var extra = remaining.Count == 0
                ? " No other skins left."
                : " Reapplied " + remaining.Count + " other skin(s).";
            return (true, null, new ApplyAttemptDto(true, "Removed this skin." + extra));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (false, "Reset failed: " + ex.Message, null);
        }
    }

    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryReapply()
    {
        var loadout = LoadoutStore.Load();
        var skins = loadout.Skins ?? [];
        if (loadout.Maps.Count == 0 && loadout.Music.Count == 0 && skins.Count == 0)
        {
            return (false, "No current loadout to reapply.", null);
        }

        string? gameRoot = null;
        if (loadout.Maps.Count > 0 || skins.Count > 0)
        {
            var blocked = EnsureCanWrite();
            if (blocked.Error is not null)
            {
                return (false, blocked.Error, null);
            }

            gameRoot = blocked.GameRoot;
        }
        else
        {
            var guard = BrawlhallaProcess.Check();
            if (guard.Running)
            {
                return (false, guard.Message ?? "Close Brawlhalla before applying or changing game files.", null);
            }
        }

        var maps = 0;
        foreach (var entry in loadout.Maps)
        {
            var result = ApplyMapMod(gameRoot!, entry.ModId, downloadIfMissing: true);
            if (!result.Ok)
            {
                return (false, result.Error ?? "Reapply failed.", null);
            }

            maps++;
        }

        var music = 0;
        var mp3Folder = "";
        if (loadout.Music.Count > 0)
        {
            var musicWrite = EnsureCanWriteMusic();
            if (musicWrite.Error is not null)
            {
                return (false, musicWrite.Error, null);
            }

            mp3Folder = musicWrite.Mp3Folder!;
        }

        foreach (var entry in loadout.Music)
        {
            var result = ApplySound(mp3Folder, entry.ModId, downloadIfMissing: true, category: null);
            if (!result.Ok)
            {
                return (false, result.Error ?? "Reapply failed.", null);
            }

            music++;
        }

        var skinCount = 0;
        if (skins.Count > 0)
        {
            try
            {
                FfdecLibFetch.Ensure();
            }
            catch (Exception ex) when (
                ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException
                or UnauthorizedAccessException)
            {
                return (false, "Could not download ffdec_lib.jar. Check your connection and Retry on the Skins tab.", null);
            }

            var tools = FfdecLocator.Resolve();
            if (!tools.Ready || tools.JavaPath is null || tools.JarPath is null)
            {
                return (false, tools.Error ?? "Java or ffdec_lib.jar is missing.", null);
            }

            foreach (var entry in skins)
            {
                var result = ApplySkin(gameRoot!, entry.ModId, tools.JavaPath, tools.JarPath, downloadIfMissing: true);
                if (!result.Ok)
                {
                    return (false, result.Error ?? "Reapply failed.", null);
                }

                skinCount++;
            }
        }

        return (true, null, new ApplyAttemptDto(
            true,
            "Reapplied " + maps + " map(s), " + music + " sound(s), and " + skinCount + " skin(s)."));
    }

    private static (string? GameRoot, string? Error) EnsureCanWrite()
    {
        var guard = BrawlhallaProcess.Check();
        if (guard.Running)
        {
            return (null, guard.Message ?? "Close Brawlhalla before applying or changing game files.");
        }

        var location = BrawlhallaLocator.Resolve();
        if (!location.Found || location.Path is null)
        {
            return (null, "Brawlhalla folder not found. Choose the game folder.");
        }

        return (location.Path, null);
    }

    private static (string? Mp3Folder, string? Error) EnsureCanWriteMusic()
    {
        var guard = BrawlhallaProcess.Check();
        if (guard.Running)
        {
            return (null, guard.Message ?? "Close Brawlhalla before applying or changing game files.");
        }

        var mp3 = Mp3Locator.Resolve();
        if (!mp3.Found || mp3.Path is null)
        {
            return (null, "Game audio folder not found. Use Set music folder (audio\\pc).");
        }

        return (mp3.Path, null);
    }

    private static bool HasFolder(string gameRoot, string folder)
    {
        return Directory.Exists(Path.Combine(gameRoot, folder));
    }

    private static (bool Ok, string? Error, ApplyAttemptDto? Result) ApplyMapMod(
        string gameRoot,
        int modId,
        bool downloadIfMissing)
    {
        var folder = AppPaths.DownloadsFolder(modId);
        if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            if (!downloadIfMissing)
            {
                return (false, "Download this map first, then Apply.", null);
            }

            try
            {
                DownloadGate
                    .RunAsync("maps", modId, token => ModDownloadClient.DownloadAsync(modId, cancellationToken: token))
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException)
            {
                return (false, "Download cancelled.", null);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                or InvalidOperationException or IOException)
            {
                return (false, "Download failed: " + ex.Message, null);
            }
        }

        var (archives, archiveError) = MapArtZipApplier.PickArchives(AppPaths.DownloadsFolder(modId));
        if (archiveError is not null)
        {
            return (false, archiveError, null);
        }

        try
        {
            var count = MapArtZipApplier.ApplyArchives(gameRoot, archives);
            if (count == 0)
            {
                return (false, "This archive has no mapArt files. Check the pack on GameBanana or try another map.", null);
            }

            var reason = "Applied " + count + " file(s).";
            try
            {
                LoadoutStore.RecordMapApplied(modId);
            }
            catch (IOException ex)
            {
                reason += " Could not save loadout: " + ex.Message;
            }

            return (true, null, new ApplyAttemptDto(true, reason));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (false, "Apply failed: " + ex.Message, null);
        }
    }

    private static (bool Ok, string? Error, ApplyAttemptDto? Result) ApplySound(
        string mp3Folder,
        int soundId,
        bool downloadIfMissing,
        string? category)
    {
        if (SoundApplyKind.BlocksCatalogApply(category))
        {
            return (false, SoundApplyKind.CatalogBlockedMessage(), null);
        }

        var folder = AppPaths.SoundDownloadsFolder(soundId);
        if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            if (!downloadIfMissing)
            {
                return (false, "Download this sound first, then Apply.", null);
            }

            try
            {
                DownloadGate
                    .RunAsync(
                        "sounds",
                        soundId,
                        token => ModDownloadClient.DownloadAsync(
                            soundId,
                            GameBananaIds.SoundItemType,
                            cancellationToken: token))
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException)
            {
                return (false, "Download cancelled.", null);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                or InvalidOperationException or IOException)
            {
                return (false, "Download failed: " + ex.Message, null);
            }
        }

        try
        {
            var count = Mp3Applier.ApplyDownloadFolder(mp3Folder, AppPaths.SoundDownloadsFolder(soundId), category);
            if (count == 0)
            {
                return (false, "This pack has no .bnk / .wem (or .mp3) whose name matches a vanilla file under audio\\pc. Close the game if it is open. Music / Win / Main Theme cannot Apply this way.", null);
            }

            var reason = "Applied " + count + " audio file(s).";
            try
            {
                LoadoutStore.RecordMusicApplied(soundId);
            }
            catch (IOException ex)
            {
                reason += " Could not save loadout: " + ex.Message;
            }

            return (true, null, new ApplyAttemptDto(true, reason));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (false, "Apply failed: " + ex.Message, null);
        }
    }

    private static (bool Ok, string? Error, ApplyAttemptDto? Result) ApplySkin(
        string gameRoot,
        int skinId,
        string javaPath,
        string jarPath,
        bool downloadIfMissing)
    {
        var folder = AppPaths.SkinDownloadsFolder(skinId);
        if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            if (!downloadIfMissing)
            {
                return (false, "Download this skin first, then Apply.", null);
            }

            try
            {
                DownloadGate
                    .RunAsync(
                        "skins",
                        skinId,
                        token => ModDownloadClient.DownloadAsync(
                            skinId,
                            "Mod",
                            AppPaths.SkinDownloadsFolder(skinId),
                            progressKind: "skins",
                            cancellationToken: token))
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException)
            {
                return (false, "Download cancelled.", null);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                or InvalidOperationException or IOException)
            {
                return (false, "Download failed: " + ex.Message, null);
            }
        }

        var (bmodPaths, extractRoot, locateError) = SkinBmodLocator.Locate(folder);
        if (locateError is not null || bmodPaths.Count == 0)
        {
            return (false, locateError ?? "No .bmod found. Download this skin first, then Apply.", null);
        }

        var snapshots = new List<(string GameFile, string Snapshot)>();
        var workRoot = Path.Combine(Path.GetTempPath(), "BrawlEngine", "skin-apply", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        try
        {
            var (pack, parseError) = BmodManifest.TryParseMany(bmodPaths);
            if (parseError is not null || pack is null)
            {
                return (false, parseError ?? "This pack is not understood. Apply stopped.", null);
            }

            var jobs = new List<(
                string Relative,
                string GameFile,
                IReadOnlyList<string> Sprites,
                IReadOnlyList<BmodColorScript> ColorScripts)>();
            foreach (var swf in pack.Swfs)
            {
                var relative = VanillaBackup.FindSwfRelative(gameRoot, swf.FileName);
                if (relative is null)
                {
                    return (false, "Game SWF not found: " + swf.FileName + ". Apply stopped.", null);
                }

                var (gameFile, _) = VanillaBackup.SwfPaths(gameRoot, relative);
                if (!File.Exists(gameFile))
                {
                    return (false, "Game SWF not found: " + swf.FileName + ". Apply stopped.", null);
                }

                jobs.Add((relative, gameFile, swf.Sprites, swf.ColorScripts));
            }

            foreach (var job in jobs)
            {
                VanillaBackup.BackupSwfIfMissing(gameRoot, job.Relative);
                var snapshot = Path.Combine(workRoot, Guid.NewGuid().ToString("N") + ".swf");
                File.Copy(job.GameFile, snapshot, overwrite: false);
                snapshots.Add((job.GameFile, snapshot));
            }

            var replaced = 0;
            foreach (var job in jobs)
            {
                var outFile = Path.Combine(workRoot, Guid.NewGuid().ToString("N") + "-out.swf");
                var javaError = FfdecSkinApplier.ReplaceSprites(
                    javaPath,
                    jarPath,
                    bmodPaths,
                    job.GameFile,
                    outFile,
                    job.Sprites,
                    job.ColorScripts,
                    out var copied);
                if (javaError is not null)
                {
                    RestoreSnapshots(snapshots);
                    return (false, javaError, null);
                }

                if (!File.Exists(outFile))
                {
                    RestoreSnapshots(snapshots);
                    return (false, "Java did not write a patched SWF.", null);
                }

                if (copied == 0)
                {
                    continue;
                }

                File.Copy(outFile, job.GameFile, overwrite: true);
                replaced += copied;
            }

            var reason = "Applied " + replaced + " sprite(s) in " + jobs.Count + " SWF(s).";
            try
            {
                LoadoutStore.RecordSkinApplied(skinId);
            }
            catch (IOException ex)
            {
                reason += " Could not save loadout: " + ex.Message;
            }

            return (true, null, new ApplyAttemptDto(true, reason));
        }
        catch (Exception ex) when (
            ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            RestoreSnapshots(snapshots);
            return (false, "Apply failed: " + ex.Message, null);
        }
        finally
        {
            SkinBmodLocator.TryDelete(extractRoot);
            SkinBmodLocator.TryDelete(workRoot);
        }
    }

    private static void RestoreSnapshots(List<(string GameFile, string Snapshot)> snapshots)
    {
        foreach (var pair in snapshots)
        {
            try
            {
                if (File.Exists(pair.Snapshot))
                {
                    File.Copy(pair.Snapshot, pair.GameFile, overwrite: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
