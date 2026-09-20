using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.Storage;

public static class DownloadImport
{
    public static DownloadImportDto Import(string kind, int id)
    {
        if (id <= 0)
        {
            return new DownloadImportDto(0, "", 0) { Error = "Need a GameBanana id." };
        }

        var files = PickFiles();
        if (files.Length == 0)
        {
            return new DownloadImportDto(id, DownloadInventory.KindFolder(kind), 0, Cancelled: true);
        }

        var folder = kind switch
        {
            "sounds" => AppPaths.SoundDownloadsFolder(id),
            "skins" => AppPaths.SkinDownloadsFolder(id),
            _ => AppPaths.DownloadsFolder(id),
        };

        Directory.CreateDirectory(folder);
        var copied = 0;
        foreach (var source in files)
        {
            var name = Path.GetFileName(source);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            File.Copy(source, Path.Combine(folder, name), overwrite: true);
            copied++;
        }

        return new DownloadImportDto(id, folder, copied);
    }

    private static string[] PickFiles()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return PickFilesCore();
        }

        string[]? result = null;
        var thread = new Thread(() => result = PickFilesCore());
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result ?? [];
    }

    private static string[] PickFilesCore()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose zip files downloaded from GameBanana",
            Filter = "Mod files (*.zip;*.rar;*.7z;*.bmod)|*.zip;*.rar;*.7z;*.bmod|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true,
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileNames : [];
    }
}
