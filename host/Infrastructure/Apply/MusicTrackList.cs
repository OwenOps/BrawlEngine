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

            if (bankById.TryGetValue(id, out var bankFile))
            {
                var label = HumanBankName(bankFile) + " · " + name;
                tracks.Add(new MusicTrackDto(name, label, SlotForMusBank(bankFile)));
            }
            else
            {
                tracks.Add(new MusicTrackDto(name, name, "other"));
            }
        }

        return tracks
            .OrderBy(track => track.Slot, StringComparer.OrdinalIgnoreCase)
            .ThenBy(track => track.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        if (stem.StartsWith("MUS_Menu", StringComparison.OrdinalIgnoreCase))
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
