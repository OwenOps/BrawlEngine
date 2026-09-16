using System.Diagnostics;
using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.Processes;

public static class BrawlhallaProcess
{
    public static ApplyGuardDto Check()
    {
        var running = Count() > 0;
        return new ApplyGuardDto(
            Allowed: !running,
            Running: running,
            Message: running
                ? "Brawlhalla is open. Apply waits until it closes."
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
