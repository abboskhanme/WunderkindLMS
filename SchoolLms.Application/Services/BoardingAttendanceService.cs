using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  KECHKI DARS VA YOTOQXONA DAVOMATI (mijoz, 2026-09-23)
//
//  Kunduzgi davomat (jurnal, dars-ma-dars) TEGILMAYDI. Bu ikki sessiya kuniga bir marta,
//  ikki xil odam (ruxsatlar: attendanceEvening, attendanceDorm) tomonidan olinadi.
//
//  KIMDAN OLINADI — shu kuni FAOL yotoqxona abonementi borlardan
//  (`fee_categories.code = 'dormitory'`, starts_on <= kun <= ends_on). Kechki darsga ham faqat
//  yotoqxonadagilar keladi (mijoz). Abonementsizlar ro'yxatda kulrang, saqlashda RAD ETILADI.
//
//  RO'YXAT — yo'nalish guruhlari bo'yicha (`study_groups.is_track`). Hech qaysi yo'nalish
//  guruhida bo'lmagan yotoqxona o'quvchisi "Guruhsiz" — o'z sinfi bo'limida.
//
//  OTA-ONAGA XABAR — "kelmadi" (kechki) yoki "yo'q" (yotoqxona) belgilanganda, Telegram orqali,
//  bir holat uchun BIR marta (`notified_at`). Sababli/ruxsat bilan — xabarsiz.
// ===========================================================================

