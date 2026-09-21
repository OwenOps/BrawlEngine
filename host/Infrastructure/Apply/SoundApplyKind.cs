namespace BrawlEngine.Host.Infrastructure.Apply;

/// <summary>
/// GameBanana chips are filters. Copy-apply is filename match under audio\pc.
/// MP3 / WAV / OGG without a vanilla name encode onto a picked .wem (ROADMAP6bis).
/// </summary>
public static class SoundApplyKind
{
    public static string? PreferSubfolder(string? category)
    {
        return category switch
        {
            "Announcer" or "Voicelines" or "Dubs" or "Signature" => "English(US)",
            _ => null,
        };
    }
}
