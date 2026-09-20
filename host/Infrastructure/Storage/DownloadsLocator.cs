using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.Storage;

public static class DownloadsLocator
{
    public static DownloadsLocationDto Current()
    {
        var path = AppPaths.DownloadsRoot;
        Directory.CreateDirectory(path);
        return new DownloadsLocationDto(path);
    }

    public static DownloadsLocationDto PickFolder()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return PickFolderCore();
        }

        DownloadsLocationDto? result = null;
        var thread = new Thread(() => result = PickFolderCore());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result ?? Current() with { Cancelled = true };
    }

    public static bool IsUsingDefault()
    {
        var saved = AppSettings.Load().DownloadsPath?.Trim();
        if (string.IsNullOrWhiteSpace(saved))
        {
            return true;
        }

        try
        {
            return SamePath(saved, AppPaths.DefaultDownloadsRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static DownloadsLocationDto PickFolderCore()
    {
        var from = AppPaths.DownloadsRoot;

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the mods folder. Maps, audio, and skins are stored in Maps, sounds, and skins inside it.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = from,
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return Current() with { Cancelled = true };
        }

        var path = dialog.SelectedPath.Trim();
        if (path.Length == 0)
        {
            return new DownloadsLocationDto(from)
            {
                Error = "Pick a folder to store downloaded mods.",
            };
        }

        Directory.CreateDirectory(path);
        return ApplyRoot(path, clearSaved: SamePath(path, AppPaths.DefaultDownloadsRoot));
    }

    public static DownloadsLocationDto ResetToDefault()
    {
        return ApplyRoot(AppPaths.DefaultDownloadsRoot, clearSaved: true);
    }

    private static DownloadsLocationDto ApplyRoot(string path, bool clearSaved)
    {
        var from = AppPaths.DownloadsRoot;
        Directory.CreateDirectory(path);
        var moveError = RelocateInto(from, path);
        if (moveError is not null)
        {
            return new DownloadsLocationDto(from) { Error = moveError };
        }

        var settings = AppSettings.Load();
        settings.DownloadsPath = clearSaved ? null : path;
        settings.Save();
        AppPaths.RefreshDownloadLayout();
        return new DownloadsLocationDto(Path.GetFullPath(path));
    }

    private static string? RelocateInto(string from, string to)
    {
        string fromFull;
        string toFull;
        try
        {
            fromFull = Path.GetFullPath(from);
            toFull = Path.GetFullPath(to);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "That mods folder path is not valid.";
        }

        if (SamePath(fromFull, toFull) || !Directory.Exists(fromFull))
        {
            return null;
        }

        if (IsInside(toFull, fromFull) || IsInside(fromFull, toFull))
        {
            return "Pick a folder that is not inside the current mods folder.";
        }

        try
        {
            foreach (var source in Directory.EnumerateFiles(fromFull, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(fromFull, source);
                var dest = Path.Combine(toFull, relative);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                MoveOrCopy(source, dest);
            }

            foreach (var dir in Directory.GetDirectories(fromFull, "*", SearchOption.AllDirectories)
                .OrderByDescending(static dir => dir.Length))
            {
                TryDeleteDir(dir);
            }

            TryDeleteDir(fromFull);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Could not move the existing mods into that folder.";
        }

        return null;
    }

    private static void MoveOrCopy(string source, string dest)
    {
        if (File.Exists(dest))
        {
            File.Delete(dest);
        }

        try
        {
            File.Move(source, dest);
        }
        catch (IOException)
        {
            File.Copy(source, dest, overwrite: true);
            File.Delete(source);
        }
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsInside(string inner, string outer)
    {
        var prefix = outer.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return inner.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string a, string b)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
    }
}
