using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Apply;

/// <summary>
/// The IPC router sets Push for one Apply / Reset. Cards read that id's percent.
/// </summary>
public static class ApplyProgress
{
    public static Action<ApplyProgressDto>? Push { get; set; }

    public static string? Kind { get; private set; }

    public static int Id { get; private set; }

    public static string Action { get; private set; } = "apply";

    public static void Begin(string kind, int id, string action, int total)
    {
        Kind = kind;
        Id = id;
        Action = action;
        Report(0, total);
    }

    public static bool Matches(string kind, int id, string action)
    {
        return Kind == kind && Id == id && Action == action;
    }

    public static void Report(int done, int total)
    {
        if (Kind is null || Id <= 0)
        {
            return;
        }

        var safeTotal = Math.Max(1, total);
        var name = CatalogLibrary.TryRead(Kind, Id)?.Name;
        Push?.Invoke(
            new ApplyProgressDto(Kind, Id, Action, Math.Clamp(done, 0, safeTotal), safeTotal, name));
    }

    public static void Clear()
    {
        Kind = null;
        Id = 0;
        Action = "apply";
        Push = null;
    }
}
