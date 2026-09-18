using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Networking;

namespace BrawlEngine.Host.Infrastructure.GameBanana;

/// <summary>
/// GameBanana has no real "default vs store skin" field. We only surface a target
/// when the title, body, or _aRequirements say so (e.g. "over THNX Researcher Loki",
/// "replaces the *Deathly Presence Munin*", or several Requirements lines).
/// </summary>
public static class SkinTarget
{
    public const string Default = "Default";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PerIdTimeout = TimeSpan.FromSeconds(12);
    private static readonly ConcurrentDictionary<int, CacheEntry> Cache = new();
    private static readonly Regex ReplacesRx = new(
        @"\breplaces(?:\s+the)?\s+\*?([^*\n]{3,80}?)\*?(?:\s+from\b|\s+and both\b|\s*[.\n]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex OverRx = new(
        @"\b(?:goes\s+)?over\s+\*?([^*\n)]{3,80}?)\*?(?=\s+from\b|\s*\)|\s*$|\s*\n)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex NumberedRx = new(
        @"^\s*\d+[\.\)]\s+(.{3,80}?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline);

    public static string? FromRecord(JsonElement record, string name, string legend)
    {
        var names = new List<string>();
        AddUnique(names, ReadRequirementNames(record), legend);
        var blob = name
            + "\n"
            + ReadString(record, "_sDescription")
            + "\n"
            + ReadString(record, "_sText");
        AddUnique(names, ParseNames(blob, legend), legend);
        return Join(names);
    }

    public static string? ReadDescription(JsonElement record, string name)
    {
        var text = StripHtml(ReadString(record, "_sDescription"));
        if (text.Length == 0 || text.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (text.Length > 160)
        {
            return text[..157] + "…";
        }

        return text;
    }

    public static CatalogPageDto ApplyCached(CatalogPageDto page)
    {
        if (page.Items.Count == 0)
        {
            return page;
        }

        var next = new CatalogItemDto[page.Items.Count];
        for (var i = 0; i < page.Items.Count; i++)
        {
            var item = page.Items[i];
            if (Cache.TryGetValue(item.Id, out var hit)
                && hit.ExpiresUtc > DateTime.UtcNow)
            {
                next[i] = item with
                {
                    SkinTarget = hit.Target ?? item.SkinTarget,
                    Description = hit.Description ?? item.Description,
                };
            }
            else
            {
                next[i] = item;
            }
        }

        return page with { Items = next };
    }

    public static async Task<CatalogPageDto> AttachAsync(
        CatalogPageDto page,
        CancellationToken cancellationToken)
    {
        var filled = await AttachAsync(page.Items, cancellationToken).ConfigureAwait(false);
        return page with { Items = filled };
    }

    public static async Task<IReadOnlyList<CatalogItemDto>> AttachAsync(
        IReadOnlyList<CatalogItemDto> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var next = new CatalogItemDto[items.Count];
        using var gate = new SemaphoreSlim(4);
        var tasks = items.Select(async (item, index) =>
        {
            var target = item.SkinTarget;
            var description = item.Description;
            if (Cache.TryGetValue(item.Id, out var hit) && hit.ExpiresUtc > DateTime.UtcNow)
            {
                target = hit.Target ?? target;
                description = hit.Description ?? description;
            }
            else
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (Cache.TryGetValue(item.Id, out hit) && hit.ExpiresUtc > DateTime.UtcNow)
                    {
                        target = hit.Target ?? target;
                        description = hit.Description ?? description;
                    }
                    else
                    {
                        var fetched = await FetchAsync(item, cancellationToken).ConfigureAwait(false);
                        target = fetched.Target ?? target;
                        description = fetched.Description ?? description;
                    }
                }
                finally
                {
                    gate.Release();
                }
            }

            next[index] = item with { SkinTarget = target, Description = description };
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return next;
    }

    public static string? Parse(string raw, string legend)
    {
        return Join(ParseNames(raw, legend));
    }

    private static IReadOnlyList<string> ParseNames(string raw, string legend)
    {
        var text = StripHtml(raw);
        if (text.Length == 0)
        {
            return [];
        }

        var names = new List<string>();
        foreach (Match match in ReplacesRx.Matches(text))
        {
            AddUnique(names, [match.Groups[1].Value], legend);
        }

        foreach (Match match in OverRx.Matches(text))
        {
            AddUnique(names, [match.Groups[1].Value], legend);
        }

        AddUnique(names, AfterPhrase(text, " requires "), legend);
        AddUnique(names, AfterPhrase(text, " require "), legend);
        AddUnique(names, RequirementSectionLines(text), legend);

        if (names.Count == 0 && MentionsDefault(text, legend))
        {
            return [Default];
        }

        return names;
    }

    private static async Task<CacheEntry> FetchAsync(CatalogItemDto item, CancellationToken cancellationToken)
    {
        try
        {
            var url = "https://gamebanana.com/apiv11/Mod/" + item.Id
                + "?_csvProperties=_sText,_sDescription,_aRequirements";
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(PerIdTimeout);
            using var response = await AppHttp.Shared.GetAsync(url, linked.Token).ConfigureAwait(false);
            using var doc = await GameBananaJson.ReadDocumentAsync(response, linked.Token).ConfigureAwait(false);
            var target = FromRecord(doc.RootElement, item.Name, item.Category);
            var description = ReadDescription(doc.RootElement, item.Name);
            var entry = new CacheEntry(target, description, DateTime.UtcNow.Add(CacheTtl));
            Cache[item.Id] = entry;
            return entry;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or OperationCanceledException)
        {
            var entry = new CacheEntry(item.SkinTarget, item.Description, DateTime.UtcNow.Add(CacheTtl));
            Cache[item.Id] = entry;
            return entry;
        }
    }

    private static IReadOnlyList<string> ReadRequirementNames(JsonElement record)
    {
        if (!record.TryGetProperty("_aRequirements", out var req))
        {
            return [];
        }

        if (req.ValueKind == JsonValueKind.Object
            && req.TryGetProperty("_aItems", out var items))
        {
            req = items;
        }

        if (req.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var row in req.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.String)
            {
                if (row.GetString() is { Length: > 0 } text)
                {
                    names.Add(text.Trim());
                }
            }
            else if (row.ValueKind == JsonValueKind.Array)
            {
                string? reqName = null;
                string? note = null;
                foreach (var cell in row.EnumerateArray())
                {
                    if (cell.ValueKind != JsonValueKind.String
                        || cell.GetString() is not { Length: > 0 } text)
                    {
                        continue;
                    }

                    text = text.Trim();
                    if (reqName is null)
                    {
                        reqName = text;
                    }
                    else
                    {
                        note = text;
                        break;
                    }
                }

                if (reqName is not null)
                {
                    names.Add(note is null ? reqName : reqName + " (" + note + ")");
                }
            }
            else if (row.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "_sName", "_sTitle", "_sValue" })
                {
                    if (row.TryGetProperty(key, out var nameEl)
                        && nameEl.GetString() is { Length: > 0 } name)
                    {
                        names.Add(name.Trim());
                        break;
                    }
                }
            }
        }