public sealed class BoardingAttendanceService(
    IAppDbContext db, TelegramService telegram, ILogger<BoardingAttendanceService> logger)
{
    public const string DormitoryCategoryCode = "dormitory";

    /// <summary>Shu kuni faol yotoqxona abonementi bor (arxivlanmagan) o'quvchilar.</summary>
    public async Task<HashSet<string>> EligibleAsync(DateOnly date, CancellationToken ct = default)
    {
        var ids = await (
            from s in db.StudentSubscriptions.AsNoTracking()
            join c in db.FeeCategories.AsNoTracking() on s.CategoryId equals c.Id
            join st in db.Students.AsNoTracking() on s.StudentId equals st.Id
            where c.Code == DormitoryCategoryCode
                  && s.StartsOn <= date && (s.EndsOn == null || s.EndsOn >= date)
                  && !st.IsArchived
            select s.StudentId).Distinct().ToListAsync(ct);
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<BoardingDayDto> DayAsync(DateOnly date, string session, CancellationToken ct = default)
    {
        var eligible = await EligibleAsync(date, ct);
        var marks = await db.BoardingAttendance.AsNoTracking()
            .Where(a => a.Date == date && a.Session == session)
            .ToDictionaryAsync(a => a.StudentId, a => (a.Status, a.ReasonId), ct);

        // Yo'nalish guruhlari va ularning faol a'zolari.
        var tracks = await db.StudyGroups.AsNoTracking()
            .Where(g => g.IsTrack && !g.IsArchived)
            .OrderBy(g => g.Name)
            .Select(g => new { g.Id, g.Name })
            .ToListAsync(ct);
        var trackIds = tracks.Select(t => t.Id).ToList();
        var members = await (
            from m in db.StudyGroupMembers.AsNoTracking()
            join st in db.Students.AsNoTracking() on m.StudentId equals st.Id
            where trackIds.Contains(m.GroupId) && m.LeftOn == null && !st.IsArchived
            select new { m.GroupId, st.Id, st.FullName, st.ClassName }).ToListAsync(ct);

        BoardingStudentDto Row(string id, string name, string? cls) =>
            new(id, name, cls ?? "", eligible.Contains(id),
                marks.TryGetValue(id, out var m) ? m.Status : null,
                marks.TryGetValue(id, out var r) ? r.ReasonId : null);

        var sections = new List<BoardingSectionDto>();
        var inTrack = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in tracks)
        {
            var rows = members.Where(m => m.GroupId == t.Id)
                .OrderBy(m => m.FullName, StringComparer.Ordinal)
                .Select(m => Row(m.Id, m.FullName, m.ClassName)).ToList();
            rows.ForEach(r => inTrack.Add(r.StudentId));
            sections.Add(Section(t.Id.ToString(), t.Name, "group", rows));
        }

        // Guruhsiz yotoqxona o'quvchilari — sinf bo'yicha (faqat abonementi borlar).
        var loose = eligible.Where(id => !inTrack.Contains(id)).ToList();
        if (loose.Count > 0)
        {
            var students = await db.Students.AsNoTracking()
                .Where(s => loose.Contains(s.Id))
                .Select(s => new { s.Id, s.FullName, s.ClassName })
                .ToListAsync(ct);
            foreach (var g in students.GroupBy(s => string.IsNullOrWhiteSpace(s.ClassName) ? "" : s.ClassName)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var rows = g.OrderBy(s => s.FullName, StringComparer.Ordinal)
                    .Select(s => Row(s.Id, s.FullName, s.ClassName)).ToList();
                var title = g.Key == "" ? "Guruhsiz · sinfsiz" : $"Guruhsiz · {g.Key}";
                sections.Add(Section("class:" + g.Key, title, "class", rows));
            }
        }

        return new BoardingDayDto(
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), session, sections,
            sections.Sum(s => s.Eligible), sections.Sum(s => s.Marked), sections.Sum(s => s.Absent));
    }

    private static BoardingSectionDto Section(string key, string title, string kind, List<BoardingStudentDto> rows) =>
        new(key, title, kind, rows,
            rows.Count(r => r.Eligible),
            rows.Count(r => r.Eligible && r.Status != null),
            rows.Count(r => r.Eligible && r.Status == BoardingStatus.Absent));

    /// <summary>
    /// Belgilarni saqlaydi (bor bo'lsa — yangilaydi). Xato bo'lsa — foydalanuvchiga ko'rsatiladigan matn.
    /// </summary>
    public async Task<(SaveBoardingResult? Result, string? Error)> SaveAsync(
        DateOnly date, string session, IReadOnlyList<BoardingMarkInput> marks, string userId,
        CancellationToken ct = default)
    {
        if (marks.Count == 0) return (null, "Belgilanadigan o'quvchi yo'q");
        if (marks.Any(m => !BoardingStatus.All.Contains(m.Status)))
            return (null, "Holat noto'g'ri (present, absent yoki excused)");

        // Sabab (2026-09-26): "keldi" — faqat kechikish sababi (IsLate); "kelmadi"/"sababli" —
        // faqat yo'qlik sababi. Katalogda yo'q id — xato.
        var reasonIds = marks.Where(m => !string.IsNullOrEmpty(m.ReasonId)).Select(m => m.ReasonId!).Distinct().ToList();
        var reasons = await db.AbsenceReasons.AsNoTracking()
            .Where(r => reasonIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.IsLate, ct);
        foreach (var m in marks.Where(m => !string.IsNullOrEmpty(m.ReasonId)))
        {
            if (!reasons.TryGetValue(m.ReasonId!, out var isLate))
                return (null, "Sabab topilmadi — sahifani yangilab qayta urinib ko'ring");
            if (isLate != (m.Status == BoardingStatus.Present))
                return (null, "Sabab holatga mos emas: \"keldi\" — faqat kechikish, \"kelmadi\" — faqat yo'qlik sababi");
        }

        var eligible = await EligibleAsync(date, ct);
        var foreign = marks.Where(m => !eligible.Contains(m.StudentId)).ToList();
        if (foreign.Count > 0)
            return (null, $"{foreign.Count} ta o'quvchida shu kuni yotoqxona abonementi yo'q — ularni belgilab bo'lmaydi");

        var ids = marks.Select(m => m.StudentId).ToList();
        var existing = await db.BoardingAttendance
            .Where(a => a.Date == date && a.Session == session && ids.Contains(a.StudentId))
            .ToDictionaryAsync(a => a.StudentId, ct);

        var now = AppClock.NowInstant;
        var toNotify = new List<BoardingAttendance>();
        foreach (var m in marks.DistinctBy(m => m.StudentId))
        {
            if (!existing.TryGetValue(m.StudentId, out var row))
            {
                row = new BoardingAttendance { Date = date, Session = session, StudentId = m.StudentId };
                db.BoardingAttendance.Add(row);
            }
            // Holat "yo'q" dan boshqasiga o'zgarsa — keyingi "yo'q" uchun xabar yana ketishi mumkin.
            if (m.Status != BoardingStatus.Absent) row.NotifiedAt = null;
            row.Status = m.Status;
            row.ReasonId = string.IsNullOrEmpty(m.ReasonId) ? null : m.ReasonId;
            row.MarkedBy = userId;
            row.MarkedAt = now;
            if (m.Status == BoardingStatus.Absent && row.NotifiedAt is null) toNotify.Add(row);
        }
        await db.SaveChangesAsync(ct);

        var notified = await NotifyAsync(toNotify, date, session, ct);
        return (new SaveBoardingResult(marks.Count, notified), null);
    }

    /// <summary>Ota-onaga Telegram: bot sozlanmagan yoki ota-ona ulanmagan bo'lsa — jim o'tadi.</summary>
    private async Task<int> NotifyAsync(
        List<BoardingAttendance> rows, DateOnly date, string session, CancellationToken ct)
    {
        if (rows.Count == 0 || !telegram.IsConfigured) return 0;
        var ids = rows.Select(r => r.StudentId).ToList();
        var students = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var chats = (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => ids.Contains(r.StudentId))
                .Select(r => new { r.StudentId, r.ChatId }).ToListAsync(ct))
            .GroupBy(x => x.StudentId).ToDictionary(g => g.Key, g => g.Select(x => x.ChatId).Distinct().ToList());

        var sentStudents = 0;
        foreach (var row in rows)
        {
            if (!chats.TryGetValue(row.StudentId, out var chatIds) || chatIds.Count == 0) continue;
            var text = BuildMessage(students.GetValueOrDefault(row.StudentId, ""), date, session);
            var any = false;
            foreach (var chat in chatIds)
                any |= await telegram.SendMessageAsync(chat, text, ct: ct);
            if (!any) continue;
            row.NotifiedAt = AppClock.NowInstant;
            sentStudents++;
        }
        if (sentStudents > 0) await db.SaveChangesAsync(ct);
        logger.LogInformation("Kechki/yotoqxona xabari: session={Session}, date={Date}, sent={Sent}",
            session, date, sentStudents);
        return sentStudents;
    }

    /// <summary>Ota-ona o'qiydigan qisqa matn.</summary>
    public static string BuildMessage(string fullName, DateOnly date, string session)
    {
        var day = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        return session == BoardingSession.Dorm
            ? $"Yotoqxona davomati\n\n{fullName} {day} kuni kechqurun yotoqxonada yo'q."
            : $"Kechki dars davomati\n\n{fullName} {day} kuni kechki darsga kelmadi.";
    }
}
