using System.IO.Compression;
using BrawlEngine.Host.Infrastructure.Networking;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Ffdec;

/// <summary>
/// Official JPEXS "library only" zip (LGPLv3). Not the GPL GUI. Pinned so Apply does not chase nightlies.
/// </summary>
public static class FfdecLibFetch
{
    public const string Version = "26.2.1";
    private const string ZipUrl =
        "https://github.com/jindrapetrik/jpexs-decompiler/releases/download/version26.2.1/ffdec_lib_26.2.1.zip";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static void Ensure()
    {
        EnsureAsync().GetAwaiter().GetResult();
    }

    public static async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (FfdecLocator.HasJar())
        {
            return;
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (FfdecLocator.HasJar())
            {
                return;
            }

            Directory.CreateDirectory(AppPaths.Root);
            var zipPath = FfdecLocator.StoredJarPath + ".download.zip";
            try
            {
                using var response = await AppHttp.Shared
                    .GetAsync(ZipUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var output = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                ExtractJar(zipPath, FfdecLocator.StoredJarPath);
            }
            finally
            {
                TryDelete(zipPath);
            }

            if (!FfdecLocator.HasJar())
            {
                throw new InvalidOperationException("The JPEXS zip did not contain ffdec_lib.jar.");
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void ExtractJar(string zipPath, string destJar)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(item =>
            Path.GetFileName(item.FullName).Equals(FfdecLocator.JarFileName, StringComparison.OrdinalIgnoreCase)
            && item.Length > 0);
        if (entry is null)
        {
            throw new InvalidOperationException("The JPEXS zip did not contain ffdec_lib.jar.");
        }

        var destPart = destJar + ".part";
        TryDelete(destPart);
        entry.ExtractToFile(destPart, overwrite: true);
        TryDelete(destJar);
        File.Move(destPart, destJar);
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
