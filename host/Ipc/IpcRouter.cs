using BrawlEngine.Host.Catalog;
using BrawlEngine.Host.Game;
using System.Text.Json;
using Photino.NET;

namespace BrawlEngine.Host.Ipc;

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

    private static IpcEnvelope CatalogMaps(IpcEnvelope request)
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

        try
        {
            var page = GameBananaClient.ListRealmsAsync(apiPage).GetAwaiter().GetResult();
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
}
