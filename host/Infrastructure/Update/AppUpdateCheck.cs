using System.Net.Http.Headers;
using System.Text.Json;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Networking;

namespace BrawlEngine.Host.Infrastructure.Update;

/// <summary>
/// Compares the running version to GitHub Releases. Fail quiet — a missing
/// release or a down API must not block launch.
/// </summary>
public static class AppUpdateCheck
{
    public const string RepoUrl = "https://github.com/OwenOps/BrawlEngine";

    private const string LatestApi = "https://api.github.com/repos/OwenOps/BrawlEngine/releases/latest";

    public static AppUpdateDto Check(string currentVersion)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = AppHttp.Shared.Send(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return None();
            }

            using var doc = JsonDocument.Parse(response.Content.ReadAsStream(cts.Token));
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            var url = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag) || !IsNewer(tag, currentVersion))
            {
                return None();
            }

            return new AppUpdateDto(true, tag.Trim(), string.IsNullOrWhiteSpace(url) ? RepoUrl : url);
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or TaskCanceledException
            or JsonException
            or IOException)
        {
            return None();
        }
    }

    private static AppUpdateDto None() => new(false);

    private static bool IsNewer(string latestTag, string currentVersion)
    {
        if (!TryParseVersion(latestTag, out var latest) || !TryParseVersion(currentVersion, out var current))
        {
            return false;
        }

        return latest > current;
    }

    private static bool TryParseVersion(string raw, out Version version)
    {
        var text = raw.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            text = text[..plus];
        }

        if (Version.TryParse(text, out var parsed) && parsed is not null)
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0);
        return false;
    }
}
