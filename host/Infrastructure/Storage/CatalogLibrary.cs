using System.Text.Json;
using System.Text.Json.Serialization;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.GameBanana;
using BrawlEngine.Host.Infrastructure.Networking;

namespace BrawlEngine.Host.Infrastructure.Storage;

/// <summary>
/// Saved catalog cards for On disk / Applied. Lives next to downloads, not inside them,
/// so Delete still leaves a name and thumbnail for an applied mod.
/// </summary>
public static class CatalogLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static CatalogItemDto? TryRead(string kind, int id)
    {
        var folder = TryFolder(kind, id);
        if (folder is null)
        {
            return null;
        }

        var path = Path.Combine(folder, "item.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var file = JsonSerializer.Deserialize<LibraryFile>(File.ReadAllText(path), JsonOptions);
            if (file is null || file.Id <= 0 || string.IsNullOrWhiteSpace(file.Name))
            {
                return null;
            }

            if (file.Name.StartsWith("Item ", StringComparison.Ordinal))
            {
                return null;
            }

            var thumb = AppThumbUrl(kind, file.Id, folder, file.LocalThumb);
            return new CatalogItemDto(
                file.Id,
                file.Name,
                file.Author ?? "",
                thumb,
                file.Category ?? "",
                file.ProfileUrl ?? "",
                file.SkinTarget,
                file.Description,
                file.Nsfw || CatalogSearch.LooksNsfw(file.Name, file.Description),
                file.AuthorUrl,
                file.AuthorId,
                file.AuthorAvatarUrl,
                file.LikeCount,
                file.DownloadCount);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool NeedsAuthorRefresh(CatalogItemDto item)
    {
        return item.Id > 0 && item.AuthorId is not > 0;
    }

    public static void Remember(string kind, CatalogItemDto item)
    {
        if (item.Id <= 0 || string.IsNullOrWhiteSpace(item.Name))
        {
            return;
        }

        if (item.Name.StartsWith("Item ", StringComparison.Ordinal))
        {
            return;
        }

        if (TryRead(kind, item.Id) is null && !HasDownload(kind, item.Id))
        {
            return;
        }

        try
        {
            WriteMeta(kind, item, localThumb: null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (HasLocalThumb(kind, item.Id))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await SaveFetchedAsync(kind, item, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        });
    }

    /// <summary>Keep a card for Liked even when the archive is not on disk.</summary>
    public static void RememberCard(string kind, CatalogItemDto item)
    {
        if (item.Id <= 0 || string.IsNullOrWhiteSpace(item.Name))
        {
            return;
        }

        if (item.Name.StartsWith("Item ", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            WriteMeta(kind, item, localThumb: null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (HasLocalThumb(kind, item.Id))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await SaveFetchedAsync(kind, item, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        });
    }

    public static bool HasLocalThumb(string kind, int id)
    {
        var folder = TryFolder(kind, id);
        if (folder is null)
        {
            return false;
        }

        return ResolveLocalThumb(folder, ReadFile(folder)?.LocalThumb) is not null;
    }

    public static async Task EnsureLocalThumbAsync(string kind, int id, CancellationToken cancellationToken)
    {
        var folder = TryFolder(kind, id);
        if (folder is null)
        {
            return;
        }

        var file = ReadFile(folder);
        if (file is null)
        {
            return;
        }

        var item = new CatalogItemDto(
            file.Id,
            file.Name,
            file.Author ?? "",
            file.ThumbnailUrl,
            file.Category ?? "",
            file.ProfileUrl ?? "",
            file.SkinTarget,
            file.Description,
            file.Nsfw || CatalogSearch.LooksNsfw(file.Name, file.Description),
            file.AuthorUrl,
            file.AuthorId,
            file.AuthorAvatarUrl,
            file.LikeCount,
            file.DownloadCount);
        await Write(kind, item, fetchThumb: true, cancellationToken).ConfigureAwait(false);
    }

    public static Stream? OpenThumb(string url, out string contentType)
    {
        contentType = "application/octet-stream";
        if (!TryParseAppLibraryUrl(url, out var kind, out var id))
        {
            return null;
        }

        var folder = TryFolder(kind, id);
        if (folder is null)
        {
            return null;
        }

        var name = ResolveLocalThumb(folder, ReadFile(folder)?.LocalThumb);
        if (name is null)
        {
            return null;
        }

        var path = Path.Combine(folder, name);
        try
        {
            contentType = ThumbContentType(name);
            return new MemoryStream(File.ReadAllBytes(path), writable: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task SaveFromProfileAsync(
        string kind,
        JsonElement profile,
        CancellationToken cancellationToken)
    {
        var item = CatalogSearch.FromRecord(profile, DefaultCategory(kind));
        if (item.Id <= 0 || string.IsNullOrWhiteSpace(item.Name))
        {
            return;
        }

        await Write(kind, item, fetchThumb: true, cancellationToken).ConfigureAwait(false);
    }

    public static async Task SaveFetchedAsync(
        string kind,
        CatalogItemDto item,
        CancellationToken cancellationToken)
    {
        if (item.Id <= 0 || string.IsNullOrWhiteSpace(item.Name))
        {
            return;
        }

        if (item.Name.StartsWith("Item ", StringComparison.Ordinal))
        {
            return;
        }

        await Write(kind, item, fetchThumb: true, cancellationToken).ConfigureAwait(false);
    }

    private static async Task Write(
        string kind,
        CatalogItemDto item,
        bool fetchThumb,
        CancellationToken cancellationToken)
    {
        var folder = TryFolder(kind, item.Id);
        if (folder is null)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var existing = ReadFile(folder);
        var localThumb = existing?.LocalThumb;
        if (fetchThumb && (localThumb is null || !File.Exists(Path.Combine(folder, localThumb))))
        {
            localThumb = await TryDownloadThumbAsync(folder, item.ThumbnailUrl, cancellationToken)
                .ConfigureAwait(false) ?? localThumb;
        }

        WriteMeta(kind, item, localThumb);
    }

    private static void WriteMeta(string kind, CatalogItemDto item, string? localThumb)
    {
        var folder = TryFolder(kind, item.Id);
        if (folder is null)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var existing = ReadFile(folder);
        localThumb ??= existing?.LocalThumb;

        var file = new LibraryFile
        {
            Id = item.Id,
            Name = Prefer(item.Name, existing?.Name) ?? "",
            Author = Prefer(item.Author, existing?.Author) ?? "",
            ThumbnailUrl = item.ThumbnailUrl ?? existing?.ThumbnailUrl,
            LocalThumb = localThumb,
            Category = Prefer(item.Category, existing?.Category) ?? "",
            ProfileUrl = Prefer(item.ProfileUrl, existing?.ProfileUrl) ?? "",
            AuthorUrl = Prefer(item.AuthorUrl, existing?.AuthorUrl),
            AuthorId = item.AuthorId is > 0 ? item.AuthorId : existing?.AuthorId,
            AuthorAvatarUrl = Prefer(item.AuthorAvatarUrl, existing?.AuthorAvatarUrl),
            LikeCount = item.LikeCount > 0 ? item.LikeCount : existing?.LikeCount ?? 0,
            DownloadCount = item.DownloadCount > 0 ? item.DownloadCount : existing?.DownloadCount ?? 0,
            SkinTarget = item.SkinTarget ?? existing?.SkinTarget,
            Description = item.Description ?? existing?.Description,
            Nsfw = item.Nsfw || existing?.Nsfw == true
                || CatalogSearch.LooksNsfw(Prefer(item.Name, existing?.Name), item.Description ?? existing?.Description),
        };

        File.WriteAllText(Path.Combine(folder, "item.json"), JsonSerializer.Serialize(file, JsonOptions));
    }

    private static async Task<string?> TryDownloadThumbAsync(
        string folder,
        string? url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(TimeSpan.FromSeconds(12));
            using var response = await AppHttp.Shared.GetAsync(url, linked.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(linked.Token).ConfigureAwait(false);
            if (bytes.Length < 32 || bytes.Length > 800_000)
            {
                return null;
            }

            var name = "thumb" + ThumbExtension(bytes);
            await File.WriteAllBytesAsync(Path.Combine(folder, name), bytes, cancellationToken).ConfigureAwait(false);
            return name;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException
                or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string ThumbExtension(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return ".jpg";
        }

        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50)
        {
            return ".png";
        }

        if (bytes.Length >= 6 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I')
        {
            return ".gif";
        }

        if (bytes.Length >= 12
            && bytes[0] == (byte)'R'
            && bytes[8] == (byte)'W'
            && bytes[9] == (byte)'E'
            && bytes[10] == (byte)'B'
            && bytes[11] == (byte)'P')
        {
            return ".webp";
        }

        return ".img";
    }

    private static string? AppThumbUrl(string kind, int id, string folder, string? localThumb)
    {
        if (ResolveLocalThumb(folder, localThumb) is null)
        {
            return null;
        }

        return "app://library/" + kind + "/" + id;
    }

    private static string? ResolveLocalThumb(string folder, string? recorded)
    {
        if (!string.IsNullOrEmpty(recorded))
        {
            var name = Path.GetFileName(recorded);
            if (name.StartsWith("thumb.", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(folder, name)))
            {
                return name;
            }
        }

        return FindThumbFile(folder);
    }

    private static string? FindThumbFile(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "thumb.*")
                .Select(Path.GetFileName)
                .FirstOrDefault(name =>
                    name is not null && name.StartsWith("thumb.", StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool TryParseAppLibraryUrl(string url, out string kind, out int id)
    {
        kind = "";
        id = 0;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var colon = url.IndexOf(':');
        if (colon < 0)
        {
            return false;
        }

        var rest = url[(colon + 1)..].TrimStart('/');
        var q = rest.IndexOf('?');
        if (q >= 0)
        {
            rest = rest[..q];
        }

        var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3
            || !parts[0].Equals("library", StringComparison.OrdinalIgnoreCase)
            || parts[1] is not ("maps" or "sounds" or "skins")
            || !int.TryParse(parts[2], out id)
            || id <= 0)
        {
            return false;
        }

        kind = parts[1];
        return true;
    }

    private static string ThumbContentType(string name)
    {
        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return "image/png";
        }

        if (name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            return "image/gif";
        }

        if (name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            return "image/webp";
        }

        return "image/jpeg";
    }

    private static LibraryFile? ReadFile(string folder)
    {
        var path = Path.Combine(folder, "item.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LibraryFile>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private static string? TryFolder(string kind, int id)
    {
        if (id <= 0)
        {
            return null;
        }

        if (kind is not ("maps" or "sounds" or "skins"))
        {
            return null;
        }

        return AppPaths.LibraryFolder(kind, id);
    }

    private static bool HasDownload(string kind, int id)
    {
        var folder = kind switch
        {
            "sounds" => AppPaths.SoundDownloadsFolder(id),
            "skins" => AppPaths.SkinDownloadsFolder(id),
            "maps" => AppPaths.DownloadsFolder(id),
            _ => null,
        };
        if (folder is null || !Directory.Exists(folder))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any();
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string DefaultCategory(string kind) =>
        kind switch
        {
            "sounds" => "Sounds",
            "skins" => GameBananaIds.SkinsCategoryName,
            _ => GameBananaIds.RealmsCategoryName,
        };

    private static string? Prefer(string? next, string? previous)
    {
        if (!string.IsNullOrWhiteSpace(next) && !next.StartsWith("Item ", StringComparison.Ordinal))
        {
            return next;
        }

        return previous;
    }

    private sealed class LibraryFile
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Author { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? LocalThumb { get; set; }
        public string? Category { get; set; }
        public string? ProfileUrl { get; set; }
        public string? AuthorUrl { get; set; }
        public int? AuthorId { get; set; }
        public string? AuthorAvatarUrl { get; set; }
        public int LikeCount { get; set; }
        public int DownloadCount { get; set; }
        public string? SkinTarget { get; set; }
        public string? Description { get; set; }
        public bool Nsfw { get; set; }
    }
}
