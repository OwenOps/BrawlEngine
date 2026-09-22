using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using BrawlEngine.Host.Infrastructure.Apply;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Wwise;

/// <summary>
/// ffmpeg (decode) is fetched like ffdec_lib. wav2wem (GPLv2, https://github.com/pas2k/wav2wem)
/// ships next to the app as a separate exe — do not link it.
/// </summary>
public static class WwiseToolFetch
{
    private const string FfmpegZipUrl =
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly HttpClient Http = CreateHttp();

    public static void Ensure()
    {
        EnsureAsync().GetAwaiter().GetResult();
    }

    public static async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        EnsureBundledWav2wem();
        if (WwiseToolLocator.HasFfmpeg() && WwiseToolLocator.HasWav2wem())
        {
            return;
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureBundledWav2wem();
            if (!WwiseToolLocator.HasFfmpeg())
            {
                ApplyProgress.Note("Downloading ffmpeg (first convert, ~80 MB)…");
                await DownloadFfmpegAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!WwiseToolLocator.HasWav2wem())
            {
                throw new InvalidOperationException(
                    "wav2wem.exe is missing. Reinstall BrawlEngine (the encoder ships next to the app).");
            }

            if (!WwiseToolLocator.HasFfmpeg())
            {
                throw new InvalidOperationException("The ffmpeg zip did not contain ffmpeg.exe.");
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void EnsureBundledWav2wem()
    {
        if (WwiseToolLocator.HasWav2wem())
        {
            return;
        }

        var bundled = WwiseToolLocator.BundledWav2wemPath;
        if (!File.Exists(bundled))
        {
            return;
        }

        Directory.CreateDirectory(WwiseToolLocator.ToolsFolder);
        File.Copy(bundled, WwiseToolLocator.StoredWav2wemPath, overwrite: true);
    }

    private static async Task DownloadFfmpegAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(WwiseToolLocator.ToolsFolder);
        var zipPath = WwiseToolLocator.StoredFfmpegPath + ".download.zip";
        try
        {
            using var response = await Http
                .GetAsync(FfmpegZipUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[65536];
                long total = 0;
                var lastNote = Stopwatch.StartNew();
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                           .ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    total += read;
                    if (lastNote.ElapsedMilliseconds < 250 && total < (length ?? long.MaxValue))
                    {
                        continue;
                    }

                    lastNote.Restart();
                    ApplyProgress.Note(FfmpegDownloadNote(total, length));
                    if (length is > 0)
                    {
                        ApplyProgress.Report((int)Math.Min(99, total * 100 / length.Value), 100);
                    }
                }
            }

            ApplyProgress.Note("Extracting ffmpeg…");
            ExtractFfmpeg(zipPath, WwiseToolLocator.StoredFfmpegPath);
        }
        finally
        {
            TryDelete(zipPath);
        }
    }

    private static string FfmpegDownloadNote(long got, long? length)
    {
        if (length is > 0)
        {
            return "Downloading ffmpeg… " + Mb(got) + " / " + Mb(length.Value);
        }

        return "Downloading ffmpeg… " + Mb(got);
    }

    private static string Mb(long bytes)
    {
        return (bytes / (1024d * 1024d)).ToString("0.0") + " MB";
    }

    private static void ExtractFfmpeg(string zipPath, string destExe)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(item =>
            Path.GetFileName(item.FullName).Equals(WwiseToolLocator.FfmpegFileName, StringComparison.OrdinalIgnoreCase)
            && item.Length > 1_000_000);
        if (entry is null)
        {
            throw new InvalidOperationException("The ffmpeg zip did not contain ffmpeg.exe.");
        }

        var destPart = destExe + ".part";
        TryDelete(destPart);
        entry.ExtractToFile(destPart, overwrite: true);
        TryDelete(destExe);
        File.Move(destPart, destExe);
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BrawlEngine", "0.6"));
        return http;
    }

    private static void TryDelete(string path)
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
        catch (UnauthorizedAccessException)
        {
        }
    }
}
