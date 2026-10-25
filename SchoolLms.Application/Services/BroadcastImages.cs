using System.Text.RegularExpressions;

namespace SchoolLms.Application.Services;

/// <summary>
/// Pictures of an announcement (client, 2026-10-24: "e'lon jo'natish qismida xabarlarga rasm qo'shib
/// jo'natish") and, since 2026-10-25, of an automatic message rule. The panel first uploads each one
/// through <c>POST /api/admin/uploads</c>, then sends the returned <c>/uploads/…</c> URLs. Only a name
/// that upload produced (<see cref="UploadGuard.SafeName"/>: 32 hex + extension) is accepted — no path
/// from the caller ever reaches the file system.
/// </summary>
public static partial class BroadcastImages
{
    /// <summary>Telegram's <c>sendPhoto</c> limit.</summary>
    public const long MaxBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["png"] = "image/png",
        ["webp"] = "image/webp",
    };

    [GeneratedRegex(@"^/uploads/([0-9a-f]{32})\.([a-z]+)$")]
    private static partial Regex UploadUrl();

    /// <summary>The URLs as given → trimmed, blanks dropped (the order is kept).</summary>
    public static List<string> Clean(IEnumerable<string?>? urls) =>
        [.. (urls ?? []).Select(u => (u ?? "").Trim()).Where(u => u.Length > 0)];

    /// <summary>The URLs as given (trimmed, blanks dropped) → the pictures, or an Uzbek error.</summary>
    public static async Task<(List<TelegramPhoto> Photos, string? Error)> LoadAsync(
        IWebHostEnvironment env, IReadOnlyList<string> urls, CancellationToken ct)
    {
        var photos = new List<TelegramPhoto>(urls.Count);
        var (files, error) = Resolve(env, urls);
        if (error is not null) return (photos, error);
        foreach (var f in files)
            photos.Add(new TelegramPhoto(f.Name, f.ContentType, await File.ReadAllBytesAsync(f.Path, ct)));
        return (photos, null);
    }

    /// <summary>The same checks as <see cref="LoadAsync"/> without reading the files; null — all fine.</summary>
    public static string? Check(IWebHostEnvironment env, IReadOnlyList<string> urls) => Resolve(env, urls).Error;

    private static (List<(string Name, string ContentType, string Path)> Files, string? Error) Resolve(
        IWebHostEnvironment env, IReadOnlyList<string> urls)
    {
        var files = new List<(string, string, string)>(urls.Count);
        if (urls.Count > TelegramService.MaxPhotos)
            return (files, $"Bitta xabarga ko'pi bilan {TelegramService.MaxPhotos} ta rasm qo'shish mumkin");
        foreach (var url in urls)
        {
            var m = UploadUrl().Match(url);
            if (!m.Success || !Types.TryGetValue(m.Groups[2].Value, out var contentType))
                return (files, "Rasm JPG, PNG yoki WEBP bo'lsin");
            var name = $"{m.Groups[1].Value}.{m.Groups[2].Value}";
            var path = Path.Combine(env.ContentRootPath, "uploads", name);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0) return (files, "Rasm topilmadi — qaytadan qo'shing");
            if (info.Length > MaxBytes) return (files, "Rasm 10 MB dan katta bo'lmasin");
            files.Add((name, contentType, path));
        }
        return (files, null);
    }
}
