using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Processes;
using BrawlEngine.Host.Infrastructure.Steam;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class ModApplyService
{
    public static (bool Ok, string? Error, ApplyAttemptDto? Result) TryPrepare(int modId)
    {
        var guard = BrawlhallaProcess.Check();
        if (guard.Running)
        {
            return (false, guard.Message ?? "Close Brawlhalla before applying or changing game files.", null);
        }

        var location = BrawlhallaLocator.Resolve();
        if (!location.Found)
        {
            return (false, "Brawlhalla folder not found. Choose the game folder.", null);
        }

        var folder = AppPaths.DownloadsFolder(modId);
        if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            return (false, "Download this mod first.", null);
        }

        return (true, null, new ApplyAttemptDto(false, "Extract is not implemented yet."));
    }
}
