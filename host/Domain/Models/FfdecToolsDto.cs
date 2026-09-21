namespace BrawlEngine.Host.Domain.Models;

public sealed record FfdecToolsDto(
    bool Ready,
    string? JavaPath,
    string? JarPath,
    string? Error);
