namespace BrawlEngine.Host.Domain.Models;

public sealed record ApplyAttemptDto(bool Applied, string? Reason);

/// <summary>Pushed while Apply / Reset runs so the card can show a percent.</summary>
public sealed record ApplyProgressDto(
    string Kind,
    int Id,
    string Action,
    int Done,
    int Total,
    string? Name = null);
