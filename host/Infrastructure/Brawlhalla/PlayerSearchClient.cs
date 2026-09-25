using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Brawlhalla;

/// <summary>
/// BMG v1 ranked search. No API key. Do not scrape Tracker.
/// </summary>
public static class PlayerSearchClient
{
    public const string LoadError = BrawlhallaJson.LoadError;

    public static readonly string[] Modes = ["1v1", "2v2", "3v3"];
    public const int PageSize = 15;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    private static readonly ConcurrentDictionary<string, Entry> Pages = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Task<PlayerSearchPageDto>> Inflight = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, BandMark> BandMarks = new(StringComparer.Ordinal);

    public static async Task<PlayerSearchPageDto> SearchAsync(
        string? query,
        string? gameMode = null,
        string? region = null,
        int page = 1,
        string? tier = null,
        bool ascending = false,
        CancellationToken cancellationToken = default)
    {
        query = query?.Trim() ?? "";
        gameMode = NormalizeMode(gameMode);
        region = NormalizeRegion(region);
        tier = NormalizeTier(tier);
        if (page < 1)
        {
            page = 1;
        }

        var cacheKey = CacheKey(query, gameMode, region, page, tier, ascending);
        if (Pages.TryGetValue(cacheKey, out var entry) && entry.ExpiresUtc > DateTime.UtcNow)
        {
            return WithRecents(entry.Page, query);
        }

        var task = Inflight.GetOrAdd(
            cacheKey,
            _ => FetchAsync(cacheKey, query, gameMode, region, page, tier, ascending, CancellationToken.None));
        try
        {
            return WithRecents(await task.WaitAsync(cancellationToken).ConfigureAwait(false), query);
        }
        catch
        {
            Inflight.TryRemove(cacheKey, out _);
            throw;
        }
    }

    public static async Task<IReadOnlyList<PlayerSearchMatchDto>> RankingsAsync(
        string gameMode,
        string search,
        CancellationToken cancellationToken)
    {
        var (matches, _) = await RankingsPageAsync(gameMode, search, "ALL", 1, cancellationToken)
            .ConfigureAwait(false);
        return matches;
    }

    private static async Task<(IReadOnlyList<PlayerSearchMatchDto> Matches, int TotalPages)> RankingsPageAsync(
        string gameMode,
        string? search,
        string region,
        int page,
        CancellationToken cancellationToken)
    {
        var url =
            "https://api.brawlhalla.com/v1/leaderboard/ranked?game_mode="
            + Uri.EscapeDataString(gameMode)
            + "&region="
            + Uri.EscapeDataString(region)
            + "&max_results="
            + PageSize.ToString(CultureInfo.InvariantCulture)
            + "&page="
            + page.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(search))
        {
            url += "&search=" + Uri.EscapeDataString(search);
        }

