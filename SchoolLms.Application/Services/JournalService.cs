using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Jurnal o'qish/yozish mantig'i (baho, davomat, dars mavzusi/uyga vazifa). Admin jurnali ham,
/// o'qituvchi ilovasi ham shu yagona mantiqdan foydalanadi (faqat ruxsat tekshiruvi farq qiladi).
/// </summary>
public static class JournalService
{
    /// <summary>Fanning chorakdagi darslari (sana + dars raqami). Bir kunda bir fan bir necha marta bo'lishi mumkin.</summary>
    public static async Task<List<JournalColumnDto>> ComputeColumnsAsync(
        IAppDbContext db, string classId, string subjectId, int quarter)
    {
        var q = await db.Quarters.FirstOrDefaultAsync(x => x.Quarter == quarter);
        if (q is null) return [];

        var weeks = ScheduleMath.GetQuarterWeeks(q.StartDate, q.EndDate);
        var assignments = await db.WeekAssignments
            .Where(a => a.ClassId == classId && a.Quarter == quarter).ToListAsync();
        var templates = await db.ScheduleTemplates.Include(t => t.Lessons)
            .Where(t => t.ClassId == classId).ToListAsync();
        // Bayram kunlari — bu sanalarda dars yo'q, jurnal ustuni chiqmaydi.
        var holidays = (await db.Holidays.Select(h => h.Date).ToListAsync()).ToHashSet();

        var cols = new List<JournalColumnDto>();
        foreach (var w in weeks)
        {
            var a = assignments.FirstOrDefault(x => x.Week == w.Week);
            if (a?.TemplateId is null) continue;
            var tpl = templates.FirstOrDefault(t => t.Id == a.TemplateId);
            if (tpl is null) continue;
            // Bir kunda bir fan bir necha marta bo'lsa — har biri (sana+dars+guruh) alohida ustun.
            foreach (var l in tpl.Lessons.Where(l => l.SubjectId == subjectId))
            {
                var d = ScheduleMath.AddDaysISO(ScheduleMath.MondayOfISO(w.StartISO), l.Day);
                if (string.CompareOrdinal(d, q.StartDate) >= 0 && string.CompareOrdinal(d, q.EndDate) <= 0
                    && !holidays.Contains(d))
                    cols.Add(new JournalColumnDto(d, l.Period, l.SubGroup));
            }
        }
        return cols
            .GroupBy(c => (c.Date, c.Period, c.SubGroup)).Select(g => g.First())
            .OrderBy(c => c.Date, StringComparer.Ordinal).ThenBy(c => c.Period).ThenBy(c => c.SubGroup)
            .ToList();
    }

    public static async Task<List<JournalEntryDto>> GetEntriesAsync(
        IAppDbContext db, string classId, string subjectId, int quarter) =>
        await db.JournalEntries
            .Where(e => e.ClassId == classId && e.SubjectId == subjectId && e.Quarter == quarter)
            .Select(e => new JournalEntryDto(e.StudentId, e.Date, e.Period, e.Grade, e.ReasonId))
            .ToListAsync();

