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

        // One delivery log with the automatic messages (auto-messages.md, 2026-10-15): every chat the
        // button tries is a row of the absence rule for that day, marked "manual" with the staff member.
        var period = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var noTelegramIds = rows.Where(r => !r.Telegram && r.SentAt is null).Select(r => r.StudentId).ToList();
        var logIds = ids.Concat(noTelegramIds).ToList();
        var logged = await db.AutoMessageLog
            .Where(l => l.RuleKey == AutoMessageKind.Absence && l.Period == period && logIds.Contains(l.StudentId))
            .ToListAsync(ct);

        int sent = 0, failed = 0;
        foreach (var row in pending)
        {
            var text = BuildMessage(row.FullName, day, row.ReasonName);
            var delivered = 0;
            foreach (var chat in chats.GetValueOrDefault(row.StudentId) ?? [])
            {
                var ok = await telegram.SendMessageAsync(chat, text, ct: ct);
                if (ok) delivered++;
                var entry = logged.FirstOrDefault(l => l.StudentId == row.StudentId && l.ChatId == chat);
                if (entry is null)
                {
                    entry = new AutoMessageLogEntry { RuleKey = AutoMessageKind.Absence, Period = period, StudentId = row.StudentId, ChatId = chat };
                    db.AutoMessageLog.Add(entry);
                    logged.Add(entry);
                }
                entry.Status = ok ? BroadcastDelivery.Sent : BroadcastDelivery.Failed;
                entry.Text = TelegramHtml.Escape(text);
                entry.Source = AutoMessageSource.Manual;
                entry.SentBy = userId;
                entry.Attempts++;
                entry.Error = ok ? null : "Telegram xabarni qabul qilmadi";
                entry.At = AppClock.NowInstant;
            }
            if (delivered == 0) { failed++; continue; }
            db.AbsenceNotices.Add(new AbsenceNotice
            {
                StudentId = row.StudentId, Date = day, ReasonId = reasonIdOf.GetValueOrDefault(row.StudentId),
                Chats = delivered, SentBy = userId, SentAt = AppClock.NowInstant,
            });
            sent++;
        }
        foreach (var id in noTelegramIds.Where(id => !logged.Any(l => l.StudentId == id && l.ChatId == null)))
            db.AutoMessageLog.Add(new AutoMessageLogEntry
            {
                RuleKey = AutoMessageKind.Absence, Period = period, StudentId = id, ChatId = null,
                Status = BroadcastDelivery.NoTelegram, Source = AutoMessageSource.Manual, SentBy = userId,
                At = AppClock.NowInstant,
            });
        await db.SaveChangesAsync(ct);
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
    /// <param name="periods">Faqat shu dars raqamlari hisoblanadi (avto-xabarning kesish vaqti); null — hammasi.</param>
    internal async Task<(List<AbsenceNoticeRowDto> Rows, Dictionary<string, string?> ReasonIdOf)> RowsAsync(
        DateOnly day, CancellationToken ct, IReadOnlySet<int>? periods = null)
    {
        var iso = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var report = await AttendanceAnalytics.BuildAsync(db, null, iso, iso, iso, AttendanceAnalyticsExport.MaxEvents, periods: periods);
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