        using var doc = await BrawlhallaJson.GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return ([], 1);
        }

        var totalPages = BrawlhallaJson.ReadInt(doc.RootElement, "total_pages") ?? 1;
        return (ParseRankings(doc.RootElement, gameMode), totalPages < 1 ? 1 : totalPages);
    }

    private static async Task<PlayerSearchPageDto> FetchAsync(
        string cacheKey,
        string query,
        string gameMode,
        string region,
        int page,
        string tier,
        bool ascending,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ResolveAsync(query, gameMode, region, page, tier, ascending, cancellationToken)
                .ConfigureAwait(false);
            // Empty name misses must not freeze for 2 minutes.
            if (result.Warning is null && (result.Matches.Count > 0 || query.Length < 2))
            {
                Pages[cacheKey] = new Entry(result, DateTime.UtcNow.Add(Ttl));
            }

            return result;
        }
        finally
        {
            Inflight.TryRemove(cacheKey, out _);
        }
    }

    private static async Task<PlayerSearchPageDto> ResolveAsync(
        string query,
        string gameMode,
        string region,
        int page,
        string tier,
        bool ascending,
        CancellationToken cancellationToken)
    {
        if (LooksLikeSteamId(query))
        {
            return await SearchBySteamAsync(query, cancellationToken).ConfigureAwait(false);
        }

        var needle = NameNeedle(query);
        if (LooksLikeId(query, out var id))
        {
            var byId = await SearchByIdAsync(id, query, cancellationToken).ConfigureAwait(false);
            if (byId.Matches.Count == 0)
            {
                return needle.Length < 2
                    ? byId
                    : await SearchByNameAsync(needle, gameMode, region, page, tier, ascending, cancellationToken)
                        .ConfigureAwait(false);
            }

            if (needle.Length < 2 || NameHits(byId.Matches[0].Name, needle))
            {
                return byId;
            }
        }

        return await SearchByNameAsync(
                needle.Length >= 2 ? needle : query,
                gameMode,
                region,
                page,
                tier,
                ascending,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<PlayerSearchPageDto> SearchByNameAsync(
        string query,
        string gameMode,
        string region,
        int page,
        string tier,
        bool ascending,
        CancellationToken cancellationToken)
    {
        var search = NameNeedle(query);
        if (search.Length < 2)
        {
            search = "";
        }

        if (search.Length == 0 && tier is not "all")
        {
            return await SearchBandAsync(gameMode, region, page, tier, ascending, cancellationToken)
                .ConfigureAwait(false);
        }

        // Ascending on the whole board means the bottom of the ladder, not a flipped page.
        var boardPage = page;
        if (search.Length == 0 && ascending)
        {
            var total = await TotalPagesAsync(gameMode, region, cancellationToken).ConfigureAwait(false);
            boardPage = Math.Clamp(total - page + 1, 1, total);
        }

        if (search.Length == 0)
        {
            var (matches, totalPages) = await RankingsPageAsync(
                    gameMode,
                    null,
                    region,
                    boardPage,
                    cancellationToken)
                .ConfigureAwait(false);
            return new PlayerSearchPageDto(matches, query, gameMode, totalPages);
        }

        // BMG 1v1 search= is fuzzy and often omits the exact name; 2v2/3v3 still list them.
        StatsDebugLog.Reset(
            "name=" + search
            + " mode=" + gameMode
            + " region=" + region);
        var modes = NameSearchModes(gameMode);
        var bmgTasks = modes
            .Select(mode => RankingsPageAsync(mode, search, region, 1, cancellationToken))
            .ToArray();
        await Task.WhenAll(bmgTasks).ConfigureAwait(false);

        var hits = new List<PlayerSearchMatchDto>();
        var seen = new HashSet<int>();
        for (var i = 0; i < modes.Length; i++)
        {
            var pageRows = (await bmgTasks[i].ConfigureAwait(false)).Matches;
            var modeHits = 0;
            foreach (var row in pageRows)
            {
                if (!NameHits(row.Name, search))
                {
                    continue;
                }

                modeHits++;
                if (!seen.Add(row.Id))
                {
                    continue;
                }

                hits.Add(row);
            }

            StatsDebugLog.Line("bmg " + modes[i] + " raw=" + pageRows.Count + " nameHits=" + modeHits);
        }

        hits.Sort((left, right) =>
        {
            var rank = NameRank(left.Name, search).CompareTo(NameRank(right.Name, search));
            if (rank != 0)
            {
                return rank;
            }

            var leftRating = left.Rating ?? -1;
            var rightRating = right.Rating ?? -1;
            return ascending ? leftRating.CompareTo(rightRating) : rightRating.CompareTo(leftRating);
        });
        var warning = hits.Count == 0
            ? "No ranked player named \"" + search + "\" in 1v1, 2v2, or 3v3 this season. Paste their Brawlhalla id or a player URL."
            : null;
        StatsDebugLog.Line("merged=" + hits.Count);
        return WithLog(new PlayerSearchPageDto(hits, query, gameMode, 1, warning));
    }

    private static PlayerSearchPageDto WithLog(PlayerSearchPageDto page)
    {
        var path = StatsDebugLog.PathOrNull();
        return path is null ? page : page with { LogPath = path };
    }

    private static string[] NameSearchModes(string gameMode)
    {
        return gameMode switch
        {
            "2v2" => ["2v2", "1v1", "3v3"],
            "3v3" => ["3v3", "1v1", "2v2"],
            _ => ["1v1", "2v2", "3v3"],
        };
    }

    private static async Task<PlayerSearchPageDto> SearchBandAsync(
        string gameMode,
        string region,
        int page,
        string tier,
        bool ascending,
        CancellationToken cancellationToken)
    {
        var band = await BandAsync(gameMode, region, tier, cancellationToken).ConfigureAwait(false);
        // Ascending walks the band backwards, so page 1 is the lowest rating of the tier.
        var boardPage = ascending
            ? band.StartPage + band.PageCount - page
            : band.StartPage + page - 1;
        boardPage = Math.Clamp(boardPage, band.StartPage, band.StartPage + band.PageCount - 1);

        var leaf = await RankingsPageAsync(gameMode, null, region, boardPage, cancellationToken)
            .ConfigureAwait(false);
        return new PlayerSearchPageDto(InBand(leaf.Matches, tier), "", gameMode, band.PageCount);
    }

    private static async Task<int> TotalPagesAsync(
        string gameMode,
        string region,
        CancellationToken cancellationToken)
    {
        var markKey = gameMode + "|" + region + "|board";
        if (BandMarks.TryGetValue(markKey, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
        {
            return cached.PageCount;
        }

        var first = await RankingsPageAsync(gameMode, null, region, 1, cancellationToken).ConfigureAwait(false);
        var total = Math.Max(1, first.TotalPages);
        BandMarks[markKey] = new BandMark(1, total, DateTime.UtcNow.Add(Ttl));
        return total;
    }

    private static async Task<BandMark> BandAsync(
        string gameMode,
        string region,
        string tier,
        CancellationToken cancellationToken)
    {
        var markKey = gameMode + "|" + region + "|" + tier;
        if (BandMarks.TryGetValue(markKey, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
        {
            return cached;
        }

        var first = await RankingsPageAsync(gameMode, null, region, 1, cancellationToken).ConfigureAwait(false);
        var total = Math.Max(1, first.TotalPages);
        var start = tier == "diamond"
            ? 1
            : await FirstPageAsync(
                gameMode,
                region,
                total,
                GuessBandPage(tier, total),
                rows => InBand(rows, tier).Count > 0,
                cancellationToken).ConfigureAwait(false);
        var afterBand = tier == "tin"
            ? total + 1
            : await FirstPageAsync(
                gameMode,
                region,
                total,
                GuessBandPage(NextBand(tier), total),
                rows => PastTier(rows, tier),
                cancellationToken).ConfigureAwait(false);

        var mark = new BandMark(start, Math.Max(1, afterBand - start), DateTime.UtcNow.Add(Ttl));
        BandMarks[markKey] = mark;
        return mark;
    }

    /// <summary>
    /// First page where <paramref name="reached"/> is true, or <c>total + 1</c> if none.
    /// One guess to shrink the range, then bisection. Cached on the band, so the extra
    /// probes run once per mode/region/tier for two minutes.
    /// </summary>
    private static async Task<int> FirstPageAsync(
        string gameMode,
        string region,
        int total,
        int guess,
        Func<IReadOnlyList<PlayerSearchMatchDto>, bool> reached,
        CancellationToken cancellationToken)
    {
        var lo = 1;
        var hi = total + 1;
        guess = Math.Clamp(guess, 1, total);
        var rows = await RankingsPageAsync(gameMode, null, region, guess, cancellationToken).ConfigureAwait(false);
        if (reached(rows.Matches))
        {
            hi = guess;
        }
        else
        {
            lo = guess + 1;
        }

        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            rows = await RankingsPageAsync(gameMode, null, region, mid, cancellationToken).ConfigureAwait(false);
            if (reached(rows.Matches))
            {
                hi = mid;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return lo;
    }

    private static bool PastTier(IReadOnlyList<PlayerSearchMatchDto> rows, string tier)
    {
        var wanted = FamilyRank(tier);
        var ranks = rows
            .Select(row => TierFamily(row.Tier, row.Rating))
            .Where(family => family is not null)
            .Select(FamilyRank)
            .ToList();
        return ranks.Count > 0 && ranks.All(rank => rank < wanted);
    }

    private static int FamilyRank(string? family)
    {
        return family switch
        {
            "diamond" or "valhallan" => 5,
            "platinum" => 4,
            "gold" => 3,
            "silver" => 2,
            "bronze" => 1,
            _ => 0,
        };
    }

    private static List<PlayerSearchMatchDto> InBand(IEnumerable<PlayerSearchMatchDto> rows, string tier)
    {
        return rows.Where(row => TierFamily(row.Tier, row.Rating) == tier).ToList();
    }

    private static PlayerSearchPageDto WithRecents(PlayerSearchPageDto page, string query)
    {
        if (LooksLikeSteamId(query) || LooksLikeId(query, out _))
        {
            return page;
        }

        var local = StatsRecentStore.Matches(query);
        if (local.Count == 0)
        {
            return page;
        }

        var rankedIds = page.Matches.Select(row => row.Id).ToHashSet();
        var extra = local.Where(row => !rankedIds.Contains(row.Id)).ToList();
        if (extra.Count == 0)
        {
            return page;
        }

        return page with { Matches = extra.Concat(page.Matches).ToList() };
    }

    private static async Task<PlayerSearchPageDto> SearchByIdAsync(
        int id,
        string query,
        CancellationToken cancellationToken)
    {
        var stats = await PlayerStatsAsync(id, cancellationToken).ConfigureAwait(false);
        if (stats is null)
        {
            return new PlayerSearchPageDto([], query, "1v1");
        }

        var ranked = await RankingsAsync("1v1", stats.Name, cancellationToken).ConfigureAwait(false);
        var match = ranked.FirstOrDefault(row => row.Id == id);
        if (match is not null)
        {
            return new PlayerSearchPageDto([match], query, "1v1");
        }

        return new PlayerSearchPageDto([stats], query, stats.GameMode);
    }

    private static async Task<PlayerSearchPageDto> SearchBySteamAsync(
        string steamId,
        CancellationToken cancellationToken)
    {
        var url = "https://api.brawlhalla.com/v1/search?steam_id=" + Uri.EscapeDataString(steamId);
        using var doc = await BrawlhallaJson.GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return new PlayerSearchPageDto([], steamId, "");
        }

        var id = BrawlhallaJson.ReadInt(doc.RootElement, "brawlhalla_id") ?? 0;
        if (id <= 0)
        {
            return new PlayerSearchPageDto([], steamId, "");
        }

        return await SearchByIdAsync(id, steamId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PlayerSearchMatchDto?> PlayerStatsAsync(int id, CancellationToken cancellationToken)
    {
        var url = "https://api.brawlhalla.com/v1/player/stats?brawlhalla_id=" + id.ToString(CultureInfo.InvariantCulture);
        using var doc = await BrawlhallaJson.GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }

        var root = doc.RootElement;
        var playerId = BrawlhallaJson.ReadInt(root, "brawlhalla_id") ?? 0;
        var name = BrawlhallaJson.ReadString(root, "name");
        if (playerId <= 0 || name.Length == 0)
        {
            return null;
        }

        var games = BrawlhallaJson.ReadInt(root, "games");
        var wins = BrawlhallaJson.ReadInt(root, "wins");
        return new PlayerSearchMatchDto(
            playerId,
            name,
            "",
            BrawlhallaJson.ReadStringOrNull(root, "region"),
            BrawlhallaJson.ReadStringOrNull(root, "tier"),
            BrawlhallaJson.ReadInt(root, "rating"),
            BrawlhallaJson.ReadInt(root, "peak_rating"),
            BrawlhallaJson.ReadInt(root, "global_rank"),
            wins,
            LossesFrom(games, wins, BrawlhallaJson.ReadInt(root, "losses")));
    }

    private static IReadOnlyList<PlayerSearchMatchDto> ParseRankings(JsonElement root, string gameMode)
    {
        if (!root.TryGetProperty("rankings", out var rankings) || rankings.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var matches = new List<PlayerSearchMatchDto>();
        var seen = new HashSet<int>();
        foreach (var row in rankings.EnumerateArray())
        {
            if (!row.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var region = BrawlhallaJson.ReadStringOrNull(row, "region");
            var tier = BrawlhallaJson.ReadStringOrNull(row, "tier");
            var rating = BrawlhallaJson.ReadInt(row, "rating");
            var peak = BrawlhallaJson.ReadInt(row, "best_rating");
            var rank = BrawlhallaJson.ReadInt(row, "rank");
            var wins = BrawlhallaJson.ReadInt(row, "wins");
            var losses = BrawlhallaJson.ReadInt(row, "losses");

            var team = new List<PlayerTeammateDto>();
            foreach (var player in players.EnumerateArray())
            {
                var id = BrawlhallaJson.ReadInt(player, "id")
                    ?? BrawlhallaJson.ReadInt(player, "brawlhalla_id")
                    ?? 0;
                var name = BrawlhallaJson.ReadString(player, "username");
                if (id <= 0 || name.Length == 0)
                {
                    continue;
                }

                team.Add(new PlayerTeammateDto(id, name));
            }

            foreach (var member in team)
            {
                if (!seen.Add(member.Id))
                {
                    continue;
                }

                var teammates = team.Where(row => row.Id != member.Id).ToList();
                matches.Add(new PlayerSearchMatchDto(
                    member.Id,
                    member.Name,
                    gameMode,
                    region,
                    tier,
                    rating,
                    peak,
                    rank,
                    wins,
                    losses,
                    teammates.Count == 0 ? null : teammates));
            }
        }

        return matches;
    }

    private static bool NameHits(string name, string needle)
    {
        return NameKey(name).Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static int NameRank(string name, string needle)
    {
        var key = NameKey(name);
        if (key.Equals(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return key.StartsWith(needle, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }

    private static string NameKey(string name)
    {
        var key = name.Trim();
        return key.StartsWith('@') ? key[1..].Trim() : key;
    }

    private static string NameNeedle(string query)
    {
        if (query.Contains("/player/", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var hash = query.LastIndexOf('#');
        var name = (hash >= 0 ? query[..hash] : query).Trim();
        return name.StartsWith('@') ? name[1..].Trim() : name;
    }

    private static bool LooksLikeId(string query, out int id)
    {
        return TryBareId(query, out id) || TryHashId(query, out id);
    }

    private static bool TryBareId(string query, out int id)
    {
        if (TryDigits(query, out id) || TryPlayerUrl(query, out id))
        {
            return true;
        }

        var trimmed = query.Trim();
        return trimmed.StartsWith('#') && TryLeadingDigits(trimmed.AsSpan(1).Trim(), out id);
    }

    private static bool TryHashId(string query, out int id)
    {
        id = 0;
        var hash = query.LastIndexOf('#');
        return hash > 0 && TryLeadingDigits(query.AsSpan(hash + 1).Trim(), out id);
    }

    private static bool TryPlayerUrl(string query, out int id)
    {
        id = 0;
        const string marker = "/player/";
        var at = query.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return false;
        }

        return TryLeadingDigits(query.AsSpan(at + marker.Length), out id);
    }

    private static bool TryDigits(string query, out int id)
    {
        id = 0;
        if (query.Length == 0)
        {
            return false;
        }

        foreach (var ch in query)
        {
            if (!char.IsAsciiDigit(ch))
            {
                return false;
            }
        }

        return int.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }

    private static bool TryLeadingDigits(ReadOnlySpan<char> span, out int id)
    {
        id = 0;
        var n = 0;
        while (n < span.Length && char.IsAsciiDigit(span[n]))
        {
            n++;
        }

        return n > 0
            && int.TryParse(span[..n], NumberStyles.None, CultureInfo.InvariantCulture, out id)
            && id > 0;
    }

    private static bool LooksLikeSteamId(string query)
    {
        if (query.Length is < 15 or > 20 || !query.StartsWith("765", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var ch in query)
        {
            if (!char.IsAsciiDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static string CacheKey(
        string query,
        string gameMode,
        string region,
        int page,
        string tier,
        bool ascending)
    {
        var tail = "|" + gameMode + "|" + region + (ascending ? "|asc" : "");
        if (LooksLikeSteamId(query))
        {
            return "steam|" + query;
        }

        if (TryBareId(query, out var id))
        {
            return "id|" + id.ToString(CultureInfo.InvariantCulture);
        }

        if (TryHashId(query, out id))
        {
            return "id|"
                + id.ToString(CultureInfo.InvariantCulture)
                + "|as|"
                + NameNeedle(query).ToLowerInvariant();
        }

        var name = NameNeedle(query).ToLowerInvariant();
        if (name.Length >= 2)
        {
            return "name|" + name + tail;
        }

        return "board" + tail + "|" + tier + "|" + page.ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizeMode(string? gameMode)
    {
        return gameMode is "2v2" or "3v3" ? gameMode : "1v1";
    }

    private static string NormalizeRegion(string? region)
    {
        var code = region?.Trim().ToUpperInvariant() ?? "ALL";
        return code is "ALL" or "US-E" or "US-W" or "EU" or "SEA" or "BRZ" or "AUS" or "JPS" or "SA" or "ME"
            ? code
            : "ALL";
    }

    private static string NormalizeTier(string? tier)
    {
        var key = (tier ?? "all").Trim().ToLowerInvariant();
        return key is "diamond" or "platinum" or "gold" or "silver" or "bronze" or "tin"
            ? key
            : "all";
    }

    private static int GuessBandPage(string tier, int totalPages)
    {
        var guess = tier switch
        {
            "diamond" => 1,
            "platinum" => totalPages * 2 / 100,
            "gold" => totalPages * 62 / 100,
            "silver" => totalPages * 88 / 100,
            "bronze" => totalPages * 96 / 100,
            "tin" => totalPages * 99 / 100,
            _ => totalPages,
        };
        return Math.Clamp(guess, 1, Math.Max(1, totalPages));
    }

    private static string NextBand(string tier)
    {
        return tier switch
        {
            "diamond" => "platinum",
            "platinum" => "gold",
            "gold" => "silver",
            "silver" => "bronze",
            "bronze" => "tin",
            _ => "end",
        };
    }

    internal static string? TierFamily(string? tier, int? rating)
    {
        var named = (tier ?? "").Trim().ToLowerInvariant();
        if (named.Contains("valhall"))
        {
            return "valhallan";
        }

        foreach (var key in new[] { "diamond", "platinum", "gold", "silver", "bronze", "tin" })
        {
            if (named.Contains(key))
            {
                return key;
            }
        }

        if (rating is null)
        {
            return null;
        }

        return rating >= 2000 ? "diamond"
            : rating >= 1680 ? "platinum"
            : rating >= 1390 ? "gold"
            : rating >= 1130 ? "silver"
            : rating >= 910 ? "bronze"
            : "tin";
    }

    private static int? LossesFrom(int? games, int? wins, int? losses)
    {
        if (losses is not null)
        {
            return losses;
        }

        if (games is null || wins is null || games < wins)
        {
            return null;
        }

        return games - wins;
    }

    private readonly record struct Entry(PlayerSearchPageDto Page, DateTime ExpiresUtc);

    private readonly record struct BandMark(int StartPage, int PageCount, DateTime ExpiresUtc);
}
