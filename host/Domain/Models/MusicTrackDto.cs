namespace BrawlEngine.Host.Domain.Models;

public sealed record MusicTrackDto(
    string FileName,
    string Label,
    string Slot,
    bool Changed = false,
    string? Source = null,
    string? ChangedAt = null);

public sealed record MusicTracksDto(
    IReadOnlyList<MusicTrackDto> Tracks,
    IReadOnlyList<string> OtherChanges,
    int OtherCount);
