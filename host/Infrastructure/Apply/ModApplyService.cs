using BrawlEngine.Host.Domain.Models;
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
            return (false, "mapArt folder missing in the game folder.", null);
        }

        return ApplyMapMod(blocked.GameRoot!, modId, downloadIfMissing);
    }

    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryApplySound(
        int soundId,
        bool downloadIfMissing = false)
    {
        var blocked = EnsureCanWrite();
        if (blocked.Error is not null)
        {
            return (false, blocked.Error, null);
        }

        if (!HasFolder(blocked.GameRoot!, BrawlhallaLocator.Mp3Folder))
        {
            return (false, "mp3 folder missing in the game folder.", null);
        }

        return ApplySound(blocked.GameRoot!, soundId, downloadIfMissing);
    }

    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryReplaceTrack(string targetFileName)
    {
        var blocked = EnsureCanWrite();
        if (blocked.Error is not null)
        {
            return (false, blocked.Error, null);
        }

        if (!HasFolder(blocked.GameRoot!, BrawlhallaLocator.Mp3Folder))
        {
            return (false, "mp3 folder missing in the game folder.", null);
        }

        var picked = Mp3FilePicker.PickMp3();
        if (string.IsNullOrEmpty(picked))
        {
            return (false, "No MP3 selected.", null);
        }

        try
        {
            Mp3Applier.ReplaceTrack(blocked.GameRoot!, targetFileName, picked);
            return (true, null, new ApplyAttemptDto(true, "Replaced " + targetFileName + "."));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (false, "Replace failed: " + ex.Message, null);
        }
    }

    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryResetAll()
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
            return (true, null, new ApplyAttemptDto(true, "Restored " + restored + " vanilla file(s)."));
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
            var restored = VanillaReset.RestoreMapArt(blocked.GameRoot!);
            if (restored == 0)
            {
                return (false, "Nothing to restore in mapArt. Apply a map mod first so vanilla files are backed up.", null);
            }

            LoadoutStore.ClearMaps();
            return (true, null, new ApplyAttemptDto(true, "Ranked/safe: restored " + restored + " mapArt file(s). Music left unchanged."));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (false, "Ranked/safe failed: " + ex.Message, null);
        }
    }

    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryReapply()
    {
        var blocked = EnsureCanWrite();
        if (blocked.Error is not null)
        {
            return (false, blocked.Error, null);
        }

        var loadout = LoadoutStore.Load();
        if (loadout.Maps.Count == 0 && loadout.Music.Count == 0)
        {
            return (false, "No current loadout to reapply.", null);
        }

        var maps = 0;
        foreach (var entry in loadout.Maps)
        {
            var result = ApplyMapMod(blocked.GameRoot!, entry.ModId, downloadIfMissing: true);
            if (!result.Ok)
            {
                return (false, result.Error ?? "Reapply failed.", null);
            }

            maps++;
        }

        var music = 0;
        foreach (var entry in loadout.Music)
        {
            var result = ApplySound(blocked.GameRoot!, entry.ModId, downloadIfMissing: true);
            if (!result.Ok)
            {
                return (false, result.Error ?? "Reapply failed.", null);
            }

            music++;
        }

        return (true, null, new ApplyAttemptDto(true, "Reapplied " + maps + " map mod(s) and " + music + " sound(s)."));
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
                return (false, "Download this mod first.", null);
            }

            try
            {
                ModDownloadClient.DownloadAsync(modId).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                or InvalidOperationException or IOException)
            {
                return (false, "Download failed: " + ex.Message, null);
            }
        }

        var (zips, zipError) = MapArtZipApplier.PickZips(AppPaths.DownloadsFolder(modId));
        if (zipError is not null)
        {
            return (false, zipError, null);
        }

        try
        {
            var count = MapArtZipApplier.ApplyZips(gameRoot, zips);
            if (count == 0)
            {
                return (false, "This zip has no mapArt files to apply.", null);
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
        string gameRoot,
        int soundId,
        bool downloadIfMissing)
    {
        var folder = AppPaths.SoundDownloadsFolder(soundId);
        if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            if (!downloadIfMissing)
            {
                return (false, "Download this sound first.", null);
            }

            try
            {
                ModDownloadClient.DownloadAsync(soundId, GameBananaIds.SoundItemType).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                or InvalidOperationException or IOException)
            {
                return (false, "Download failed: " + ex.Message, null);
            }
        }

        try
        {
            var count = Mp3Applier.ApplyDownloadFolder(gameRoot, AppPaths.SoundDownloadsFolder(soundId));
            if (count == 0)
            {
                return (false, "This pack has no .mp3 that matches a file in the game mp3 folder. v1 only replaces those tracks (zip or mp3, not RAR).", null);
            }

            var reason = "Applied " + count + " mp3 file(s).";
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
}
