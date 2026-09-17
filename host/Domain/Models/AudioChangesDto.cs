namespace BrawlEngine.Host.Domain.Models;

public sealed record AudioChangeEntryDto(string FileName, string Label, string? Source, string At);

public sealed record AudioChangesDto(IReadOnlyList<AudioChangeEntryDto> Entries)
{
    public static AudioChangesDto Empty { get; } = new([]);
}
