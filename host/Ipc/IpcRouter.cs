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
            Payload = JsonSerializer.SerializeToElement(new { app = "BrawlEngine" }),
        };
    }
}
