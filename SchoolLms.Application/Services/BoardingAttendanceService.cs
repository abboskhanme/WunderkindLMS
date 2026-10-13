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
//  KIMDAN OLINADI — shu kuni sessiya RO'YXATIDA turganlardan (`boarding_rosters`, sanali).
//  Mijoz, 2026-10-11: "o'quvchilarni qo'lda qo'shadigan qilish, abonomentga qaralmasin" — ro'yxat
//  qo'lda yuritiladi (qo'shish / chiqarish), kechki dars va yotoqxona ro'yxati ALOHIDA. Ilgari
//  yotoqxona abonementidan olinardi; migratsiya o'shalarni ikkala ro'yxatga ko'chirgan.
//  Ro'yxatda yo'q o'quvchini belgilab bo'lmaydi.
//
//  BO'LIMLAR — yo'nalish guruhlari bo'yicha (`study_groups.is_track`). Hech qaysi yo'nalish
//  guruhida bo'lmagan ro'yxat a'zosi "Guruhsiz" — o'z sinfi bo'limida.
//
//  OTA-ONAGA XABAR — "kelmadi" (kechki) yoki "yo'q" (yotoqxona) belgilanganda, Telegram orqali,
//  bir holat uchun BIR marta (`notified_at`). Sababli/ruxsat bilan — xabarsiz.
// ===========================================================================

