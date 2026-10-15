using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  KELMAGANLARNING OTA-ONALARIGA XABAR — BITTA TUGMA (2026-10-11)
// ===========================================================================
//
//  Mijoz: "Hamshira davomat qilib bo'lgandan so'ng bir tugmani bosish orqali kelmagan o'quvchini
//  ota onasiga o'quvchi kelmaganligi to'g'risida avtomatik habar yuborish."
//
//  · KIM — o'sha kuni KUN BO'YI kelmaganlar (StudentDay.Absent: belgilangan har bir darsda yo'q) —
//    "Davomat belgilash" ekranidagi "N o'quvchi kelmadi" bilan bir xil ta'rif. Ayrim darslarga
//    kelmagan (partial) va kech kelgan bu xabarni olmaydi: bola maktabda bo'lgan.
//  · BIR MARTA — bitta o'quvchiga bir kunda bitta xabar (`absence_notices`, unikal indeks);
//    tugma qayta bosilsa faqat hali xabar olmaganlarga ketadi.
//  · QAYERGA — `telegram_registrations` dagi ota-ona chatlari (e'lon va chek bilan bir xil).
//    Ulanmagan ota-ona ro'yxatda ko'rinadi, lekin xabar bormaydi.
// ===========================================================================

/// <summary>Ro'yxatdagi bitta kelmagan o'quvchi.</summary>
/// <param name="Telegram">Ota-onasi Telegramga ulangan (xabar yetadi).</param>
/// <param name="SentAt">Shu kun uchun xabar allaqachon yuborilgan bo'lsa — qachon.</param>
public sealed record AbsenceNoticeRowDto(
    string StudentId, string FullName, string RowName, string? ReasonName, bool Unexcused,
    bool Telegram, DateTimeOffset? SentAt);

/// <summary>Tugma bosilishidan oldingi ko'rinish.</summary>
/// <param name="Ready">Hozir xabar ketadiganlar: kelmagan, ota-onasi ulangan, hali yuborilmagan.</param>
public sealed record AbsenceNoticePreviewDto(
    string Date, bool BotConfigured, List<AbsenceNoticeRowDto> Rows, int Ready, int AlreadySent, int NoTelegram);

/// <summary>Yuborish natijasi.</summary>
public sealed record AbsenceNoticeResultDto(int Sent, int AlreadySent, int NoTelegram, int Failed);

/// <inheritdoc cref="AbsenceNoticeRowDto"/>
public sealed class DailyAbsenceNotifier(IAppDbContext db, TelegramService telegram, ILogger<DailyAbsenceNotifier> logger)
{
    public async Task<AbsenceNoticePreviewDto> PreviewAsync(string date, CancellationToken ct = default)
    {
        var day = ParseDate(date);
        var (rows, _) = await RowsAsync(day, ct);
        return new AbsenceNoticePreviewDto(
            date, telegram.IsConfigured, rows,
            rows.Count(r => r.Telegram && r.SentAt is null),
            rows.Count(r => r.SentAt is not null),
            rows.Count(r => !r.Telegram && r.SentAt is null));
    }

