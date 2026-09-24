using System.Globalization;
using System.Net;
using System.Text.Json;
using BrawlEngine.Host.Infrastructure.Networking;

namespace BrawlEngine.Host.Infrastructure.Brawlhalla;

public static class BrawlhallaJson
{
    public const string LoadError = "Could not reach Brawlhalla. Retry.";
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(25);

    public static async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HttpTimeout);
        using var response = await AppHttp.Shared.GetAsync(url, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
        {
            throw new HttpRequestException(LoadError);
        }

        if (body[0] is not '{' and not '[')
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new HttpRequestException(LoadError);
        }
    }

    public static int? ReadInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var prop)
            || prop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var number))
        {
            return number;
        }

        if (prop.ValueKind == JsonValueKind.String
            && int.TryParse(prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static string ReadString(JsonElement el, string name)
    {
        return ReadStringOrNull(el, name) ?? "";
    }

    public static string? ReadStringOrNull(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = prop.GetString()?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
