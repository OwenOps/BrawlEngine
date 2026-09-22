using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Wwise;

public static class WwiseToolLocator
{
    public const string FfmpegFileName = "ffmpeg.exe";
    public const string Wav2wemFileName = "wav2wem.exe";

    public static string ToolsFolder => Path.Combine(AppPaths.Root, "tools");

    public static string StoredFfmpegPath => Path.Combine(ToolsFolder, FfmpegFileName);

    public static string StoredWav2wemPath => Path.Combine(ToolsFolder, Wav2wemFileName);

    public static string BundledWav2wemPath =>
        Path.Combine(AppContext.BaseDirectory, "tools", Wav2wemFileName);

    public static string? ResolveFfmpeg()
    {
        if (IsExe(StoredFfmpegPath, FfmpegFileName))
        {
            return StoredFfmpegPath;
        }

        var nextToApp = Path.Combine(AppContext.BaseDirectory, "tools", FfmpegFileName);
        return IsExe(nextToApp, FfmpegFileName) ? nextToApp : null;
    }

    public static string? ResolveWav2wem()
    {
        if (IsExe(StoredWav2wemPath, Wav2wemFileName))
        {
            return StoredWav2wemPath;
        }

        return IsExe(BundledWav2wemPath, Wav2wemFileName) ? BundledWav2wemPath : null;
    }

    public static bool HasFfmpeg() => ResolveFfmpeg() is not null;

    public static bool HasWav2wem() => ResolveWav2wem() is not null;

    private static bool IsExe(string path, string fileName)
    {
        return File.Exists(path)
            && Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase)
            && new FileInfo(path).Length > 1024;
    }
}
