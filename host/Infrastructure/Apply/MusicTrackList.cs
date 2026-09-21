using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class MusicTrackList
{
    public static IReadOnlyList<MusicTrackDto> List(string audioFolder)
    {
        if (!Directory.Exists(audioFolder))
        {
            return [];
        }

        var mp3 = Directory.GetFiles(audioFolder, "*.mp3");
        if (mp3.Length > 0)
        {
            return mp3
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => new MusicTrackDto(name, name, SlotForLegacyMp3(name)))
                .ToList();
        }

        var bankById = MapWemIdsToMusBank(audioFolder);
        var tracks = new List<MusicTrackDto>();
        foreach (var bnk in Directory.GetFiles(audioFolder, "MUS_*.bnk"))
        {
            IReadOnlyList<WwiseDidx.MediaIndex> entries;
            try
            {
                entries = WwiseDidx.ReadEntries(bnk);
            }
            catch (IOException)
            {
                continue;
            }

            if (entries.Count == 0)
            {
                continue;
            }

            var bankName = Path.GetFileName(bnk);
            WwiseDidx.MediaIndex longest = entries[0];
            foreach (var entry in entries)
            {
                if (entry.Size > longest.Size)
                {
                    longest = entry;
                }
            }

            var fileName = longest.Id + ".wem";
            tracks.Add(new MusicTrackDto(fileName, HumanBankName(bankName), SlotForMusBank(bankName)));
        }

        foreach (var file in Directory.GetFiles(audioFolder, "*.wem"))
        {
            var name = Path.GetFileName(file);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var idText = Path.GetFileNameWithoutExtension(name);
            if (!uint.TryParse(idText, out var id))
            {
                tracks.Add(new MusicTrackDto(name, name, "other"));
                continue;
            }

            if (bankById.TryGetValue(id, out _))
            {
                continue;
            }

            tracks.Add(new MusicTrackDto(name, name, "other"));
        }

        return tracks
            .OrderBy(track => track.Slot, StringComparer.OrdinalIgnoreCase)
            .ThenBy(track => track.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Old mp3 folder names (BrawlhallaMenu.mp3, …) → a numbered .wem in that slot.</summary>
    public static string? MapLegacyFileName(string audioFolder, string sourceFileName)
    {
        var slot = SlotForLegacyMp3(sourceFileName);
        if (slot == "other")
        {
            return null;
        }

        var tracks = List(audioFolder)
            .Where(track =>
                track.Slot == slot
                && track.FileName.EndsWith(".wem", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (tracks.Count == 0)
        {
            return null;
        }

        if (slot == "menu")
        {
            var menuOnly = tracks
                .Where(track => track.Label.StartsWith("Menu (main)", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (menuOnly.Count > 0)
            {
                tracks = menuOnly;
            }
        }

        string? best = null;
        long bestSize = -1;
        foreach (var track in tracks)
        {
            var path = Path.Combine(audioFolder, track.FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            var size = new FileInfo(path).Length;
            if (size > bestSize)
            {
                bestSize = size;
                best = track.FileName;
            }
        }

        return best ?? tracks[0].FileName;
    }

    private static Dictionary<uint, string> MapWemIdsToMusBank(string audioFolder)
    {
        var map = new Dictionary<uint, string>();
        foreach (var bnk in Directory.GetFiles(audioFolder, "MUS_*.bnk"))
        {
            IReadOnlyList<uint> ids;
            try
            {
                ids = WwiseDidx.ReadMediaIds(bnk);
            }
            catch (IOException)
            {
                continue;
            }

            var bankName = Path.GetFileName(bnk);
            foreach (var id in ids)
            {
                if (!map.ContainsKey(id))
                {
                    map[id] = bankName;
                }
            }
        }

        return map;
    }

    private static string HumanBankName(string bankFile)
    {
        var stem = Path.GetFileNameWithoutExtension(bankFile);
        if (stem.Equals("MUS_Menu", StringComparison.OrdinalIgnoreCase))
        {
            return "Menu (main)";
        }

        if (stem.Equals("MUS_BattlePass", StringComparison.OrdinalIgnoreCase))
        {
            return "Battle Pass";
        }

        if (stem.StartsWith("MUS_BattlePass_", StringComparison.OrdinalIgnoreCase))
        {
            return "Battle Pass · " + stem["MUS_BattlePass_".Length..].Replace('_', ' ');
        }

        if (stem.StartsWith("MUS_Menu_Event_", StringComparison.OrdinalIgnoreCase))
        {
            return "Event menu · " + stem["MUS_Menu_Event_".Length..].Replace('_', ' ');
        }

        if (stem.StartsWith("MUS_Menu_XO_", StringComparison.OrdinalIgnoreCase))
        {
            return "Crossover menu · " + stem["MUS_Menu_XO_".Length..].Replace('_', ' ');
        }

        if (stem.Equals("MUS_Level_01", StringComparison.OrdinalIgnoreCase))
        {
            return "Level 01 (default maps)";
        }

        if (stem.StartsWith("MUS_", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[4..];
        }

        return stem.Replace('_', ' ');
    }

    private static string SlotForMusBank(string bankFile)
    {
        var stem = Path.GetFileNameWithoutExtension(bankFile);
        if (stem.Contains("Win", StringComparison.OrdinalIgnoreCase))
        {
            return "victory";
        }

        if (stem.Contains("Select", StringComparison.OrdinalIgnoreCase))
        {
            return "select";
        }

        if (stem.StartsWith("MUS_Menu", StringComparison.OrdinalIgnoreCase)
            || stem.StartsWith("MUS_BattlePass", StringComparison.OrdinalIgnoreCase))
        {
            return "menu";
        }

        if (stem.StartsWith("MUS_Level", StringComparison.OrdinalIgnoreCase))
        {
            return "combat";
        }

        return "other";
    }

    private static string SlotForLegacyMp3(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (baseName.StartsWith("BrawlhallaCharacterSelect", StringComparison.OrdinalIgnoreCase))
        {
            return "select";
        }

        if (baseName.StartsWith("BrawlhallaWinTheme", StringComparison.OrdinalIgnoreCase))
        {
            return "victory";
        }

        if (baseName.StartsWith("BrawlhallaMenu", StringComparison.OrdinalIgnoreCase))
        {
            return "menu";
        }

        if (baseName.StartsWith("BrawlhallaTheme", StringComparison.OrdinalIgnoreCase))
        {
            return "combat";
        }

        return "other";
    }
}
