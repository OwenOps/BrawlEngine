namespace BrawlEngine.Host.Domain.Models;

public sealed record AppInfoDto(string MadeBy, string Version);

public sealed record AppUpdateDto(bool Available, string? Latest = null, string? Url = null);