    /// <summary>
    /// Bitta katakni belgilash — baho yoki davomat sababi (mavjud bo'lsa ustiga yoziladi).
    /// SubGroup o'quvchining Student.SubGroup'idan olinadi — guruh o'zgarsa journal yozuvi
    /// yangi guruh ostida ko'rinadi.
    /// </summary>
    public static async Task SetEntryAsync(IAppDbContext db, SetJournalEntryRequest req, FcmService? fcm = null)
    {
        var entry = await db.JournalEntries.FirstOrDefaultAsync(e =>
            e.ClassId == req.ClassId && e.SubjectId == req.SubjectId && e.Quarter == req.Quarter &&
            e.StudentId == req.StudentId && e.Date == req.Date && e.Period == req.Period);
        // Push uchun — yangi/o'zgargan baho yoki sababnigina xabar qilamiz.
        var oldGrade = entry?.Grade;
        var oldReason = entry?.ReasonId;

        // O'quvchining guruhi — yozuvga ham, mos LessonNote'ga ham SubGroup sifatida yoziladi.
        var student = await db.Students.FindAsync(req.StudentId);
        var subGroup = student?.SubGroup ?? 0;

        if (entry is null)
        {
            entry = new JournalEntry
            {
                ClassId = req.ClassId,
                SubjectId = req.SubjectId,
                Quarter = req.Quarter,
                StudentId = req.StudentId,
                Date = req.Date,
                Period = req.Period,
                SubGroup = subGroup,
            };
            db.JournalEntries.Add(entry);
        }
        entry.Grade = req.Grade;
        entry.ReasonId = req.ReasonId;
        entry.SubGroup = subGroup;

        // Baho yoki davomat kiritilsa — shu darsni (sana+dars+guruh) "o'tildi" deb avtomatik belgilaymiz.
        if (req.Grade.HasValue || req.ReasonId is not null)
        {
            var note = await db.LessonNotes.FirstOrDefaultAsync(n =>
                n.ClassId == req.ClassId && n.SubjectId == req.SubjectId &&
                n.Quarter == req.Quarter && n.Date == req.Date && n.Period == req.Period &&
                n.SubGroup == subGroup);
            if (note is null)
                db.LessonNotes.Add(new LessonNote
                {
                    ClassId = req.ClassId,
                    SubjectId = req.SubjectId,
                    Quarter = req.Quarter,
                    Date = req.Date,
                    Period = req.Period,
                    SubGroup = subGroup,
                    Conducted = true,
                });
            else if (!note.Conducted)
                note.Conducted = true;
        }

        await db.SaveChangesAsync();

        // Avtomatik push: farzandi baho olsa yoki davomatda belgilansa, oila ilovasiga xabar.
        await NotifyEntryAsync(db, fcm, req, student, oldGrade, oldReason);
    }

    /// <summary>Baho/davomat yozuvi o'zgarganda oila ilovasiga push yuboradi (fire-and-forget).</summary>
    private static async Task NotifyEntryAsync(
        IAppDbContext db, FcmService? fcm, SetJournalEntryRequest req, Student? student,
        int? oldGrade, string? oldReason)
    {
        if (fcm is null || student?.UserId is null) return;
        var notifyGrade = req.Grade.HasValue && req.Grade != oldGrade;
        var notifyAbsence = !notifyGrade && req.ReasonId is not null && req.ReasonId != oldReason;
        if (!notifyGrade && !notifyAbsence) return;

        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        var json = meta?.FcmServiceAccountJson ?? "";
        if (!FcmService.IsConfigured(json)) return;

        var tokens = await db.DeviceTokens.Where(d => d.UserId == student.UserId)
            .Select(d => d.Token).Distinct().ToListAsync();
        if (tokens.Count == 0) return;

        var subject = (await db.Subjects.FindAsync(req.SubjectId))?.Name ?? "";
        string title, body;
        if (notifyGrade)
        {
            title = "Yangi baho";
            body = $"{student.FullName}: {subject} fanidan {req.Grade} baho ({req.Date})";
        }
        else
        {
            var reason = (await db.AbsenceReasons.FindAsync(req.ReasonId!))?.Name ?? "Davomat";
            title = "Davomat";
            body = $"{student.FullName}: {subject} darsida — {reason} ({req.Date})";
        }
        // FCM faqat HTTP (db'ga tegmaydi) — jurnal javobini bloklamaslik uchun kutmaymiz.
        _ = fcm.SendAsync(json, tokens, title, body);
    }

    public static async Task ClearEntryAsync(
        IAppDbContext db, string classId, string subjectId, int quarter,
        string studentId, string date, int period)
    {
        var entries = db.JournalEntries.Where(e =>
            e.ClassId == classId && e.SubjectId == subjectId && e.Quarter == quarter &&
            e.StudentId == studentId && e.Date == date && e.Period == period);
        db.JournalEntries.RemoveRange(entries);
        await db.SaveChangesAsync();
    }

    /* ---------- Chorak (yakuniy) bahosi ---------- */

