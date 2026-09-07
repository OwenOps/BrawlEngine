using BrawlEngine.Host.Infrastructure.Networking;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class Mp3UrlFetch
{
    public const long MaxBytes = 40 * 1024 * 1024;

    public static async Task<string> DownloadToTempAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Use an http or https link to an .mp3 file.");
        }

        if (IsBlockedHost(uri.Host))
        {
            throw new InvalidOperationException(
                "That site is not a direct file. Paste a link that ends in .mp3, not YouTube or similar.");
        }

        var folder = Path.Combine(AppPaths.Root, "tmp");
        Directory.CreateDirectory(folder);
        var dest = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".mp3");

        try
        {
            using var response = await AppHttp.Shared
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is { } length && length > MaxBytes)
            {
                throw new InvalidOperationException("That file is larger than 40 MB.");
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                           .ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxBytes)
                    {
                        throw new InvalidOperationException("That file is larger than 40 MB.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            if (!LooksLikeMp3(dest))
            {
                throw new InvalidOperationException(
                    "The download is not an MP3 (the page was probably HTML). Use a direct .mp3 link.");
            }

            return dest;
        }
        catch
        {
            TryDelete(dest);
            throw;
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private static bool IsBlockedHost(string host)
    {
        host = host.ToLowerInvariant();
        return host == "youtu.be"
            || host.EndsWith(".youtube.com", StringComparison.Ordinal)
            || host == "youtube.com"
            || host.EndsWith(".youtu.be", StringComparison.Ordinal)
            || host.Contains("soundcloud.com", StringComparison.Ordinal)
            || host.Contains("spotify.com", StringComparison.Ordinal)
            || host.Contains("tiktok.com", StringComparison.Ordinal);
    }

    private static bool LooksLikeMp3(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length < 3)
        {
            return false;
        }

        Span<byte> head = stackalloc byte[3];
        var read = stream.Read(head);
        if (read < 3)
        {
            return false;
        }

        if (head[0] == (byte)'I' && head[1] == (byte)'D' && head[2] == (byte)'3')
        {
            return true;
        }

        return head[0] == 0xFF && (head[1] & 0xE0) == 0xE0;
    }
}
