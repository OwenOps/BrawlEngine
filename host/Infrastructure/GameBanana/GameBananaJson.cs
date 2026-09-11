using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace BrawlEngine.Host.Infrastructure.GameBanana;

/// <summary>
/// GameBanana sometimes returns empty, HTML, or a block page instead of JSON.
/// </summary>
public static class GameBananaJson
{
    public const string CatalogLoadError = "Could not load GameBanana. Retry.";

    public static async Task<JsonDocument> ReadDocumentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "";
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Fail(status, contentType, body.Length, "HTTP " + status);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            Fail(status, contentType, body.Length, "empty body");
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            Fail(status, contentType, body.Length, "not JSON");
        }

        return null!;
    }

    [DoesNotReturn]
    private static void Fail(int status, string contentType, int bodyLength, string reason)
    {
        Debug.WriteLine(
            "GameBanana: "
            + reason
            + " status="
            + status
            + " Content-Type="
            + contentType
            + " bytes="
            + bodyLength);
        throw new HttpRequestException(CatalogLoadError);
    }
}
