using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Apply;

/// <summary>
/// The IPC router sets Push for one Apply / Reset. Cards read that id's percent.
/// Custom audio replace uses <see cref="CustomReplaceId"/> (not a catalog mod).
/// </summary>
public static class ApplyProgress
{
    public const int CustomReplaceId = 2_147_483_647;

    public static Action<ApplyProgressDto>? Push { get; set; }

    public static string? Kind { get; private set; }

    public static int Id { get; private set; }

    public static string Action { get; private set; } = "apply";

    private static int Done;
    private static int Total = 1;
    private static string? LastNote;

    public static void Begin(string kind, int id, string action, int total)
    {
        Kind = kind;
        Id = id;
        Action = action;
        Done = 0;
        Total = Math.Max(1, total);
        LastNote = null;
        Report(0, Total);
    }

    public static bool Matches(string kind, int id, string action)
    {
        return Kind == kind && Id == id && Action == action;
    }

    public static void Note(string text)
    {
        if (Kind is null || Id <= 0)
        {
            return;
        }

        LastNote = text;
        Push?.Invoke(new ApplyProgressDto(Kind, Id, Action, Done, Total, text));
    }

    public static void Report(int done, int total)
    {
        if (Kind is null || Id <= 0)
        {
            return;
        }

        Total = Math.Max(1, total);
        Done = Math.Clamp(done, 0, Total);
        var name = LastNote ?? CatalogLibrary.TryRead(Kind, Id)?.Name;
        Push?.Invoke(new ApplyProgressDto(Kind, Id, Action, Done, Total, name));
    }

    public static void Clear()
    {
        Kind = null;
        Id = 0;
        Action = "apply";
        LastNote = null;
        Push = null;
    }
}
