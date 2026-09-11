namespace BrawlEngine.Host.Infrastructure.Apply;

/// <summary>
/// GameBanana chips are filters. Copy-apply is filename match under audio\pc.
/// Map music banks (MUS_*) and GB Music / Win / Main Theme packs are not Apply this wave.
/// </summary>
public static class SoundApplyKind
{
    public static bool BlocksCatalogApply(string? category)
    {
        return category is "Music" or "Main Theme" or "Win Theme";
    }

    public static string CatalogBlockedMessage()
    {
        return "Win Theme, Main Theme, and Music cannot Apply yet (GameBanana MP3s / swapping MUS_Level banks). Use Announcer, weapons, UI, Podium, Dubs, Signature, Voicelines, or Other when filenames match vanilla.";
    }

    public static bool SkipFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        return name.StartsWith("MUS_", StringComparison.OrdinalIgnoreCase);
    }

    public static string? PreferSubfolder(string? category)
    {
        return category switch
        {
            "Announcer" or "Voicelines" or "Dubs" or "Signature" => "English(US)",
            _ => null,
        };
    }
}
