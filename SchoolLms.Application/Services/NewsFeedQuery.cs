using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Yangiliklar lentasi — o'quvchi, ota-ona va xodim O'QIYDIGAN tomon.
//  Spetsifikatsiya: docs/modules/sales-marketing.md §5.5. Vazifa: SM-6.
//
//  To'rtta endpoint, bitta so'rov: faqat e'lon qilingan
//  (`published_at is not null`), o'chirilmagan (`deleted_at is null`) va shu
//  o'quvchi auditoriyasiga mos yozuvlar, eng yangisi birinchi. So'rov
//  `ix_news_feed (published_at desc) where deleted_at is null` indeksiga
//  tushadi.
//
//  DTO ATAYLAB KICHIK: auditoriya massivi, hisoblagichlar va muallif id'si
//  YO'Q. Ota-onaga xabarni yana kimlar olgani kerak emas (§5.5).
// ===========================================================================

/// <summary>Lenta yozuvi — admin DTO'sidan ataylab kichikroq (§5.5).</summary>
public record NewsFeedDto(
    Guid Id, string Title, string Body, string? ImageUrl, DateTimeOffset PublishedAt, string AuthorName);

/// <summary>Qaysi auditoriya lentasi o'qilyapti.</summary>
public enum NewsFeedAudience { Employee, Parent, Student }

public static class NewsFeedQuery
{
    /// <summary><c>?take=</c> sukut qiymati (§5.5).</summary>
    public const int DefaultTake = 20;

    /// <summary><c>?take=</c> ning yuqori chegarasi (§5.5).</summary>
    public const int MaxTake = 50;

    public static async Task<IReadOnlyList<NewsFeedDto>> ListAsync(
        IAppDbContext db, NewsFeedAudience audience, int? take, CancellationToken ct = default)
    {
        var n = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

        var q = db.News.AsNoTracking()
            .Where(x => x.PublishedAt != null && x.DeletedAt == null);
        q = audience switch
        {
            NewsFeedAudience.Employee => q.Where(x => x.ForEmployee),
            NewsFeedAudience.Parent => q.Where(x => x.ForParent),
            _ => q.Where(x => x.ForStudent),
        };

        return await q
            .OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id)
            .Take(n)
            .Select(x => new NewsFeedDto(
                x.Id, x.Title, x.Body, x.ImageUrl, x.PublishedAt!.Value, x.AuthorName))
            .ToListAsync(ct);
    }
}
