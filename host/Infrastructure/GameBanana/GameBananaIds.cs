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

    /// <summary>Legend Skins subcategories (GameBanana mods/cats under 9845).</summary>
    public static readonly IReadOnlyList<(int Id, string Name)> SkinLegends =
    [
        (9856, "Ada"),
        (21055, "Arcadia"),
        (9877, "Artemis"),
        (9861, "Asuri"),
        (48399, "Aurus"),
        (9864, "Azoth"),
        (9862, "Barraza"),
        (9846, "Bödvar"),
        (9860, "Brynn"),
        (9878, "Caspian"),
        (9847, "Cassidy"),
        (22129, "Chel"),
        (9872, "Cross"),
        (9866, "Diana"),
        (9880, "Dusk"),
        (9863, "Ember"),
        (21053, "Ezio"),
        (9897, "Fait"),
        (9850, "Gnash"),
        (9852, "Hattori"),
        (15173, "Hugin"),
        (30616, "Imugi"),
        (9896, "Isaiah"),
        (9908, "Jaeyun"),
        (9867, "Jhala"),
        (9901, "Jiro"),
        (13189, "Kaya"),
        (32594, "King Zuva"),
        (9865, "Koji"),
        (9868, "Kor"),
        (41791, "Lady Vera"),
        (9906, "Lin Fei"),
        (25404, "Loki"),
        (9849, "Lord Vraxx"),
        (9858, "Lucien"),
        (9911, "Magyar"),
        (9909, "Mako"),
        (9873, "Mirage"),
        (9875, "Mordex"),
        (15172, "Munin"),
        (9874, "Nix"),
        (13190, "Onyx"),
        (9848, "Orion"),
        (9894, "Other/Misc"),
        (9899, "Petra"),
        (34975, "Priya"),
        (48476, "Qinghua"),
        (9851, "Queen Nai"),
        (9871, "Ragnir"),
        (38108, "Ransom"),
        (9902, "Rayman"),
        (23564, "Red Raptor"),
        (13193, "Reno"),
        (42619, "Rupture"),
        (9854, "Scarlet"),
        (9857, "Sentinel"),
        (26811, "Seven"),
        (13192, "Sidra"),
        (9853, "Sir Roland"),
        (9859, "Teros"),
        (21057, "Tezca"),
        (9855, "Thatch"),
        (22928, "Thea"),
        (13188, "Thor"),
        (13191, "Ulgrim"),
        (9870, "Val"),
        (9904, "Vector"),
        (28728, "Vivi"),
        (9912, "Volkov"),
        (9869, "Wu Shang"),
        (9879, "Xull"),
        (9876, "Yumiko"),
        (9895, "Zariel"),
    ];

    public static bool IsSkinLegend(int categoryId)
    {
        foreach (var (id, _) in SkinLegends)
        {
            if (id == categoryId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Numeric id from a GameBanana mods/sounds URL, or a plain id.</summary>
    public static int ParseItemId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        var text = raw.Trim();
        if (int.TryParse(text, out var plain) && plain > 0)
        {
            return plain;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return 0;
        }

        var host = uri.Host;
        if (!host.Equals("gamebanana.com", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("www.gamebanana.com", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return 0;
        }

        var bucket = parts[^2];
        if (!bucket.Equals("mods", StringComparison.OrdinalIgnoreCase)
            && !bucket.Equals("sounds", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return int.TryParse(parts[^1], out var id) && id > 0 ? id : 0;
    }
}
