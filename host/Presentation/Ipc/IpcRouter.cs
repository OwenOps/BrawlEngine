using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Apply;
using BrawlEngine.Host.Infrastructure.Ffdec;
using BrawlEngine.Host.Infrastructure.GameBanana;
using BrawlEngine.Host.Infrastructure.Processes;
using BrawlEngine.Host.Infrastructure.Steam;
using BrawlEngine.Host.Infrastructure.Storage;
using BrawlEngine.Host.Infrastructure.Update;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Photino.NET;

namespace BrawlEngine.Host.Presentation.Ipc;

public static class IpcRouter
{
    private const string MadeBy = "Owen";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly object ApplyLock = new();

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
            "app.update" => AppUpdate(request),
            "game.get" => GameLocation(request, Mp3Locator.AttachTo(BrawlhallaLocator.Resolve())),
            "game.pick" => GameLocation(request, Mp3Locator.AttachTo(BrawlhallaLocator.PickFolder())),
            "music.pick" => MusicPick(request),
            "game.running" => ApplyGuard(request),
            "catalog.maps" => CatalogMaps(request),
            "catalog.sounds" => CatalogSounds(request),
            "catalog.skins" => CatalogSkins(window, request),
            "catalog.byIds" => CatalogByIds(request),
            "browser.open" => OpenUrl(request),
            "folder.open" => OpenFolder(request),
            "mod.download" => DownloadMod(window, request),
            "sound.download" => DownloadSound(window, request),
            "skin.download" => DownloadSkin(window, request),
            "skins.tools" => SkinsTools(request),
            "skins.tools.pick" => SkinsToolsPick(request),
            "skin.apply" => WhenGameClosed(window, request, () => ApplySkin(request)),
            "skin.reset" => WhenGameClosed(window, request, () => ResetSkin(request)),
            "downloads.cancel" => CancelDownload(request),
            "downloads.list" => ListDownloads(request),
            "downloads.summary" => SummaryDownloads(request),
            "downloads.delete" => DeleteDownload(request),
            "downloads.open" => OpenDownloads(request),
            "downloads.pick" => PickDownloads(request),
            "downloads.reset" => ResetDownloads(request),
            "downloads.import" => ImportDownload(request),
            "mod.mapNames" => MapNames(request),
            "mod.apply" => WhenGameClosed(window, request, () => ApplyMod(request)),
            "mod.reset" => WhenGameClosed(window, request, () => ResetMap(request)),
            "music.apply" => WhenGameClosed(window, request, () => ApplySound(request)),
            "music.reset" => WhenGameClosed(window, request, () => ResetSound(request)),
            "music.tracks" => MusicTracks(request),
            "music.replace" => ReplaceTrack(window, request),
            "music.restore" => WhenGameClosed(window, request, () => RestoreTrack(request)),
            "music.restoreAll" => WhenGameClosed(window, request, () => RestoreAllAudio(request)),
            "loadout.get" => Loadout(request),
            "likes.get" => LikesGet(request),
            "likes.toggle" => LikesToggle(request),
            "crashes.get" => CrashesGet(request),
            "crashes.toggle" => CrashesToggle(request),
            "mods.resetAll" => WhenGameClosed(window, request, () => ResetAll(request)),
            "mods.rankedSafe" => WhenGameClosed(window, request, () => RankedSafe(request)),
            "mods.reapply" => WhenGameClosed(window, request, () => Reapply(request)),
            "configs.list" => ConfigsList(request),
            "configs.save" => ConfigsSave(request),
            "configs.load" => WhenGameClosed(window, request, () => ConfigsLoad(request)),
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
            Payload = JsonSerializer.SerializeToElement(new AppInfoDto(MadeBy, AppVersion()), JsonOptions),
        };
    }

    private static IpcEnvelope AppUpdate(IpcEnvelope request)
    {
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(AppUpdateCheck.Check(AppVersion()), JsonOptions),
        };
    }

    private static string AppVersion()
    {
        var raw = typeof(IpcRouter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? typeof(IpcRouter).Assembly.GetName().Version?.ToString(3)
            ?? "";
        var plus = raw.IndexOf('+');
        return plus >= 0 ? raw[..plus] : raw;
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

    private static IpcEnvelope SkinsTools(IpcEnvelope request)
    {
        try
        {
            FfdecLibFetch.Ensure();
        }
        catch (Exception ex) when (
            ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException
            or UnauthorizedAccessException)
        {
            var failed = FfdecLocator.Resolve();
            var message = "Could not download ffdec_lib.jar. Check your connection and Retry.";
            if (string.IsNullOrEmpty(failed.JavaPath) && string.IsNullOrEmpty(failed.JarPath))
            {
                message = failed.Error ?? message;
            }

            return ToolsReply(request, failed with { Error = message });
        }

        return ToolsReply(request, FfdecLocator.Resolve());
    }

    private static IpcEnvelope SkinsToolsPick(IpcEnvelope request)
    {
        return ToolsReply(request, FfdecLocator.Pick(ReadString(request, "kind")));
    }

    private static IpcEnvelope ToolsReply(IpcEnvelope request, FfdecToolsDto tools)
    {
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(tools, JsonOptions),
        };
    }

    private static IpcEnvelope ApplyGuard(IpcEnvelope request)
    {
        var status = BrawlhallaProcess.Check();
        var pending = PendingGameWrites.Count;
        string? message = status.Message;
        if (status.Running && pending > 0)
        {
            message = pending == 1
                ? "1 change will apply when Brawlhalla closes."
                : pending + " changes will apply when Brawlhalla closes.";
        }
        else if (status.Running)
        {
            message = "Brawlhalla is open. Apply waits until it closes.";
        }

        var payload = status with { Message = message, Pending = pending };
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(payload, JsonOptions),
        };
    }

    private static IpcEnvelope CatalogSounds(IpcEnvelope request)
    {
        var (apiPage, query, sort, categoryId, authorId) = ReadCatalog(request);
        try
        {
            var page = SoundCatalogClient.ListAsync(apiPage, query, sort, categoryId, authorId).GetAwaiter().GetResult();
            RememberCatalog("sounds", page.Items);
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

    private static IpcEnvelope CatalogSkins(PhotinoWindow window, IpcEnvelope request)
    {
        var (apiPage, query, sort, categoryId, authorId) = ReadCatalog(request);
        try
        {
            var page = SkinCatalogClient.ListAsync(apiPage, query, sort, categoryId, authorId).GetAwaiter().GetResult();
            page = SkinTarget.ApplyCached(page);
            RememberCatalog("skins", page.Items);
            _ = Task.Run(() => PushSkinTargets(window, page.Items));
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

    private static void PushSkinTargets(PhotinoWindow window, IReadOnlyList<CatalogItemDto> items)
    {
        IReadOnlyList<CatalogItemDto> filled = items;
        try
        {
            filled = SkinTarget.AttachAsync(items, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // Cards are already on screen; Needs/Default can stay blank.
        }

        RememberCatalog("skins", filled);
        var rows = filled
            .Select(item => new SkinTargetRowDto(item.Id, item.SkinTarget, item.Description))
            .ToList();
        var envelope = new IpcEnvelope
        {
            Id = "",
            Type = "catalog.skinTargets",
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(new { items = rows }, JsonOptions),
        };
        window.SendWebMessage(JsonSerializer.Serialize(envelope, JsonOptions));
    }

    private static IpcEnvelope CatalogByIds(IpcEnvelope request)
    {
        var kind = ReadString(request, "kind");
        var itemType = kind == "sounds" ? GameBananaIds.SoundItemType : "Mod";
        var defaultCategory = kind == "sounds"
            ? "Sounds"
            : kind == "skins"
                ? GameBananaIds.SkinsCategoryName
                : GameBananaIds.RealmsCategoryName;
        var ids = ReadIntList(request, "ids");
        try
        {
            var items = CatalogProfileClient
                .GetAsync(kind, itemType, ids, defaultCategory)
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

    private static IpcEnvelope SummaryDownloads(IpcEnvelope request)
    {
        var summary = DownloadInventory.Summary();
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(summary, JsonOptions),
        };
    }

    private static IpcEnvelope PickDownloads(IpcEnvelope request)
    {
        var location = DownloadsLocator.PickFolder();
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

    private static IpcEnvelope ResetDownloads(IpcEnvelope request)
    {
        var location = DownloadsLocator.ResetToDefault();
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

    private static IpcEnvelope ImportDownload(IpcEnvelope request)
    {
        var kind = ReadString(request, "kind");
        if (kind != "maps" && kind != "sounds" && kind != "skins")
        {
            kind = "maps";
        }

        var id = ReadId(request);
        if (id <= 0)
        {
            id = GameBananaIds.ParseItemId(ReadString(request, "url"));
        }

        if (id <= 0)
        {
            return MissingId(request);
        }

        try
        {
            var result = DownloadImport.Import(kind, id);
            if (result.Error is not null)
            {
                return new IpcEnvelope
                {
                    Id = request.Id,
                    Type = request.Type,
                    Ok = false,
                    Error = result.Error,
                };
            }

            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = true,
                Payload = JsonSerializer.SerializeToElement(result, JsonOptions),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Could not copy those files into the mods folder.",
            };
        }
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

        var target = ReadString(request, "target");
        var (ok, error, result) = ModApplyService.TryApplySound(
            id,
            category: ReadString(request, "category"),
            targetFileName: string.IsNullOrWhiteSpace(target) ? null : target);
        return Attempt(request, ok, error, result);
    }

    private static IpcEnvelope ApplySkin(IpcEnvelope request)
    {
        var id = ReadId(request);
        if (id <= 0)
        {
            return MissingId(request);
        }

        var (ok, error, result) = ModApplyService.TryApplySkin(id);
        return Attempt(request, ok, error, result);
    }

    private static IpcEnvelope ResetSkin(IpcEnvelope request)
    {
        var id = ReadId(request);
        if (id <= 0)
        {
            return MissingId(request);
        }

        var (ok, error, result) = ModApplyService.TryResetSkin(id);
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

        var listed = Mp3Applier.ListTracks(mp3.Path);
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(listed, JsonOptions),
        };
    }

    private static IpcEnvelope ReplaceTrack(PhotinoWindow window, IpcEnvelope request)
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

        string? picked = null;
        var downloaded = false;
        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                picked = Mp3UrlFetch.DownloadToTempAsync(url).GetAwaiter().GetResult();
                downloaded = true;
            }
            catch (Exception ex) when (
                ex is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException)
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
        else
        {
            picked = Mp3FilePicker.PickAudio(target);
        }

        if (string.IsNullOrEmpty(picked))
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "No audio file selected.",
            };
        }

        var sourceFile = picked;
        var sourceUrl = url;
        return WhenGameClosed(window, request, () =>
        {
            try
            {
                var (ok, error, result) = ModApplyService.TryReplaceTrack(target, sourceUrl, sourceFile);
                return Attempt(request, ok, error, result);
            }
            finally
            {
                if (downloaded)
                {
                    Mp3UrlFetch.TryDelete(sourceFile);
                }
            }
        });
    }

    private static IpcEnvelope RestoreTrack(IpcEnvelope request)
    {
        var target = "";
        if (request.Payload is { } payload
            && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("target", out var targetEl))
        {
            target = targetEl.GetString() ?? "";
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Choose which in-game theme to restore.",
            };
        }

        var (ok, error, result) = ModApplyService.TryRestoreTrack(target);
        return Attempt(request, ok, error, result);
    }

    private static IpcEnvelope RestoreAllAudio(IpcEnvelope request)
    {
        var (ok, error, result) = ModApplyService.TryRestoreAllAudio();
        return Attempt(request, ok, error, result);
    }

    private static (int Page, string Query, string Sort, int CategoryId, int AuthorId) ReadCatalog(IpcEnvelope request)
    {
        var apiPage = 1;
        var query = "";
        var sort = "newest";
        var categoryId = 0;
        var authorId = 0;
        if (request.Payload is not { } payload || payload.ValueKind != JsonValueKind.Object)
        {
            return (apiPage, query, sort, categoryId, authorId);
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

        if (payload.TryGetProperty("authorId", out var authorEl)
            && authorEl.TryGetInt32(out var parsedAuthor)
            && parsedAuthor > 0)
        {
            authorId = parsedAuthor;
        }

        return (apiPage, query, sort, categoryId, authorId);
    }

    private static IpcEnvelope OpenUrl(IpcEnvelope request)
    {
        var url = ReadString(request, "url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !IsAllowedBrowserUrl(uri))
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Only https GameBanana, Google Images, or the BrawlEngine GitHub repo can be opened.",
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

        if (!IsInsideAppFolder(full, AppPaths.Root)
            && !IsInsideAppFolder(full, AppPaths.DownloadsRoot))
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Can only open folders inside BrawlEngine data or the mods folder.",
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

    private static bool IsInsideAppFolder(string full, string root)
    {
        string rootFull;
        try
        {
            rootFull = Path.GetFullPath(root);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }

        var prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase);
    }

    private static IpcEnvelope WhenGameClosed(PhotinoWindow window, IpcEnvelope request, Func<IpcEnvelope> run)
    {
        if (!BrawlhallaProcess.Check().Running)
        {
            return WithApplyProgress(window, run);
        }

        PendingGameWrites.Enqueue(window, () => WithApplyProgress(window, run));
        return Attempt(request, true, null, new ApplyAttemptDto(false, PendingGameWrites.QueuedReason, Queued: true));
    }

    private static IpcEnvelope WithApplyProgress(PhotinoWindow window, Func<IpcEnvelope> run)
    {
        lock (ApplyLock)
        {
            ApplyProgress.Push = progress => PushApplyProgress(window, progress);
            try
            {
                return run();
            }
            finally
            {
                ApplyProgress.Clear();
                PushApplyDone(window);
            }
        }
    }

    private static void PushApplyDone(PhotinoWindow window)
    {
        var envelope = new IpcEnvelope
        {
            Id = "",
            Type = "apply.done",
            Ok = true,
        };
        window.SendWebMessage(JsonSerializer.Serialize(envelope, JsonOptions));
    }

    private static void PushApplyProgress(PhotinoWindow window, ApplyProgressDto progress)
    {
        var envelope = new IpcEnvelope
        {
            Id = "",
            Type = "apply.progress",
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(progress, JsonOptions),
        };
        window.SendWebMessage(JsonSerializer.Serialize(envelope, JsonOptions));
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

    private static bool IsAllowedBrowserUrl(Uri uri)
    {
        if (IsGameBananaHost(uri.Host))
        {
            return true;
        }

        if (IsSourceRepo(uri))
        {
            return true;
        }

        var google = uri.Host.Equals("google.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("www.google.com", StringComparison.OrdinalIgnoreCase);
        if (!google)
        {
            return false;
        }

        return uri.AbsolutePath.Equals("/search", StringComparison.OrdinalIgnoreCase)
            && uri.Query.Contains("tbm=isch", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSourceRepo(Uri uri)
    {
        var github = uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase);
        if (!github)
        {
            return false;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4];
        }

        return path.Equals("/OwenOps/BrawlEngine", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/OwenOps/BrawlEngine/", StringComparison.OrdinalIgnoreCase);
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

    private static void RememberCatalog(string kind, IReadOnlyList<CatalogItemDto> items)
    {
        foreach (var item in items)
        {
            CatalogLibrary.Remember(kind, item);
        }
    }

    private static IpcEnvelope CatalogMaps(IpcEnvelope request)
    {
        var (apiPage, query, sort, _, authorId) = ReadCatalog(request);
        try
        {
            var page = RealmCatalogClient.ListAsync(apiPage, query, sort, authorId).GetAwaiter().GetResult();
            RememberCatalog("maps", page.Items);
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

    private static IpcEnvelope LikesGet(IpcEnvelope request)
    {
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(LikeStore.Load(), JsonOptions),
        };
    }

    private static IpcEnvelope LikesToggle(IpcEnvelope request)
    {
        var kind = ReadString(request, "kind");
        var id = ReadId(request);
        if (id <= 0 || kind is not ("maps" or "sounds" or "skins"))
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Missing like target.",
            };
        }

        var (likes, liked) = LikeStore.Toggle(kind, id);
        if (liked
            && request.Payload is { } payload
            && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("item", out var itemEl))
        {
            try
            {
                var item = itemEl.Deserialize<CatalogItemDto>(JsonOptions);
                if (item is not null)
                {
                    CatalogLibrary.RememberCard(kind, item);
                }
            }
            catch (JsonException)
            {
            }
        }

        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(likes, JsonOptions),
        };
    }

    private static IpcEnvelope CrashesGet(IpcEnvelope request)
    {
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(CrashStore.Load(), JsonOptions),
        };
    }

    private static IpcEnvelope CrashesToggle(IpcEnvelope request)
    {
        var kind = ReadString(request, "kind");
        var id = ReadId(request);
        if (id <= 0 || kind is not ("maps" or "sounds" or "skins"))
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Missing crash tag target.",
            };
        }

        var note = ReadString(request, "note");
        var crashes = CrashStore.Toggle(kind, id, string.IsNullOrWhiteSpace(note) ? null : note);
        return new IpcEnvelope
        {
            Id = request.Id,
            Type = request.Type,
            Ok = true,
            Payload = JsonSerializer.SerializeToElement(crashes, JsonOptions),
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
