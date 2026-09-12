using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BrawlEngine.Host.Infrastructure.Apply;

/// <summary>
/// One Brawlhalla recolour script: the palette a body part is tinted from. A pack that
/// reshapes a sprite has to ship a new palette, or the game recolours the wrong parts.
/// </summary>
public sealed record BmodColorScript(
    string ClassName,
    IReadOnlyList<int> Colors);

public sealed record BmodSwfReplace(
    string FileName,
    IReadOnlyList<string> Sprites,
    IReadOnlyList<BmodColorScript> ColorScripts);

public sealed record BmodPack(
    IReadOnlyList<BmodSwfReplace> Swfs);

public static class BmodManifest
{
    private static readonly byte[] FormatVersionMarker = Encoding.ASCII.GetBytes("\"formatVersion\"");

    public static (BmodPack? Pack, string? Error) TryParse(string bmodPath)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(bmodPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, "Could not read the .bmod: " + ex.Message);
        }

        if (bytes.Length < 8
            || bytes[0] != (byte)'F'
            || bytes[1] != (byte)'W'
            || bytes[2] != (byte)'S')
        {
            return (null, "This .bmod is not an uncompressed SWF (FWS). Apply cannot use it.");
        }

        var json = ExtractJsonObject(bytes);
        if (json is null)
        {
            return (null, "This pack has no formatVersion JSON. Apply cannot guess the game SWFs.");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return ReadPack(doc.RootElement);
        }
        catch (JsonException)
        {
            return (null, "This pack's mapping JSON is invalid. Apply stopped.");
        }
    }

    private static (BmodPack? Pack, string? Error) ReadPack(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (null, "This pack's mapping JSON is invalid. Apply stopped.");
        }

        if (!root.TryGetProperty("formatVersion", out var versionEl)
            || !versionEl.TryGetInt32(out var version)
            || version != 2)
        {
            return (null, "This pack is not formatVersion 2. Apply stopped.");
        }

        if (!root.TryGetProperty("formatType", out var typeEl)
            || typeEl.ValueKind != JsonValueKind.String
            || !string.Equals(typeEl.GetString(), "mod", StringComparison.Ordinal))
        {
            return (null, "This pack's formatType is not mod. Apply stopped.");
        }

        if (HasUnknownPayload(root, "files") || HasUnknownPayload(root, "langFiles"))
        {
            return (null, "This pack uses files/langFiles which Apply does not support yet.");
        }

        if (!root.TryGetProperty("swfs", out var swfsEl) || swfsEl.ValueKind != JsonValueKind.Object)
        {
            return (null, "This pack has no swfs mapping. Apply stopped.");
        }

        var list = new List<BmodSwfReplace>();
        foreach (var swf in swfsEl.EnumerateObject())
        {
            if (swf.Value.ValueKind != JsonValueKind.Object)
            {
                return (null, "This pack's swfs mapping is not understood. Apply stopped.");
            }

            var (colorScripts, scriptError) = ReadColorScripts(swf.Value);
            if (scriptError is not null)
            {
                return (null, scriptError);
            }

            if (HasSounds(swf.Value))
            {
                return (null, "This pack replaces sounds inside SWFs, which Apply does not support yet.");
            }

            if (!swf.Value.TryGetProperty("sprites", out var spritesEl)
                || spritesEl.ValueKind != JsonValueKind.Array)
            {
                return (null, "This pack's sprite list is missing. Apply stopped.");
            }

            var sprites = new List<string>();
            foreach (var item in spritesEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(item.GetString()))
                {
                    return (null, "This pack's sprite list is not understood. Apply stopped.");
                }

                sprites.Add(item.GetString()!);
            }

            if (sprites.Count == 0)
            {
                continue;
            }

            var name = Path.GetFileName(swf.Name.Trim());
            if (string.IsNullOrEmpty(name)
                || !name.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
            {
                return (null, "This pack lists a SWF name Apply cannot use: " + swf.Name);
            }

            list.Add(new BmodSwfReplace(name, sprites, colorScripts));
        }

        if (list.Count == 0)
        {
            return (null, "This pack lists no sprites to replace. Apply stopped.");
        }

        return (new BmodPack(list), null);
    }

    public static (BmodPack? Pack, string? Error) TryParseMany(IReadOnlyList<string> bmodPaths)
    {
        var spritesBySwf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var colorsBySwf = new Dictionary<string, List<BmodColorScript>>(StringComparer.OrdinalIgnoreCase);
        string? lastError = null;
        var any = false;
        foreach (var path in bmodPaths)
        {
            var (pack, error) = TryParse(path);
            if (pack is null)
            {
                lastError = error;
                continue;
            }

            any = true;
            foreach (var swf in pack.Swfs)
            {
                if (!spritesBySwf.TryGetValue(swf.FileName, out var sprites))
                {
                    sprites = [];
                    spritesBySwf[swf.FileName] = sprites;
                }

                foreach (var sprite in swf.Sprites)
                {
                    if (!sprites.Contains(sprite, StringComparer.Ordinal))
                    {
                        sprites.Add(sprite);
                    }
                }

                if (!colorsBySwf.TryGetValue(swf.FileName, out var colors))
                {
                    colors = [];
                    colorsBySwf[swf.FileName] = colors;
                }

                // First pack wins, same as sprites: the packs are searched in folder order.
                foreach (var script in swf.ColorScripts)
                {
                    if (!colors.Any(known => known.ClassName == script.ClassName))
                    {
                        colors.Add(script);
                    }
                }
            }
        }

        if (!any)
        {
            return (null, lastError ?? "This pack is not understood. Apply stopped.");
        }

        var list = spritesBySwf
            .Where(row => row.Value.Count > 0)
            .Select(row => new BmodSwfReplace(
                row.Key,
                row.Value,
                colorsBySwf.TryGetValue(row.Key, out var colors) ? colors : []))
            .ToList();
        if (list.Count == 0)
        {
            return (null, "This pack lists no sprites to replace. Apply stopped.");
        }

        return (new BmodPack(list), null);
    }

    /// <summary>
    /// The one script shape Apply understands: a class whose frame1 assigns a list of
    /// colour numbers. Whitespace is dropped first because packs indent this differently.
    /// Anything else is refused rather than half applied.
    /// </summary>
    private static readonly Regex ColorScriptShape = new(
        @"^package\{importflash\.display\.MovieClip;public(?:dynamic)?class(?<name>[A-Za-z_$][\w$]*)"
            + @"extendsMovieClip\{publicvara:Array;publicfunction\k<name>\(\)\{super\(\);"
            + @"addFrameScript\(0,this\.frame1\);\}functionframe1\(\):\*\{this\.a=\[(?<colors>[\d,]*)\];\}\}\}$",
        RegexOptions.Compiled);

    private static (IReadOnlyList<BmodColorScript> Scripts, string? Error) ReadColorScripts(
        JsonElement swf)
    {
        if (!swf.TryGetProperty("scripts", out var scripts)
            || scripts.ValueKind != JsonValueKind.Object)
        {
            return ([], null);
        }

        var list = new List<BmodColorScript>();
        foreach (var script in scripts.EnumerateObject())
        {
            if (script.Value.ValueKind != JsonValueKind.String)
            {
                return ([], "This pack's script " + script.Name + " is not understood. Apply stopped.");
            }

            var match = ColorScriptShape.Match(Squeeze(script.Value.GetString()!));
            if (!match.Success
                || !string.Equals(match.Groups["name"].Value, script.Name, StringComparison.Ordinal))
            {
                return ([], "This pack's script " + script.Name
                    + " does more than set colours, which Apply does not support yet.");
            }

            var (colors, colorError) = ReadColorList(match.Groups["colors"].Value, script.Name);
            if (colorError is not null)
            {
                return ([], colorError);
            }

            list.Add(new BmodColorScript(script.Name, colors));
        }

        return (list, null);
    }

    private static (IReadOnlyList<int> Colors, string? Error) ReadColorList(
        string text,
        string scriptName)
    {
        if (text.Length == 0)
        {
            return ([], null);
        }

        var colors = new List<int>();
        foreach (var part in text.Split(','))
        {
            if (!int.TryParse(part, out var color) || color < 0)
            {
                return ([], "This pack's script " + scriptName
                    + " has a colour Apply cannot read: " + part);
            }

            colors.Add(color);
        }

        return (colors, null);
    }

    private static string Squeeze(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static bool HasSounds(JsonElement swf)
    {
        if (!swf.TryGetProperty("sounds", out var sounds) || sounds.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return sounds.GetArrayLength() > 0;
    }

    private static bool HasUnknownPayload(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return false;
        }

        if (el.ValueKind == JsonValueKind.Object)
        {
            return el.EnumerateObject().Any();
        }

        if (el.ValueKind == JsonValueKind.Array)
        {
            return el.GetArrayLength() > 0;
        }

        return el.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
    }

    private static string? ExtractJsonObject(byte[] bytes)
    {
        var marker = IndexOf(bytes, FormatVersionMarker);
        if (marker < 0)
        {
            return null;
        }

        var start = marker;
        while (start > 0 && bytes[start] != (byte)'{')
        {
            start--;
        }

        if (bytes[start] != (byte)'{')
        {
            return null;
        }

        var end = MatchObjectEnd(bytes, start);
        if (end < 0)
        {
            return null;
        }

        return Encoding.UTF8.GetString(bytes, start, end - start + 1);
    }

    private static int MatchObjectEnd(byte[] bytes, int start)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < bytes.Length; i++)
        {
            var c = bytes[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == (byte)'\\')
                {
                    escape = true;
                    continue;
                }

                if (c == (byte)'"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == (byte)'"')
            {
                inString = true;
                continue;
            }

            if (c == (byte)'{')
            {
                depth++;
            }
            else if (c == (byte)'}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        var last = haystack.Length - needle.Length;
        for (var i = 0; i <= last; i++)
        {
            var ok = true;
            for (var n = 0; n < needle.Length; n++)
            {
                if (haystack[i + n] != needle[n])
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                return i;
            }
        }

        return -1;
    }
}
