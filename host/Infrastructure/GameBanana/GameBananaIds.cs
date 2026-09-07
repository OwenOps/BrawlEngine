namespace BrawlEngine.Host.Infrastructure.GameBanana;

public static class GameBananaIds
{
    public const int BrawlhallaGameId = 5704;
    public const int RealmsCategoryId = 6463;
    public const string RealmsCategoryName = "Realms";
    public const int SkinsCategoryId = 9845;
    public const string SkinsCategoryName = "Legend Skins";
    public const string SoundItemType = "Sound";

    /// <summary>Brawlhalla Sound root categories (GameBanana sounds/cats).</summary>
    public static readonly IReadOnlyList<(int Id, string Name)> SoundCategories =
    [
        (3539, "Announcer"),
        (3943, "Dubs"),
        (4242, "Main Theme"),
        (3578, "Music"),
        (3393, "Other/Misc"),
        (4108, "Podium Sounds"),
        (3750, "Signature"),
        (3587, "UI Sounds"),
        (3611, "Voicelines"),
        (3689, "Weapon Sounds"),
        (3558, "Win Theme"),
    ];

    public static bool IsSoundCategory(int categoryId)
    {
        foreach (var (id, _) in SoundCategories)
        {
            if (id == categoryId)
            {
                return true;
            }
        }

        return false;
    }
}
