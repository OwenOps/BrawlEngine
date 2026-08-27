using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BrawlEngine.Host.Game;

public static class BrawlhallaLocator
{
    public const string ExeName = "Brawlhalla.exe";
    public const string MapArtFolder = "mapArt";

    public static GameLocationDto Resolve()
    {
        var settings = AppSettings.Load();
        if (TryValidate(settings.GamePath, out var saved) && saved is not null)
        {
            return saved with { Source = "saved" };
        }

        foreach (var candidate in DefaultSteamCandidates())
        {
            if (TryValidate(candidate, out var found) && found is not null && found.Path is { } defaultPath)
            {
                Persist(defaultPath);
                return found with { Source = "default" };
            }
        }

        return new GameLocationDto(null, false, "none", false);
    }

    public static GameLocationDto PickFolder()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return PickFolderCore();
        }

        GameLocationDto? result = null;
        var thread = new Thread(() => result = PickFolderCore());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result ?? new GameLocationDto(null, false, "none", false);
    }

    private static GameLocationDto PickFolderCore()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the Brawlhalla folder (it contains Brawlhalla.exe)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return Resolve() with { Cancelled = true };
        }

        if (!TryValidate(dialog.SelectedPath, out var picked) || picked is null)
        {
            return new GameLocationDto(dialog.SelectedPath, false, "pick", false)
            {
                Error = "That folder is not Brawlhalla. Pick the folder that contains Brawlhalla.exe.",
            };
        }

        Persist(picked.Path!);
        return picked with { Source = "pick" };
    }

    public static bool TryValidate(string? path, out GameLocationDto? location)
    {
        location = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var full = Path.GetFullPath(path.Trim());
        if (!Directory.Exists(full))
        {
            return false;
        }

        var exe = Path.Combine(full, ExeName);
        if (!File.Exists(exe))
        {
            return false;
        }

        location = new GameLocationDto(
            full,
            true,
            "none",
            Directory.Exists(Path.Combine(full, MapArtFolder)));
        return true;
    }

    private static void Persist(string path)
    {
        var settings = AppSettings.Load();
        settings.GamePath = path;
        settings.Save();
    }

    private static IEnumerable<string> DefaultSteamCandidates()
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddSteamRoot(steamRoots, (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null));
        AddSteamRoot(steamRoots, (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "InstallPath", null));
        AddSteamRoot(steamRoots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        AddSteamRoot(steamRoots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));

        foreach (var root in steamRoots.ToArray())
        {
            foreach (var library in SteamLibraries(root))
            {
                steamRoots.Add(library);
            }
        }

        foreach (var root in steamRoots)
        {
            yield return Path.Combine(root, "steamapps", "common", "Brawlhalla");
        }
    }

    private static void AddSteamRoot(HashSet<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalized = path.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        if (Directory.Exists(normalized))
        {
            roots.Add(normalized);
        }
    }

    private static IEnumerable<string> SteamLibraries(string steamRoot)
    {
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
        {
            yield break;
        }

        string text;
        try
        {
            text = File.ReadAllText(vdf);
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
        {
            var library = match.Groups[1].Value.Replace(@"\\", @"\").Replace('/', Path.DirectorySeparatorChar);
            if (Directory.Exists(library))
            {
                yield return library;
            }
        }
    }
}

public sealed record GameLocationDto(
    string? Path,
    bool Found,
    string Source,
    bool HasMapArt)
{
    public bool Cancelled { get; init; }

    public string? Error { get; init; }
}
