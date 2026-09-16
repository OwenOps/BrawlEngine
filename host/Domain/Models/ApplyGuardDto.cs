namespace BrawlEngine.Host.Domain.Models;

public sealed record ApplyGuardDto(bool Allowed, bool Running, string? Message, int Pending = 0);
