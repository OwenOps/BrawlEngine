using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Brawlhalla;

/// <summary>
/// DEBUG-only transcript for Stats name search. Release writes nothing.
/// File: %LocalAppData%\BrawlEngine\stats-debug.log
/// </summary>
public static class StatsDebugLog
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.Root, "stats-debug.log");

    public static string? PathOrNull()
    {
#if DEBUG
        return FilePath;
#else
        return null;
#endif
    }

    public static void Reset(string header)
    {
        WriteAll(header);
    }

    public static void Line(string line)
    {
        WriteAll(null, line);
    }

    public static string Snip(string text, int max = 400)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length <= max)
        {
            return text;
        }

        return text[..max] + "…";
    }

    private static void WriteAll(string? header, string? line = null)
    {
#if DEBUG
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                if (header is not null)
                {
                    File.WriteAllText(
                        FilePath,
                        DateTime.Now.ToString("HH:mm:ss.fff") + " " + header + Environment.NewLine);
                }

                if (line is not null)
                {
                    File.AppendAllText(
                        FilePath,
                        DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
#endif
    }
}