        return names;
    }

    private static IReadOnlyList<string> RequirementSectionLines(string text)
    {
        var names = new List<string>();
        foreach (Match match in NumberedRx.Matches(text))
        {
            names.Add(match.Groups[1].Value);
        }

        const string marker = "required to use this Mod";
        var at = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return names;
        }

        foreach (var line in text[(at + marker.Length)..].Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                if (names.Count > 0)
                {
                    break;
                }

                continue;
            }

            names.Add(trimmed);
        }

        return names;
    }

    private static void AddUnique(List<string> names, string? extra, string legend)
    {
        if (extra is not null)
        {
            AddUnique(names, [extra], legend);
        }
    }

    private static void AddUnique(List<string> names, IEnumerable<string> extra, string legend)
    {
        foreach (var item in extra)
        {
            var cleaned = Normalize(item, legend);
            if (cleaned is null || IsJunkLine(cleaned))
            {
                continue;
            }

            if (cleaned == Default && names.Count > 0)
            {
                continue;
            }

            if (cleaned != Default)
            {
                names.RemoveAll(row => row == Default);
            }

            if (names.Exists(row => row.Equals(cleaned, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            names.Add(cleaned);
        }
    }

    private static string? Join(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return null;
        }

        var specific = names.Where(row => row != Default).ToList();
        if (specific.Count > 0)
        {
            return string.Join(" · ", specific);
        }

        return Default;
    }

    private static string? Normalize(string value, string legend)
    {
        var cleaned = StripHtml(value).Replace("*", "").Trim().Trim('"', '\'', '.', ',', ';');
        cleaned = Regex.Replace(cleaned, @"^\d+[\.\)]\s+", "");
        if (cleaned.EndsWith(" skin", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^5].Trim();
        }

        if (cleaned.Length < 3 || cleaned.Length > 90)
        {
            return null;
        }

        if (IsDefaultPhrase(cleaned, legend))
        {
            return Default;
        }

        return cleaned;
    }

    private static bool IsJunkLine(string value)
    {
        return value.Equals("Requirements", StringComparison.OrdinalIgnoreCase)
            || ContainsWord(value, "prerequisites required")
            || ContainsWord(value, "dependencies and prerequisites")
            || ContainsWord(value, "associated weapons")
            || ContainsWord(value, "use this Mod");
    }

    private static bool MentionsDefault(string text, string legend)
    {
        return ContainsWord(text, "default skin")
            || ContainsWord(text, "base skin")
            || ContainsWord(text, "default " + legend)
            || ContainsWord(text, "vanilla " + legend);
    }

    private static bool IsDefaultPhrase(string value, string legend)
    {
        return value.Equals(legend, StringComparison.OrdinalIgnoreCase)
            || value.Equals("default", StringComparison.OrdinalIgnoreCase)
            || value.Equals("default " + legend, StringComparison.OrdinalIgnoreCase)
            || value.Equals("the default", StringComparison.OrdinalIgnoreCase)
            || value.Equals("base", StringComparison.OrdinalIgnoreCase);
    }

    private static string? AfterPhrase(string text, string phrase)
    {
        var index = text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = index + phrase.Length;
        var end = start;
        while (end < text.Length)
        {
            var c = text[end];
            if (c is '\n' or ')' or ']' or '|' or '<' or '/')
            {
                break;
            }

            end++;
        }

        if (end <= start)
        {
            return null;
        }

        return text[start..end].Trim();
    }

    private static bool ContainsWord(string text, string phrase)
    {
        return text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ReadString(JsonElement record, string name)
    {
        return record.TryGetProperty(name, out var el) ? el.GetString() ?? "" : "";
    }

    private static string StripHtml(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return "";
        }

        // Keep list/paragraph breaks so Requirements lines stay separate.
        var withoutTags = Regex.Replace(raw, "<(?:br|p|div|li|h[1-6]|tr)[^>]*>", "\n", RegexOptions.IgnoreCase);
        withoutTags = Regex.Replace(withoutTags, "<[^>]+>", " ");
        withoutTags = withoutTags
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("\u00a0", " ");
        var builder = new StringBuilder(withoutTags.Length);
        var space = false;
        foreach (var c in withoutTags)
        {
            if (c == '\n')
            {
                builder.Append('\n');
                space = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (!space && builder.Length > 0)
                {
                    builder.Append(' ');
                    space = true;
                }
            }
            else
            {
                builder.Append(c);
                space = false;
            }
        }

        return builder.ToString().Trim();
    }

    private readonly record struct CacheEntry(string? Target, string? Description, DateTime ExpiresUtc);
}
