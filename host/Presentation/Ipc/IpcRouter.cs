using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Apply;
using BrawlEngine.Host.Infrastructure.GameBanana;
using BrawlEngine.Host.Infrastructure.Processes;
using BrawlEngine.Host.Infrastructure.Steam;
using BrawlEngine.Host.Infrastructure.Storage;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Photino.NET;

namespace BrawlEngine.Host.Presentation.Ipc;

public static class IpcRouter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static void Handle(PhotinoWindow window, string raw)
    {
        IpcEnvelope request;
        try
        {
            request = JsonSerializer.Deserialize<IpcEnvelope>(raw, JsonOptions)
                ?? new IpcEnvelope { Id = "", Type = "unknown", Ok = false, Error = "Empty message" };
        }
        catch (JsonException ex)
        {
            window.SendWebMessage(JsonSerializer.Serialize(new IpcEnvelope
            {
                Id = "",
                Type = "error",
                Ok = false,
                Error = "Invalid JSON: " + ex.Message,
            }, JsonOptions));
            return;
        }

        var reply = request.Type switch
        {
            "ping" => Pong(request),
            "game.get" => GameLocation(request, Mp3Locator.AttachTo(BrawlhallaLocator.Resolve())),
            "game.pick" => GameLocation(request, Mp3Locator.AttachTo(BrawlhallaLocator.PickFolder())),
            "music.pick" => MusicPick(request),
            "game.running" => ApplyGuard(request),
            "catalog.maps" => CatalogMaps(request),
            "catalog.sounds" => CatalogSounds(request),
            "catalog.skins" => CatalogSkins(request),
            "catalog.byIds" => CatalogByIds(request),
            "browser.open" => OpenUrl(request),
            "folder.open" => OpenFolder(request),
            "mod.download" => DownloadMod(window, request),
            "sound.download" => DownloadSound(window, request),
            "skin.download" => DownloadSkin(window, request),
            "downloads.cancel" => CancelDownload(request),
            "downloads.list" => ListDownloads(request),
            "downloads.delete" => DeleteDownload(request),
            "downloads.open" => OpenDownloads(request),
            "mod.mapNames" => MapNames(request),
            "mod.apply" => ApplyMod(request),
            "mod.reset" => ResetMap(request),
            "music.apply" => ApplySound(request),
            "music.reset" => ResetSound(request),
            "music.tracks" => MusicTracks(request),
            "music.replace" => ReplaceTrack(request),
            "loadout.get" => Loadout(request),
            "mods.resetAll" => ResetAll(request),
            "mods.rankedSafe" => RankedSafe(request),
            "mods.reapply" => Reapply(request),
            "configs.list" => ConfigsList(request),
            "configs.save" => ConfigsSave(request),
            "configs.load" => ConfigsLoad(request),
            "configs.delete" => ConfigsDelete(request),
            _ => new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Unknown message type: " + request.Type,
            },
        };

        window.SendWebMessage(JsonSerializer.Serialize(reply, JsonOptions));
    }

    /// <summary>UI loading spinners wait forever unless every request gets a reply, even after a crash.</summary>
    public static void SendFailure(PhotinoWindow window, string raw, string error)
    {
        var id = "";
        var type = "error";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("id", out var idEl))
            {
                id = idEl.GetString() ?? "";
            }

            if (doc.RootElement.TryGetProperty("type", out var typeEl))
            {
                type = typeEl.GetString() ?? "error";
            }
        }
        catch (JsonException)
        {
        }

        window.SendWebMessage(JsonSerializer.Serialize(new IpcEnvelope
        {
            Id = id,
            Type = type,
            Ok = false,
            Error = error,
        }, JsonOptions));
    }

    private static IpcEnvelope CatalogLoadFailed(IpcEnvelope request, Exception ex)
    {
        Debug.WriteLine(request.Type + ": " + ex);
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = false,
            Error = GameBananaJson.CatalogLoadError,
        };
    }

    private static IpcEnvelope Pong(IpcEnvelope request)
    {
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = "pong",
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(new { app = "BrawlEngine" }, JsonOptions),
        };
    }

    private static IpcEnvelope GameLocation(IpcEnvelope request, GameLocationDto location)
    {
        var ok = string.IsNullOrEmpty(location.Error);
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = ok,
            Error = location.Error,
            Payload = JsonSerializer.SerializeToElement(location, JsonOptions),
        };
    }

    private static IpcEnvelope MusicPick(IpcEnvelope request)
    {
        var picked = Mp3Locator.PickFolder();
        var location = Mp3Locator.AttachTo(BrawlhallaLocator.Resolve());
        if (picked.Cancelled)
        {
            return GameLocation(request, location with { Cancelled = true });
        }

        if (!string.IsNullOrEmpty(picked.Error))
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = picked.Error,
                Payload = JsonSerializer.SerializeToElement(location, JsonOptions),
            };
        }

        return GameLocation(request, location);
    }

    private static IpcEnvelope ApplyGuard(IpcEnvelope request)
    {
        var status = BrawlhallaProcess.Check();
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(status, JsonOptions),
        };
    }

    private static IpcEnvelope CatalogSounds(IpcEnvelope request)
    {
        var (apiPage, query, sort, categoryId) = ReadCatalog(request);
        try
        {
            var page = SoundCatalogClient.ListAsync(apiPage, query, sort, categoryId).GetAwaiter().GetResult();
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = true,
                Payload = JsonSerializer.SerializeToElement(page, JsonOptions),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return CatalogLoadFailed(request, ex);
        }
    }

    private static IpcEnvelope CatalogSkins(IpcEnvelope request)
    {
        var (apiPage, query, sort, _) = ReadCatalog(request);
        try
        {
            var page = SkinCatalogClient.ListAsync(apiPage, query, sort).GetAwaiter().GetResult();
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = true,
                Payload = JsonSerializer.SerializeToElement(page, JsonOptions),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return CatalogLoadFailed(request, ex);
        }
    }

    private static IpcEnvelope CatalogByIds(IpcEnvelope request)
    {
        var kind = ReadString(request, "kind");
        var itemType = kind == "sounds" ? GameBananaIds.SoundItemType : "Mod";
        var defaultCategory = kind == "sounds"
            ? "Sounds"
            : GameBananaIds.RealmsCategoryName;
        var ids = ReadIntList(request, "ids");
        try
        {
            var items = CatalogProfileClient
                .GetAsync(itemType, ids, defaultCategory)
                .GetAwaiter()
                .GetResult();
            var page = new CatalogPageDto(items, 1, 2, true, items.Count, items.Count);
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = true,
                Payload = JsonSerializer.SerializeToElement(page, JsonOptions),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return CatalogLoadFailed(request, ex);
        }
    }

    private static IpcEnvelope DownloadSound(PhotinoWindow window, IpcEnvelope request)
    {
        var id = ReadId(request);
        if (id <= 0)
        {
            return MissingId(request);
        }

        return RunDownload(
            request,
            "sounds",
            id,
            token => ModDownloadClient.DownloadAsync(
                id,
                GameBananaIds.SoundItemType,
                progressKind: "sounds",
                onProgress: progress => PushProgress(window, progress),
                cancellationToken: token));
    }

    private static IpcEnvelope DownloadSkin(PhotinoWindow window, IpcEnvelope request)
    {
        var id = ReadId(request);
        if (id <= 0)
        {
            return MissingId(request);
        }

        return RunDownload(
            request,
            "skins",
            id,
            token => ModDownloadClient.DownloadAsync(
                id,
                "Mod",
                AppPaths.SkinDownloadsFolder(id),
                progressKind: "skins",
                onProgress: progress => PushProgress(window, progress),
                cancellationToken: token));
    }

    private static IpcEnvelope CancelDownload(IpcEnvelope request)
    {
        var id = ReadId(request);
        var kind = ReadString(request, "kind");
        if (id <= 0)
        {
            return MissingId(request);
        }

        DownloadGate.TryCancel(kind, id);
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
        };
    }

    private static IpcEnvelope RunDownload(
        IpcEnvelope request,
        string kind,
        int id,
        Func<CancellationToken, Task<DownloadResultDto>> start)
    {
        try
        {
            var result = DownloadGate.RunAsync(kind, id, start).GetAwaiter().GetResult();
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = true,
                Payload = JsonSerializer.SerializeToElement(result, JsonOptions),
            };
        }
        catch (OperationCanceledException)
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Download cancelled.",
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or IOException)
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Download failed: " + ex.Message,
            };
        }
    }

    private static IpcEnvelope ListDownloads(IpcEnvelope request)
    {
        var kind = ReadString(request, "kind");
        if (kind != "maps" && kind != "sounds" && kind != "skins")
        {
            kind = "maps";
        }

        var list = DownloadInventory.List(kind);
        if (kind == "maps")
        {
            var withMapCount = list.Items
                .Select(item => item with { MapCount = MapArtZipApplier.CountMaps(item.Folder) })
                .ToList();
            list = new LocalDownloadListDto(withMapCount);
        }

        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(list, JsonOptions),
        };
    }

    /// <summary>Lists the mapArt base names inside a downloaded pack, so the UI can show which maps it contains.</summary>
    private static IpcEnvelope MapNames(IpcEnvelope request)
    {
        var id = ReadId(request);
        if (id <= 0)
        {
            return MissingId(request);
        }

        var names = MapArtZipApplier.ListMapNames(AppPaths.DownloadsFolder(id));
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(new { names = names ?? [] }, JsonOptions),
        };
    }

    private static IpcEnvelope DeleteDownload(IpcEnvelope request)
    {
        var id = ReadId(request);
        if (id <= 0)
        {
            return MissingId(request);
        }

        var kind = ReadString(request, "kind");
        if (kind != "maps" && kind != "sounds" && kind != "skins")
        {
            kind = "maps";
        }

        try
        {
            var deleted = DownloadInventory.Delete(kind, id);
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = true,
                Payload = JsonSerializer.SerializeToElement(new { deleted }, JsonOptions),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Could not delete the download: " + ex.Message,
            };
        }
    }

    private static IpcEnvelope ApplySound(IpcEnvelope request)
    {
        var id = ReadId(request);
        if (id <= 0)
        {
            return MissingId(request);
        }

        var (ok, error, result) = ModApplyService.TryApplySound(id, category: ReadString(request, "category"));
        return Attempt(request, ok, error, result);
    }

    private static IpcEnvelope ResetSound(IpcEnvelope request)
    {
        var id = ReadId(request);
        if (id <= 0)
        {
            return MissingId(request);
        }

        var (ok, error, result) = ModApplyService.TryResetSound(id);
        return Attempt(request, ok, error, result);
    }

    private static IpcEnvelope MusicTracks(IpcEnvelope request)
    {
        var mp3 = Mp3Locator.Resolve();
        if (!mp3.Found || mp3.Path is null)
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Game audio folder not found. Use Set audio folder (audio\\pc, or the Brawlhalla folder).",
            };
        }

        var tracks = Mp3Applier.ListTracks(mp3.Path);
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(new { tracks }, JsonOptions),
        };
    }

    private static IpcEnvelope ReplaceTrack(IpcEnvelope request)
    {
        var target = "";
        var url = "";
        if (request.Payload is { } payload
            && payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("target", out var targetEl))
            {
                target = targetEl.GetString() ?? "";
            }

            if (payload.TryGetProperty("url", out var urlEl))
            {
                url = urlEl.GetString() ?? "";
            }
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Choose which in-game track to replace.",
            };
        }

        var (ok, error, result) = ModApplyService.TryReplaceTrack(
            target,
            string.IsNullOrWhiteSpace(url) ? null : url);
        return Attempt(request, ok, error, result);
    }

    private static (int Page, string Query, string Sort, int CategoryId) ReadCatalog(IpcEnvelope request)
    {
        var apiPage = 1;
        var query = "";
        var sort = "newest";
        var categoryId = 0;
        if (request.Payload is not { } payload || payload.ValueKind != JsonValueKind.Object)
        {
            return (apiPage, query, sort, categoryId);
        }

        if (payload.TryGetProperty("page", out var pageEl)
            && pageEl.TryGetInt32(out var parsed)
            && parsed > 0)
        {
            apiPage = parsed;
        }

        if (payload.TryGetProperty("query", out var queryEl))
        {
            query = queryEl.GetString() ?? "";
        }

        if (payload.TryGetProperty("sort", out var sortEl))
        {
            var value = sortEl.GetString() ?? "";
            if (value is "newest" or "liked" or "downloaded")
            {
                sort = value;
            }
        }

        if (payload.TryGetProperty("categoryId", out var catEl)
            && catEl.TryGetInt32(out var catId)
            && catId > 0)
        {
            categoryId = catId;
        }

        return (apiPage, query, sort, categoryId);
    }

    private static IpcEnvelope OpenUrl(IpcEnvelope request)
    {
        var url = ReadString(request, "url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !IsGameBananaHost(uri.Host))
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Only https GameBanana links can be opened.",
            };
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Could not open the browser: " + ex.Message,
            };
        }

        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
        };
    }

    private static IpcEnvelope OpenFolder(IpcEnvelope request)
    {
        var path = ReadString(request, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Missing folder path.",
            };
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "That folder path is not valid.",
            };
        }

        var root = Path.GetFullPath(AppPaths.Root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, Path.GetFullPath(AppPaths.Root), StringComparison.OrdinalIgnoreCase))
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Can only open folders inside BrawlEngine data.",
            };
        }

        if (!Directory.Exists(full))
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "That folder is not on disk yet.",
            };
        }

        return LaunchExplorer(request, full);
    }

    private static IpcEnvelope OpenDownloads(IpcEnvelope request)
    {
        var kind = ReadString(request, "kind");
        if (kind != "maps" && kind != "sounds" && kind != "skins")
        {
            kind = "maps";
        }

        var folder = DownloadInventory.KindFolder(kind);
        Directory.CreateDirectory(folder);
        return LaunchExplorer(request, folder);
    }

    private static IpcEnvelope LaunchExplorer(IpcEnvelope request, string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Could not open the folder: " + ex.Message,
            };
        }

        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
        };
    }

    /// <summary>Sends an unsolicited "download.progress" message (empty Id: no request is waiting on it).</summary>
    private static void PushProgress(PhotinoWindow window, DownloadProgressDto progress)
    {
        var envelope = new IpcEnvelope
        {
            Id = "",
            Type = "download.progress",
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(progress, JsonOptions),
        };
        window.SendWebMessage(JsonSerializer.Serialize(envelope, JsonOptions));
    }

    private static bool IsGameBananaHost(string host)
    {
        return host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("www.gamebanana.com", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadId(IpcEnvelope request)
    {
        if (request.Payload is { } payload
            && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("id", out var idEl)
            && idEl.TryGetInt32(out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static IpcEnvelope MissingId(IpcEnvelope request)
    {
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = false,
            Error = "Missing id.",
        };
    }

    private static IpcEnvelope CatalogMaps(IpcEnvelope request)
    {
        var (apiPage, query, sort, _) = ReadCatalog(request);
        try
        {
            var page = RealmCatalogClient.ListAsync(apiPage, query, sort).GetAwaiter().GetResult();
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = true,
                Payload = JsonSerializer.SerializeToElement(page, JsonOptions),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return CatalogLoadFailed(request, ex);
        }
    }

    private static IpcEnvelope DownloadMod(PhotinoWindow window, IpcEnvelope request)
    {
        var modId = 0;
        if (request.Payload is { } payload
            && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("id", out var idEl)
            && idEl.TryGetInt32(out var parsed))
        {
            modId = parsed;
        }

        if (modId <= 0)
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Missing mod id.",
            };
        }

        return RunDownload(
            request,
            "maps",
            modId,
            token => ModDownloadClient.DownloadAsync(
                modId,
                progressKind: "maps",
                onProgress: progress => PushProgress(window, progress),
                cancellationToken: token));
    }

    private static IpcEnvelope Loadout(IpcEnvelope request)
    {
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(LoadoutStore.Load(), JsonOptions),
        };
    }

    private static IpcEnvelope ResetAll(IpcEnvelope request)
    {
        var (ok, error, result) = ModApplyService.TryResetAll(ReadBool(request, "deleteDownloads"));
        return Attempt(request, ok, error, result);
    }

    private static IpcEnvelope RankedSafe(IpcEnvelope request)
    {
        var (ok, error, result) = ModApplyService.TryRankedSafe();
        return Attempt(request, ok, error, result);
    }

    private static IpcEnvelope Reapply(IpcEnvelope request)
    {
        var (ok, error, result) = ModApplyService.TryReapply();
        return Attempt(request, ok, error, result);
    }

    private static IpcEnvelope ConfigsList(IpcEnvelope request)
    {
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(new { configs = NamedConfigStore.List() }, JsonOptions),
        };
    }

    private static IpcEnvelope ConfigsSave(IpcEnvelope request)
    {
        var name = ReadString(request, "name");
        try
        {
            var saved = NamedConfigStore.SaveCurrent(name);
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = true,
                Payload = JsonSerializer.SerializeToElement(saved, JsonOptions),
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = ex.Message,
            };
        }
    }

    private static IpcEnvelope ConfigsLoad(IpcEnvelope request)
    {
        var id = ReadString(request, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Missing config id.",
            };
        }

        try
        {
            NamedConfigStore.ApplyToCurrent(id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = ex.Message,
            };
        }

        var current = LoadoutStore.Load();
        if (current.Maps.Count == 0 && current.Music.Count == 0)
        {
            return Attempt(request, true, null, new ApplyAttemptDto(true, "Loaded. Nothing to apply."));
        }

        var (ok, error, result) = ModApplyService.TryReapply();
        return Attempt(request, ok, error, result);
    }

    private static IpcEnvelope ConfigsDelete(IpcEnvelope request)
    {
        var id = ReadString(request, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Missing config id.",
            };
        }

        try
        {
            NamedConfigStore.Delete(id);
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = true,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = ex.Message,
            };
        }
    }

    private static bool ReadBool(IpcEnvelope request, string property)
    {
        if (request.Payload is { } payload
            && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(property, out var value)
            && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
        {
            return value.GetBoolean();
        }

        return false;
    }

    private static string ReadString(IpcEnvelope request, string property)
    {
        if (request.Payload is { } payload
            && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(property, out var value))
        {
            return value.GetString() ?? "";
        }

        return "";
    }

    private static IReadOnlyList<int> ReadIntList(IpcEnvelope request, string property)
    {
        if (request.Payload is not { } payload
            || payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(property, out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = new List<int>();
        foreach (var el in list.EnumerateArray())
        {
            if (el.TryGetInt32(out var id) && id > 0)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static IpcEnvelope ApplyMod(IpcEnvelope request)
    {
        var modId = 0;
        if (request.Payload is { } payload
            && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("id", out var idEl)
            && idEl.TryGetInt32(out var parsed))
        {
            modId = parsed;
        }

        if (modId <= 0)
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Missing mod id.",
            };
        }

        var (ok, error, result) = ModApplyService.TryApply(modId);
        return Attempt(request, ok, error, result);
    }

    private static IpcEnvelope ResetMap(IpcEnvelope request)
    {
        var modId = ReadId(request);
        if (modId <= 0)
        {
            return MissingId(request);
        }

        var (ok, error, result) = ModApplyService.TryResetMap(modId);
        return Attempt(request, ok, error, result);
    }

    private static IpcEnvelope Attempt(
        IpcEnvelope request,
        bool ok,
        string? error,
        ApplyAttemptDto? result)
    {
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = ok,
            Error = error,
            Payload = result is null ? null : JsonSerializer.SerializeToElement(result, JsonOptions),
        };
    }
}