public sealed class BoardingAttendanceService(
    IAppDbContext db, TelegramService telegram, ILogger<BoardingAttendanceService> logger)
{
    /// <summary>Shu kuni sessiya ro'yxatida turgan (arxivlanmagan) o'quvchilar.</summary>
    public async Task<HashSet<string>> EligibleAsync(DateOnly date, string session, CancellationToken ct = default)
    {
        var ids = await (
            from r in db.BoardingRosters.AsNoTracking()
            join st in db.Students.AsNoTracking() on r.StudentId equals st.Id
            where r.Session == session && r.AddedOn <= date && (r.RemovedOn == null || r.RemovedOn > date)
                  && !st.IsArchived
            select r.StudentId).Distinct().ToListAsync(ct);
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<BoardingDayDto> DayAsync(DateOnly date, string session, CancellationToken ct = default)
    {
        var eligible = await EligibleAsync(date, session, ct);
        var marks = await db.BoardingAttendance.AsNoTracking()
            .Where(a => a.Date == date && a.Session == session)
            .ToDictionaryAsync(a => a.StudentId, a => (a.Status, a.ReasonId), ct);

        BoardingStudentDto Row(string id, string name, string? cls) =>
            new(id, name, cls ?? "", eligible.Contains(id),
                marks.TryGetValue(id, out var m) ? m.Status : null,
                marks.TryGetValue(id, out var r) ? r.ReasonId : null);

        if (session == BoardingSession.Dorm) return await DormDayAsync(date, eligible, Row, ct);

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

        var sections = new List<BoardingSectionDto>();
        var inTrack = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in tracks)
        {
            // Faqat ro'yxat a'zolari (2026-10-11) — guruhning qolgan o'quvchilari bu davomatda yo'q.
            var rows = members.Where(m => m.GroupId == t.Id && eligible.Contains(m.Id))
                .OrderBy(m => m.FullName, StringComparer.Ordinal)
                .Select(m => Row(m.Id, m.FullName, m.ClassName)).ToList();
            rows.ForEach(r => inTrack.Add(r.StudentId));
            if (rows.Count > 0) sections.Add(Section(t.Id.ToString(), t.Name, "group", rows));
        }

        // Yo'nalish guruhida bo'lmagan ro'yxat a'zolari — sinf bo'yicha.
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

    /// <summary>
    /// The dormitory is checked by building, not by track (client, 2026-10-13: "yotoqxonasini qiz bola
    /// o'g'il bolaga alohida guruhlash kerak"): girls, then boys; a pupil with no gender on file gets a
    /// section of their own so they are never dropped.
    /// </summary>
    private async Task<BoardingDayDto> DormDayAsync(
        DateOnly date, HashSet<string> eligible, Func<string, string, string?, BoardingStudentDto> row, CancellationToken ct)
    {
        var ids = eligible.ToList();
        var students = await db.Students.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName, s.Gender })
            .ToListAsync(ct);

        (string Key, string Title)[] buildings = [("female", "Qizlar"), ("male", "O'g'il bolalar"), ("", "Jinsi ko'rsatilmagan")];
        static string GenderOf(string? g) => g is "female" or "male" ? g : "";
        var sections = buildings
            .Select(b => (b, rows: students.Where(s => GenderOf(s.Gender) == b.Key)
                .OrderBy(s => s.FullName, StringComparer.Ordinal)
                .Select(s => row(s.Id, s.FullName, s.ClassName)).ToList()))
            .Where(x => x.rows.Count > 0)
            .Select(x => Section("gender:" + x.b.Key, x.b.Title, "gender", x.rows))
            .ToList();

        return new BoardingDayDto(
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), BoardingSession.Dorm, sections,
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

        var eligible = await EligibleAsync(date, session, ct);
        var foreign = marks.Where(m => !eligible.Contains(m.StudentId)).ToList();
        if (foreign.Count > 0)
            return (null, $"{foreign.Count} ta o'quvchi shu kuni ro'yxatda yo'q — avval ro'yxatga qo'shing");

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

    // =================================================================
    //  Ro'yxat — qo'lda (2026-10-11)
    // =================================================================

    /// <summary>Sessiyaning hozirgi ro'yxati — yo'nalish guruhi va kim qo'shgani bilan.</summary>
    public async Task<List<BoardingRosterRowDto>> RosterAsync(string session, CancellationToken ct = default)
    {
        var today = AppClock.Today;
        var rows = await (
            from r in db.BoardingRosters.AsNoTracking()
            join st in db.Students.AsNoTracking() on r.StudentId equals st.Id
            where r.Session == session && r.AddedOn <= today && (r.RemovedOn == null || r.RemovedOn > today)
                  && !st.IsArchived
            select new { st.Id, st.FullName, st.ClassName, r.AddedOn, r.AddedBy }).ToListAsync(ct);
        var tracks = await TrackOfAsync([.. rows.Select(r => r.Id)], ct);
        var userIds = rows.Select(r => r.AddedBy).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return [.. rows
            .OrderBy(r => tracks.GetValueOrDefault(r.Id) ?? "\uffff", StringComparer.Ordinal)
            .ThenBy(r => r.ClassName, StringComparer.Ordinal).ThenBy(r => r.FullName, StringComparer.Ordinal)
            .Select(r => new BoardingRosterRowDto(
                r.Id, r.FullName, r.ClassName ?? "", tracks.GetValueOrDefault(r.Id),
                r.AddedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), names.GetValueOrDefault(r.AddedBy)))];
    }

    /// <summary>Ro'yxatda bo'lmagan faol o'quvchilar (qidiruv bo'yicha, ko'pi bilan 100 ta).</summary>
    public async Task<List<BoardingCandidateDto>> CandidatesAsync(string session, string? query, CancellationToken ct = default)
    {
        var today = AppClock.Today;
        var inRoster = db.BoardingRosters.Where(r => r.Session == session && r.AddedOn <= today
                                                     && (r.RemovedOn == null || r.RemovedOn > today))
            .Select(r => r.StudentId);
        var q = db.Students.AsNoTracking().Where(s => !s.IsArchived && !inRoster.Contains(s.Id));
        var needle = query?.Trim();
        if (!string.IsNullOrEmpty(needle))
        {
            var lower = needle.ToLower();
            q = q.Where(s => s.FullName.ToLower().Contains(lower) || s.ClassName.ToLower().Contains(lower));
        }
        var list = await q.OrderBy(s => s.ClassName).ThenBy(s => s.FullName).Take(100)
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToListAsync(ct);
        var tracks = await TrackOfAsync([.. list.Select(s => s.Id)], ct);
        return [.. list.Select(s => new BoardingCandidateDto(s.Id, s.FullName, s.ClassName ?? "", tracks.GetValueOrDefault(s.Id)))];
    }

    /// <summary>Ro'yxatga qo'shadi (bugundan). Allaqachon ro'yxatdagilar o'tkazib yuboriladi.</summary>
    /// <returns>Nechtasi qo'shildi; xato bo'lsa — matn.</returns>
    public async Task<(int Added, string? Error)> AddAsync(
        string session, IReadOnlyCollection<string> studentIds, string userId, CancellationToken ct = default)
    {
        var ids = studentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return (0, "Hech kim tanlanmadi");
        var found = await db.Students.Where(s => ids.Contains(s.Id) && !s.IsArchived).Select(s => s.Id).ToListAsync(ct);
        if (found.Count != ids.Count) return (0, "Tanlanganlardan biri topilmadi yoki arxivda");

        var today = AppClock.Today;
        var active = (await db.BoardingRosters
                .Where(r => r.Session == session && ids.Contains(r.StudentId) && r.RemovedOn == null)
                .ToListAsync(ct))
            .ToDictionary(r => r.StudentId, StringComparer.Ordinal);
        var added = 0;
        foreach (var id in ids)
        {
            if (active.TryGetValue(id, out var row))
            {
                // Kelajakdan boshlanadigan qator bo'lsa (bo'lmasligi kerak) — bugunga suriladi.
                if (row.AddedOn > today) row.AddedOn = today;
                continue;
            }
            db.BoardingRosters.Add(new BoardingRosterEntry
            {
                Session = session, StudentId = id, AddedOn = today, AddedBy = userId, CreatedAt = AppClock.NowInstant,
            });
            added++;
        }
        await db.SaveChangesAsync(ct);
        return (added, null);
    }

    /// <summary>
    /// Ro'yxatdan chiqaradi — bugundan boshlab. Bugun qo'shilgan bo'lsa qator butunlay o'chiriladi
    /// (xato qo'shilgan), o'tgan kunlardagi davomati esa joyida qoladi.
    /// </summary>
    public async Task<bool> RemoveAsync(string session, string studentId, string userId, CancellationToken ct = default)
    {
        var row = await db.BoardingRosters
            .FirstOrDefaultAsync(r => r.Session == session && r.StudentId == studentId && r.RemovedOn == null, ct);
        if (row is null) return false;
        var today = AppClock.Today;
        if (row.AddedOn >= today) db.BoardingRosters.Remove(row);
        else
        {
            row.RemovedOn = today;
            row.RemovedBy = userId;
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>O'quvchi → faol yo'nalish guruhi nomi.</summary>
    private async Task<Dictionary<string, string>> TrackOfAsync(List<string> studentIds, CancellationToken ct) =>
        (await (from m in db.StudyGroupMembers.AsNoTracking()
                join g in db.StudyGroups.AsNoTracking() on m.GroupId equals g.Id
                where g.IsTrack && !g.IsArchived && m.LeftOn == null && studentIds.Contains(m.StudentId)
                select new { m.StudentId, g.Name }).ToListAsync(ct))
            .GroupBy(x => x.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);

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