    /// <summary>Fan+chorak bo'yicha har o'quvchining chorak bahosi (explicit) va tavsiyasi (kunlik o'rtacha).
    /// Faqat baho yoki kunlik baholari bor o'quvchilar qaytadi.</summary>
    public static async Task<List<QuarterGradeRowDto>> GetQuarterGradesAsync(
        IAppDbContext db, string classId, string subjectId, int quarter)
    {
        var explicitGrades = await db.QuarterGrades
            .Where(g => g.ClassId == classId && g.SubjectId == subjectId && g.Quarter == quarter)
            .ToListAsync();
        var recommended = (await db.JournalEntries
                .Where(e => e.ClassId == classId && e.SubjectId == subjectId
                            && e.Quarter == quarter && e.Grade != null).ToListAsync())
            .GroupBy(e => e.StudentId)
            .ToDictionary(g => g.Key, g => Math.Round(g.Average(e => (double)e.Grade!.Value), 2));

        return explicitGrades.Select(g => g.StudentId).Union(recommended.Keys).Distinct()
            .Select(sid => new QuarterGradeRowDto(
                sid,
                explicitGrades.FirstOrDefault(g => g.StudentId == sid)?.Grade,
                recommended.TryGetValue(sid, out var r) ? r : null))
            .ToList();
    }

    /// <summary>Chorak bahosini belgilash (upsert). Grade null bo'lsa — mavjud baho o'chiriladi.</summary>
    public static async Task SetQuarterGradeAsync(IAppDbContext db, SetQuarterGradeRequest req)
    {
        var existing = await db.QuarterGrades.FirstOrDefaultAsync(g =>
            g.ClassId == req.ClassId && g.SubjectId == req.SubjectId &&
            g.Quarter == req.Quarter && g.StudentId == req.StudentId);

        if (req.Grade is null)
        {
            if (existing is not null) db.QuarterGrades.Remove(existing);
        }
        else if (existing is null)
        {
            db.QuarterGrades.Add(new QuarterGrade
            {
                ClassId = req.ClassId,
                SubjectId = req.SubjectId,
                Quarter = req.Quarter,
                StudentId = req.StudentId,
                Grade = req.Grade.Value,
            });
        }
        else
        {
            existing.Grade = req.Grade.Value;
        }
        await db.SaveChangesAsync();
    }

    public static async Task<List<JournalTopicDto>> GetNotesAsync(
        IAppDbContext db, string classId, string subjectId, int quarter) =>
        await db.LessonNotes
            .Where(n => n.ClassId == classId && n.SubjectId == subjectId && n.Quarter == quarter)
            .Select(n => new JournalTopicDto(n.Date, n.Period, n.Topic, n.Homework, n.Conducted, n.SubGroup))
            .ToListAsync();

    public static async Task SetNoteAsync(IAppDbContext db, SetLessonNoteRequest req)
    {
        var note = await db.LessonNotes.FirstOrDefaultAsync(n =>
            n.ClassId == req.ClassId && n.SubjectId == req.SubjectId &&
            n.Quarter == req.Quarter && n.Date == req.Date && n.Period == req.Period &&
            n.SubGroup == req.SubGroup);

        // Mavzu, uyga vazifa va "dars o'tildi" — uchchovi ham bo'sh bo'lsa yozuvni o'chiramiz.
        var empty = string.IsNullOrWhiteSpace(req.Topic) && string.IsNullOrWhiteSpace(req.Homework) && !req.Conducted;
        if (empty)
        {
            if (note is not null) db.LessonNotes.Remove(note);
        }
        else if (note is null)
        {
            db.LessonNotes.Add(new LessonNote
            {
                ClassId = req.ClassId,
                SubjectId = req.SubjectId,
                Quarter = req.Quarter,
                Date = req.Date,
                Period = req.Period,
                SubGroup = req.SubGroup,
                Topic = req.Topic,
                Homework = req.Homework,
                Conducted = req.Conducted,
            });
        }
        else
        {
            note.Topic = req.Topic;
            note.Homework = req.Homework;
            note.Conducted = req.Conducted;
        }
        await db.SaveChangesAsync();
    }
}
