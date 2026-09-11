using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Steam;

public static class Mp3Locator
{
    public static Mp3LocationDto Resolve()
    {
        var settings = AppSettings.Load();
        if (TryValidate(settings.Mp3Path, out var saved) && saved is not null)
        {
            return saved with { Source = "saved" };
        }

        var game = BrawlhallaLocator.Resolve();
        if (game.Found && game.Path is not null)
        {
            var audioPc = BrawlhallaLocator.AudioPcPath(game.Path);
            if (TryValidate(audioPc, out var fromWem) && fromWem is not null)
            {
                return fromWem with { Source = "game" };
            }

            var nestedMp3 = Path.Combine(game.Path, BrawlhallaLocator.Mp3Folder);
            if (TryValidate(nestedMp3, out var fromMp3) && fromMp3 is not null)
            {
                return fromMp3 with { Source = "game" };
            }
        }

        return new Mp3LocationDto(null, false, "none");
    }

    public static Mp3LocationDto PickFolder()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return PickFolderCore();
        }

        Mp3LocationDto? result = null;
        var thread = new Thread(() => result = PickFolderCore());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result ?? new Mp3LocationDto(null, false, "none");
    }

    public static GameLocationDto AttachTo(GameLocationDto game)
    {
        var audio = Resolve();
        return game with
        {
            Mp3Path = audio.Path,
            HasMp3 = audio.Found,
            Mp3Source = audio.Source,
        };
    }

    public static bool TryValidate(string? path, out Mp3LocationDto? location)
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

        if (HasAudioFiles(full))
        {
            location = new Mp3LocationDto(full, true, "none");
            return true;
        }

        var audioPc = Path.Combine(full, "audio", "pc");
        if (HasAudioFiles(audioPc))
        {
            location = new Mp3LocationDto(audioPc, true, "none");
            return true;
        }

        var nestedMp3 = Path.Combine(full, BrawlhallaLocator.Mp3Folder);
        if (HasAudioFiles(nestedMp3))
        {
            location = new Mp3LocationDto(nestedMp3, true, "none");
            return true;
        }

        return false;
    }

    private static Mp3LocationDto PickFolderCore()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select audio\\pc (the folder with .wem files) or the Brawlhalla folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return Resolve() with { Cancelled = true };
        }

        if (!TryValidate(dialog.SelectedPath, out var picked) || picked is null)
        {
            return new Mp3LocationDto(dialog.SelectedPath, false, "pick")
            {
                Error = "No .wem, .bnk, or .mp3 files there. Pick audio\\pc, or the Brawlhalla folder.",
            };
        }

        Persist(picked.Path!);
        return picked with { Source = "pick" };
    }

    private static void Persist(string audioFolder)
    {
        var settings = AppSettings.Load();
        var game = BrawlhallaLocator.Resolve();
        string? auto = null;
        if (game.Found && game.Path is not null)
        {
            auto = Path.GetFullPath(BrawlhallaLocator.AudioPcPath(game.Path));
            if (!Directory.Exists(auto))
            {
                auto = Path.GetFullPath(Path.Combine(game.Path, BrawlhallaLocator.Mp3Folder));
            }
        }

        if (auto is not null
            && string.Equals(audioFolder, auto, StringComparison.OrdinalIgnoreCase))
        {
            settings.Mp3Path = null;
        }
        else
        {
            settings.Mp3Path = audioFolder;
        }

        settings.Save();
    }

    private static bool HasAudioFiles(string folder)
    {
        return Directory.Exists(folder)
            && (Directory.EnumerateFiles(folder, "*.wem", SearchOption.AllDirectories).Any()
                || Directory.EnumerateFiles(folder, "*.bnk", SearchOption.AllDirectories).Any()
                || Directory.EnumerateFiles(folder, "*.mp3", SearchOption.AllDirectories).Any());
    }
}
