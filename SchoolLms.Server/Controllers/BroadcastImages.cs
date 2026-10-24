using System.Text.RegularExpressions;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Pictures of an announcement (client, 2026-10-24: "e'lon jo'natish qismida xabarlarga rasm qo'shib
/// jo'natish"). The panel first uploads each one through <c>POST /api/admin/uploads</c>, then sends
/// the returned <c>/uploads/…</c> URLs with the announcement. Only a name that upload produced
/// (<see cref="UploadGuard.SafeName"/>: 32 hex + extension) is accepted — no path from the caller
/// ever reaches the file system.
/// </summary>
internal static partial class BroadcastImages
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

    /// <summary>The URLs as given (trimmed, blanks dropped) → the pictures, or an Uzbek error.</summary>
    public static async Task<(List<TelegramPhoto> Photos, string? Error)> LoadAsync(
        IWebHostEnvironment env, IReadOnlyList<string> urls, CancellationToken ct)
    {
        var photos = new List<TelegramPhoto>(urls.Count);
        if (urls.Count > TelegramService.MaxPhotos)
            return (photos, $"Bitta e'longa ko'pi bilan {TelegramService.MaxPhotos} ta rasm qo'shish mumkin");
        foreach (var url in urls)
        {
            var m = UploadUrl().Match(url);
            if (!m.Success || !Types.TryGetValue(m.Groups[2].Value, out var contentType))
                return (photos, "Rasm JPG, PNG yoki WEBP bo'lsin");
            var name = $"{m.Groups[1].Value}.{m.Groups[2].Value}";
            var path = Path.Combine(env.ContentRootPath, "uploads", name);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0) return (photos, "Rasm topilmadi — qaytadan qo'shing");
            if (info.Length > MaxBytes) return (photos, "Rasm 10 MB dan katta bo'lmasin");
            photos.Add(new TelegramPhoto(name, contentType, await File.ReadAllBytesAsync(path, ct)));
        }
        return (photos, null);
    }
}
