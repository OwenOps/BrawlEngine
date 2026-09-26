namespace BrawlEngine.Host.Domain.Models;

public sealed record PlayerTeammateDto(int Id, string Name);

public sealed record PlayerSearchMatchDto(
    int Id,
    string Name,
    string GameMode,
    string? Region,
    string? Tier,
    int? Rating,
    int? PeakRating,
    int? Rank,
    int? Wins,
    int? Losses,
    IReadOnlyList<PlayerTeammateDto>? Teammates = null);

public sealed record PlayerSearchPageDto(
    IReadOnlyList<PlayerSearchMatchDto> Matches,
    string Query,
    string GameMode,
    int TotalPages = 1,
    string? Warning = null,
    string? LogPath = null);

public sealed record PlayerRankedDto(
    string GameMode,
    string? Region,
    string? Tier,
    int? Rating,
    int? PeakRating,
    int? Rank,
    int? Wins,
    int? Losses,
    IReadOnlyList<PlayerTeammateDto>? Teammates = null);

public sealed record PlayerLegendDto(
    int Id,
    string Name,
    int Games,
    int Wins,
    int? Level);

public sealed record PlayerProfileDto(
    int Id,
    string Name,
    int? Level,
    int? Games,
    int? Wins,
    string? GuildName,
    IReadOnlyList<PlayerRankedDto> Ranked,
    IReadOnlyList<PlayerLegendDto> Legends,
    int? PlaySeconds = null);
