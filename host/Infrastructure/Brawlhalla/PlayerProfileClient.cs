using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using BrawlEngine.Host.Domain.Models;

namespace BrawlEngine.Host.Infrastructure.Brawlhalla;

public static class PlayerProfileClient
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);
    private static readonly ConcurrentDictionary<int, Entry> Cache = new();
    private static readonly ConcurrentDictionary<int, Task<PlayerProfileDto>> Inflight = new();

    public static async Task<PlayerProfileDto> GetAsync(
        int id,
        string? name,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            throw new HttpRequestException(BrawlhallaJson.LoadError);
        }

        if (Cache.TryGetValue(id, out var entry) && entry.ExpiresUtc > DateTime.UtcNow)
        {
            return entry.Profile;
        }

        var task = Inflight.GetOrAdd(id, _ => FetchAsync(id, name, CancellationToken.None));
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Inflight.TryRemove(id, out _);
            throw;
        }
    }

    private static async Task<PlayerProfileDto> FetchAsync(
        int id,
        string? name,
        CancellationToken cancellationToken)
    {
        try
        {
            var statsTask = LifetimeAsync(id, cancellationToken);
            var guildTask = GuildNameAsync(id, cancellationToken);
            var legendsTask = LegendNamesAsync(cancellationToken);
            var rankedHint = name?.Trim() ?? "";
            var rankedTask = RankedAllAsync(id, rankedHint, cancellationToken);

            var lifetime = await statsTask.ConfigureAwait(false);
            if (lifetime is null)
            {
                return await FallbackAsync(id, rankedHint, guildTask, rankedTask, cancellationToken)
                    .ConfigureAwait(false);
            }

            string? guild = null;
            IReadOnlyDictionary<int, string> legendNames = new Dictionary<int, string>();
            try
            {
                guild = await guildTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
            }

            try
            {
                legendNames = await legendsTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
            }

            IReadOnlyList<PlayerRankedDto> ranked;
            try
            {
                ranked = await rankedTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                ranked = EmptyRanked();
            }

            if (!ranked.Any(row => row.Rating is not null)
                && rankedHint.Length >= 2
                && !string.Equals(rankedHint, lifetime.Name, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ranked = await RankedAllAsync(id, lifetime.Name, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
                {
                }
            }

            var profile = new PlayerProfileDto(
                lifetime.Id,
                lifetime.Name,
                lifetime.Level,
                lifetime.Games,
                lifetime.Wins,
                guild,
                ranked,
                MapLegends(lifetime.Legends, legendNames),
                lifetime.PlaySeconds);
            Cache[id] = new Entry(profile, DateTime.UtcNow.Add(Ttl));
            return profile;
        }
        finally
        {
            Inflight.TryRemove(id, out _);
        }
    }

    private static async Task<PlayerProfileDto> FallbackAsync(
        int id,
        string rankedHint,
        Task<string?> guildTask,
        Task<IReadOnlyList<PlayerRankedDto>> rankedTask,
        CancellationToken cancellationToken)
    {
        string? guild = null;
        try
        {
            guild = await guildTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
        }

        IReadOnlyList<PlayerRankedDto> ranked;
        try
        {
            ranked = await rankedTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            ranked = EmptyRanked();
        }

        if (!ranked.Any(row => row.Rating is not null) && rankedHint.Length >= 2)
        {
            try
            {
                ranked = await RankedAllAsync(id, rankedHint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
            }
        }

        var name = rankedHint.Length > 0 ? rankedHint : "Player " + id.ToString(CultureInfo.InvariantCulture);
        var profile = new PlayerProfileDto(id, name, null, null, null, guild, ranked, []);
        Cache[id] = new Entry(profile, DateTime.UtcNow.Add(Ttl));
        return profile;
    }

    private static async Task<Lifetime?> LifetimeAsync(int id, CancellationToken cancellationToken)
    {
        var url = "https://api.brawlhalla.com/v1/player/stats?brawlhalla_id=" + id.ToString(CultureInfo.InvariantCulture);
        using var doc = await BrawlhallaJson.GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }

        var root = doc.RootElement;
        var playerId = BrawlhallaJson.ReadInt(root, "brawlhalla_id") ?? 0;
        var playerName = BrawlhallaJson.ReadString(root, "name");
        if (playerId <= 0 || playerName.Length == 0)
        {
            return null;
        }

        var legends = new List<RawLegend>();
        if (root.TryGetProperty("legends", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in list.EnumerateArray())
            {
                var legendId = BrawlhallaJson.ReadInt(row, "legend_id") ?? 0;
                var games = BrawlhallaJson.ReadInt(row, "games") ?? 0;
                if (legendId <= 0 || games <= 0)
                {
                    continue;
                }

                legends.Add(new RawLegend(
                    legendId,
                    games,
                    BrawlhallaJson.ReadInt(row, "wins") ?? 0,
                    BrawlhallaJson.ReadInt(row, "level"),
                    BrawlhallaJson.ReadInt(row, "match_time") ?? 0));
            }
        }

        var playSeconds = legends.Sum(legend => legend.MatchTime);
        legends.Sort((a, b) => b.Games.CompareTo(a.Games));
        if (legends.Count > 8)
        {
            legends.RemoveRange(8, legends.Count - 8);
        }

        return new Lifetime(
            playerId,
            playerName,
            BrawlhallaJson.ReadInt(root, "level"),
            BrawlhallaJson.ReadInt(root, "games"),
            BrawlhallaJson.ReadInt(root, "wins"),
            legends,
            playSeconds > 0 ? playSeconds : null);
    }

    private static async Task<string?> GuildNameAsync(int id, CancellationToken cancellationToken)
    {
        var url = "https://api.brawlhalla.com/v1/player/guild?brawlhalla_id=" + id.ToString(CultureInfo.InvariantCulture);
        using var doc = await BrawlhallaJson.GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }

        var root = doc.RootElement;
        if (!root.TryGetProperty("guild", out var guild) || guild.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return BrawlhallaJson.ReadStringOrNull(guild, "guild_name");
    }

    private static async Task<IReadOnlyDictionary<int, string>> LegendNamesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await LegendCatalog.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new Dictionary<int, string>();
        }
    }

    private static async Task<IReadOnlyList<PlayerRankedDto>> RankedAllAsync(
        int id,
        string name,
        CancellationToken cancellationToken)
    {
        var hint = CleanName(name);
        // Team modes come from the leaderboard: only it names the players you queued with.
        var oneTask = RankedStatsAsync(id, "ranked_1v1", "1v1", cancellationToken);
        var twoTask = RankedOneAsync(id, hint, "2v2", cancellationToken);
        var threeTask = RankedOneAsync(id, hint, "3v3", cancellationToken);
        await Task.WhenAll(oneTask, twoTask, threeTask).ConfigureAwait(false);
        var one = await oneTask.ConfigureAwait(false);
        var two = await twoTask.ConfigureAwait(false);
        var three = await threeTask.ConfigureAwait(false);
        if (one.Rating is null && hint.Length >= 2)
        {
            one = await RankedOneAsync(id, hint, "1v1", cancellationToken).ConfigureAwait(false);
        }

        if (three.Rating is null)
        {
            var stats = await RankedStatsAsync(id, "ranked_3v3", "3v3", cancellationToken).ConfigureAwait(false);
            if (stats.Rating is not null)
            {
                three = stats;
            }
        }

        return [one, two, three];
    }

    private static async Task<PlayerRankedDto> RankedStatsAsync(
        int id,
        string mode,
        string gameMode,
        CancellationToken cancellationToken)
    {
        try
        {
            var url =
                "https://api.brawlhalla.com/v1/player/stats?brawlhalla_id="
                + id.ToString(CultureInfo.InvariantCulture)
                + "&mode="
                + Uri.EscapeDataString(mode);
            using var doc = await BrawlhallaJson.GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            if (doc is null)
            {
                return EmptyMode(gameMode);
            }

            var root = doc.RootElement;
            var games = BrawlhallaJson.ReadInt(root, "games");
            var wins = BrawlhallaJson.ReadInt(root, "wins");
            return new PlayerRankedDto(
                gameMode,
                BrawlhallaJson.ReadStringOrNull(root, "region"),
                BrawlhallaJson.ReadStringOrNull(root, "tier"),
                BrawlhallaJson.ReadInt(root, "rating"),
                BrawlhallaJson.ReadInt(root, "peak_rating"),
                BrawlhallaJson.ReadInt(root, "global_rank"),
                wins,
                LossesFrom(games, wins));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return EmptyMode(gameMode);
        }
    }

    private static async Task<PlayerRankedDto> RankedOneAsync(
        int id,
        string name,
        string gameMode,
        CancellationToken cancellationToken)
    {
        if (name.Length < 2)
        {
            return EmptyMode(gameMode);
        }

        try
        {
            var rows = await PlayerSearchClient.RankingsAsync(gameMode, name, cancellationToken).ConfigureAwait(false);
            var match = rows.FirstOrDefault(row => row.Id == id);
            if (match is null)
            {
                return EmptyMode(gameMode);
            }

            return new PlayerRankedDto(
                gameMode,
                match.Region,
                match.Tier,
                match.Rating,
                match.PeakRating,
                match.Rank,
                match.Wins,
                match.Losses,
                match.Teammates);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return EmptyMode(gameMode);
        }
    }

    private static string CleanName(string? name)
    {
        return (name ?? "").Trim().TrimStart('@').Trim();
    }

    private static PlayerRankedDto EmptyMode(string gameMode)
    {
        return new PlayerRankedDto(gameMode, null, null, null, null, null, null, null);
    }

    private static int? LossesFrom(int? games, int? wins)
    {
        if (games is null || wins is null || games < wins)
        {
            return null;
        }

        return games - wins;
    }

    private static IReadOnlyList<PlayerLegendDto> MapLegends(
        IReadOnlyList<RawLegend> legends,
        IReadOnlyDictionary<int, string> names)
    {
        return legends
            .Select(legend => new PlayerLegendDto(
                legend.Id,
                names.TryGetValue(legend.Id, out var name) ? name : "Legend " + legend.Id,
                legend.Games,
                legend.Wins,
                legend.Level))
            .ToList();
    }

    private static IReadOnlyList<PlayerRankedDto> EmptyRanked()
    {
        return PlayerSearchClient.Modes
            .Select(EmptyMode)
            .ToList();
    }

    private sealed record Lifetime(
        int Id,
        string Name,
        int? Level,
        int? Games,
        int? Wins,
        IReadOnlyList<RawLegend> Legends,
        int? PlaySeconds);

    private sealed record RawLegend(int Id, int Games, int Wins, int? Level, int MatchTime);

    private readonly record struct Entry(PlayerProfileDto Profile, DateTime ExpiresUtc);
}