    /// <param name="userId">Yuborayotgan xodim (JWT).</param>
    public async Task<AbsenceNoticeResultDto> SendAsync(string date, string userId, CancellationToken ct = default)
    {
        var day = ParseDate(date);
        if (day > AppClock.Today)
            throw new InvalidOperationException("Kelajakdagi kun uchun xabar yuborilmaydi.");
        if (!telegram.IsConfigured)
            throw new InvalidOperationException("Telegram bot sozlanmagan — Sozlamalar → Integratsiyalar.");

        var (rows, reasonIdOf) = await RowsAsync(day, ct);
        var pending = rows.Where(r => r.SentAt is null && r.Telegram).ToList();
        var ids = pending.Select(r => r.StudentId).ToList();
        var chats = (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => ids.Contains(r.StudentId) && r.TeacherId == null)
                .Select(r => new { r.StudentId, r.ChatId }).ToListAsync(ct))
            .GroupBy(x => x.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ChatId).Distinct().ToList(), StringComparer.Ordinal);

        int sent = 0, failed = 0;
        foreach (var row in pending)
        {
            var text = BuildMessage(row.FullName, day, row.ReasonName);
            var delivered = 0;
            foreach (var chat in chats.GetValueOrDefault(row.StudentId) ?? [])
                if (await telegram.SendMessageAsync(chat, text, ct: ct)) delivered++;
            if (delivered == 0) { failed++; continue; }
            db.AbsenceNotices.Add(new AbsenceNotice
            {
                StudentId = row.StudentId, Date = day, ReasonId = reasonIdOf.GetValueOrDefault(row.StudentId),
                Chats = delivered, SentBy = userId, SentAt = AppClock.NowInstant,
            });
            sent++;
        }
        if (sent > 0) await db.SaveChangesAsync(ct);
        logger.LogInformation("Kelmaganlik xabari: {Date}, yuborildi={Sent}, oldin={Already}, ulanmagan={NoTelegram}, xato={Failed}",
            date, sent, rows.Count(r => r.SentAt is not null), rows.Count(r => !r.Telegram && r.SentAt is null), failed);
        return new AbsenceNoticeResultDto(
            sent, rows.Count(r => r.SentAt is not null), rows.Count(r => !r.Telegram && r.SentAt is null), failed);
    }

    /// <summary>Ota-ona o'qiydigan matn.</summary>
    public static string BuildMessage(string fullName, DateOnly day, string? reason)
    {
        var date = day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var text = $"📋 Davomat\n\nHurmatli ota-ona, farzandingiz {fullName} {date} kuni maktabga kelmadi.";
        return string.IsNullOrWhiteSpace(reason) ? text : $"{text}\nSabab: {reason}";
    }

    /// <summary>Kun bo'yi kelmaganlar — sinf, sabab, Telegram va oldin yuborilganmi bilan.</summary>
    internal async Task<(List<AbsenceNoticeRowDto> Rows, Dictionary<string, string?> ReasonIdOf)> RowsAsync(
        DateOnly day, CancellationToken ct)
    {
        var iso = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var report = await AttendanceAnalytics.BuildAsync(db, null, iso, iso, iso, AttendanceAnalyticsExport.MaxEvents);
        var absent = (report.Events ?? []).Where(e => e.Status == StudentDay.Absent && e.Date == iso).ToList();
        var ids = absent.Select(e => e.StudentId).ToList();

        var reasonName = report.Reasons.ToDictionary(r => r.ReasonId, r => r.Name, StringComparer.Ordinal);
        var withTelegram = (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => ids.Contains(r.StudentId) && r.TeacherId == null)
                .Select(r => r.StudentId).Distinct().ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var sentAt = await db.AbsenceNotices.AsNoTracking()
            .Where(n => n.Date == day && ids.Contains(n.StudentId))
            .ToDictionaryAsync(n => n.StudentId, n => n.SentAt, ct);

        var rows = absent
            .Select(e =>
            {
                var reasonId = e.ReasonIds.FirstOrDefault();
                return new AbsenceNoticeRowDto(
                    e.StudentId, e.FullName, e.RowName,
                    reasonId is null ? null : reasonName.GetValueOrDefault(reasonId),
                    e.Unexcused, withTelegram.Contains(e.StudentId),
                    sentAt.TryGetValue(e.StudentId, out var at) ? at : null);
            })
            .OrderBy(r => r.RowName, StringComparer.Ordinal).ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();
        var reasonIdOf = absent.ToDictionary(e => e.StudentId, e => e.ReasonIds.FirstOrDefault(), StringComparer.Ordinal);
        return (rows, reasonIdOf);
    }

    private static DateOnly ParseDate(string date) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new ArgumentException("Sana formati: YYYY-MM-DD", nameof(date));
}
