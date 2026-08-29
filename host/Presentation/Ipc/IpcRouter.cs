using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Apply;
using BrawlEngine.Host.Infrastructure.GameBanana;
using BrawlEngine.Host.Infrastructure.Processes;
using BrawlEngine.Host.Infrastructure.Steam;
using BrawlEngine.Host.Infrastructure.Storage;
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
            "game.get" => GameLocation(request, BrawlhallaLocator.Resolve()),
            "game.pick" => GameLocation(request, BrawlhallaLocator.PickFolder()),
            "game.running" => ApplyGuard(request),
            "catalog.maps" => CatalogMaps(request),
            "catalog.sounds" => CatalogSounds(request),
            "mod.download" => DownloadMod(request),
            "sound.download" => DownloadSound(request),
            "mod.apply" => ApplyMod(request),
            "music.apply" => ApplySound(request),
            "music.tracks" => MusicTracks(request),
            "music.replace" => ReplaceTrack(request),
            "loadout.get" => Loadout(request),
            "mods.resetAll" => ResetAll(request),
            "mods.reapply" => Reapply(request),
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
        var apiPage = ReadPage(request);
        try
        {
            var page = SoundCatalogClient.ListAsync(apiPage).GetAwaiter().GetResult();
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
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Could not load GameBanana: " + ex.Message,
            };
        }
    }

    private static IpcEnvelope DownloadSound(IpcEnvelope request)
    {
        var id = ReadId(request);
        if (id <= 0)
        {
            return MissingId(request);
        }

        try
        {
            var result = ModDownloadClient.DownloadAsync(id, GameBananaIds.SoundItemType).GetAwaiter().GetResult();
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = true,
                Payload = JsonSerializer.SerializeToElement(result, JsonOptions),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException or IOException)
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

    private static IpcEnvelope ApplySound(IpcEnvelope request)
    {
        var id = ReadId(request);
        if (id <= 0)
        {
            return MissingId(request);
        }

        var (ok, error, result) = ModApplyService.TryApplySound(id);
        return Attempt(request, ok, error, result);
    }

    private static IpcEnvelope MusicTracks(IpcEnvelope request)
    {
        var location = BrawlhallaLocator.Resolve();
        if (!location.Found || location.Path is null)
        {
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Brawlhalla folder not found. Choose the game folder.",
            };
        }

        var tracks = Mp3Applier.ListTracks(location.Path);
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
                Error = "Choose which in-game track to replace.",
            };
        }

        var (ok, error, result) = ModApplyService.TryReplaceTrack(target);
        return Attempt(request, ok, error, result);
    }

    private static int ReadPage(IpcEnvelope request)
    {
        var apiPage = 1;
        if (request.Payload is { } payload
            && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("page", out var pageEl)
            && pageEl.TryGetInt32(out var parsed)
            && parsed > 0)
        {
            apiPage = parsed;
        }

        return apiPage;
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
        var apiPage = ReadPage(request);
        try
        {
            var page = RealmCatalogClient.ListAsync(apiPage).GetAwaiter().GetResult();
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
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = false,
                Error = "Could not load GameBanana: " + ex.Message,
            };
        }
    }

    private static IpcEnvelope DownloadMod(IpcEnvelope request)
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

        try
        {
            var result = ModDownloadClient.DownloadAsync(modId).GetAwaiter().GetResult();
            return new IpcEnvelope
            {
                Id = request.Id,
                Type = request.Type,
                Ok = true,
                Payload = JsonSerializer.SerializeToElement(result, JsonOptions),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException or IOException)
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
        var (ok, error, result) = ModApplyService.TryResetAll();
        return Attempt(request, ok, error, result);
    }

    private static IpcEnvelope Reapply(IpcEnvelope request)
    {
        var (ok, error, result) = ModApplyService.TryReapply();
        return Attempt(request, ok, error, result);
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
