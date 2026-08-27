using System.Diagnostics;

namespace BrawlEngine.Host.Game;

public static class BrawlhallaProcess
{
    public static ApplyGuardDto Check()
    {
        var running = Count() > 0;
        return new ApplyGuardDto(
            Allowed: !running,
            Running: running,
            Message: running
                ? "Close Brawlhalla before applying or changing game files."
                : null);
    }

    public static int Count()
    {
        try
        {
            return Process.GetProcessesByName("Brawlhalla").Length;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }
}

public sealed record ApplyGuardDto(bool Allowed, bool Running, string? Message);
