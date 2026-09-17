using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Jurnal o'qish/yozish mantig'i (baho, davomat, dars mavzusi/uyga vazifa). Admin jurnali ham,
/// o'qituvchi ilovasi ham shu yagona mantiqdan foydalanadi (faqat ruxsat tekshiruvi farq qiladi).
///
/// <para>
/// <b>Jurnal katagi SINFGA ham, O'QUV GURUHIGA ham tegishli bo'lishi mumkin</b>
/// (G-12, students-parity.md §2.1.4). Ega <c>class_id</c> ustunida turadi,
/// turi esa <c>owner_kind</c> da: <c>class</c> yoki <c>group</c>. Shu sababli
/// bu fayldagi metodlar avvalgidek bitta id bilan chaqiriladi va o'zi egani
/// aniqlaydi — <c>class_id</c> ni o'qiydigan ~60 joy o'zgarishsiz qoladi.
/// </para>
/// <para>
/// <b>O'chirgich o'chiq ekan (<c>group_lessons_enabled</c>) hech narsa
/// o'zgarmaydi:</b> guruh haftaga biriktirilmaydi, ustun chiqmaydi,
/// ro'yxat so'ralmaydi — va mavjud har bir qator <c>owner_kind='class'</c>,
/// ya'ni yangi filtrlar bugungi natijani bir bayt ham siljitmaydi.
/// </para>
/// </summary>
public static class JournalService
{
    /// <summary>
    /// Guruh darsi jurnalda BUTUN guruh uchun bitta ustun: sinf ichidagi
    /// 1/2-guruhga bo'linish guruhga tegishli emas (§2.1.4).
    /// </summary>
    public const string SubGroupOnGroupMessage =
        "Guruh darsida sinf ichidagi 1/2-guruhga bo'linish bo'lmaydi — "
        + "guruhning o'zi allaqachon tanlangan o'quvchilar ro'yxati.";

    /// <summary>Guruh darslari hali yoqilmagan (cut-over o'chirgichi, §4.3).</summary>
    public const string GroupLessonsOffMessage =
        "Guruh darslari hali yoqilmagan — guruh jurnaliga yozib bo'lmaydi.";

    /// <summary>Fanning chorakdagi darslari (sana + dars raqami). Bir kunda bir fan bir necha marta bo'lishi mumkin.</summary>
    public static async Task<List<JournalColumnDto>> ComputeColumnsAsync(
        IAppDbContext db, string classId, string subjectId, int quarter)
    {
        var q = await db.Quarters.FirstOrDefaultAsync(x => x.Quarter == quarter);
        if (q is null) return [];

        var owner = await LessonRoster.OwnerAsync(db, classId);
        // O'chirgich o'chiq — guruhning darsi UMUMAN yo'q (§4.3).
        if (owner is not null && owner.IsGroup && !await LessonRoster.GroupLessonsEnabledAsync(db)) return [];

        var weeks = ScheduleMath.GetQuarterWeeks(q.StartDate, q.EndDate);
        var assignmentQuery = db.WeekAssignments.Where(a => a.ClassId == classId && a.Quarter == quarter);
        var templateQuery = db.ScheduleTemplates.Include(t => t.Lessons).Where(t => t.ClassId == classId);
        if (owner is not null)
        {
            assignmentQuery = assignmentQuery.Where(a => a.OwnerKind == owner.Kind);
            templateQuery = templateQuery.Where(t => t.OwnerKind == owner.Kind);
        }
        var assignments = await assignmentQuery.ToListAsync();
        var templates = await templateQuery.ToListAsync();
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
        IAppDbContext db, string classId, string subjectId, int quarter)
    {
        var kind = await OwnerKindAsync(db, classId);
        return await db.JournalEntries
            .Where(e => e.ClassId == classId && e.SubjectId == subjectId && e.Quarter == quarter)
            .Where(e => kind == null || e.OwnerKind == kind)
            .Select(e => new JournalEntryDto(e.StudentId, e.Date, e.Period, e.Grade, e.ReasonId, e.Homework, e.Behavior, e.Mastery))
            .ToListAsync();
    }

    /// <summary>
    /// Id'ning egasi qaysi turdan — <c>class</c>, <c>group</c>, yoki ega
    /// topilmasa <c>null</c>. <c>null</c> bo'lganda chaqiruvchi tur bo'yicha
    /// FILTRLAMAYDI: bugungi kod ham "yetim" id uchun filtrsiz ishlaydi.
    /// </summary>
    private static async Task<string?> OwnerKindAsync(IAppDbContext db, string ownerId) =>
        (await LessonRoster.OwnerAsync(db, ownerId))?.Kind;

    /// <summary>
    /// Bitta katakni belgilash — baho yoki davomat sababi (mavjud bo'lsa ustiga yoziladi).
    /// SubGroup o'quvchining Student.SubGroup'idan olinadi — guruh o'zgarsa journal yozuvi
    /// yangi guruh ostida ko'rinadi.
    /// </summary>
    /// <returns>
    /// <c>null</c> — yozildi. Aks holda foydalanuvchiga ko'rsatiladigan xato matni
    /// (§5.5 <c>make_attendance_reason_required</c>) — chaqiruvchi uni 400 bilan qaytaradi.
    /// </returns>
    public static async Task<string?> SetEntryAsync(IAppDbContext db, SetJournalEntryRequest req, FcmService? fcm = null)
    {
        var flags = await JournalSettingsGuard.FlagsAsync(db);

        // §5.5 — sababsiz yo'qlik yozilmaydi: id bo'sh yoki katalogda yo'q bo'lsa rad.
        if (flags.AttendanceReasonRequired
            && !await JournalSettingsGuard.ReasonIsUsableAsync(db, req.ReasonId))
            return JournalSettingsGuard.ReasonRequiredMessage;

        var owner = await LessonRoster.OwnerAsync(db, req.ClassId);
        // O'chirgich o'chiq ekan guruh jurnaliga yozib bo'lmaydi (§4.3).
        if (owner is not null && owner.IsGroup && !await LessonRoster.GroupLessonsEnabledAsync(db))
            return GroupLessonsOffMessage;
        var ownerKind = owner?.Kind ?? LessonOwnerKind.Class;

        var entry = await db.JournalEntries.FirstOrDefaultAsync(e =>
            e.ClassId == req.ClassId && e.SubjectId == req.SubjectId && e.Quarter == req.Quarter &&
            e.StudentId == req.StudentId && e.Date == req.Date && e.Period == req.Period &&
            e.OwnerKind == ownerKind);
        // Push uchun — yangi/o'zgargan baho yoki sababnigina xabar qilamiz.
        var oldGrade = entry?.Grade;
        var oldReason = entry?.ReasonId;

        // O'quvchining guruhi — yozuvga ham, mos LessonNote'ga ham SubGroup sifatida yoziladi.
        // GURUH darsida bo'linish yo'q: qator har doim 0 bilan yoziladi (§2.1.3).
        var student = await db.Students.FindAsync(req.StudentId);
        var subGroup = owner?.IsGroup == true ? 0 : student?.SubGroup ?? 0;

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
                OwnerKind = ownerKind,
            };
            db.JournalEntries.Add(entry);
        }
        entry.Grade = req.Grade;
        entry.ReasonId = req.ReasonId;
        entry.Homework = req.Homework;
        entry.Behavior = req.Behavior;
        entry.Mastery = req.Mastery;
        entry.SubGroup = subGroup;

        var touched = req.Grade.HasValue || req.ReasonId is not null
            || req.Homework != 0 || req.Behavior != 0 || req.Mastery.HasValue;

        // Katakning O'ZI avval yoziladi — quyidagi "baholar to'liqmi" tekshiruvi ayni shu
        // bahoni ham hisobga olishi kerak, EF so'rovi esa saqlanmagan o'zgarishni ko'rmaydi.
        await db.SaveChangesAsync();

        // Baho/davomat/uyga vazifa/xulq/o'zlashtirish kiritilsa — shu darsni "o'tildi" deb
        // avtomatik belgilaymiz. §5.5 `is_student_grade_required` yoqiq bo'lsa, bu AVTOMATIK
        // belgi baholar to'lgunicha kutadi (rad etilmaydi — aks holda birinchi bahoni ham
        // kiritib bo'lmasdi; batafsil izoh JournalSettingsGuard'da).
        if (touched
            && (!flags.GradeRequired || await JournalSettingsGuard.SlotFullyGradedAsync(
                db, req.ClassId, req.SubjectId, req.Quarter, req.Date, req.Period, subGroup)))
        {
            var note = await db.LessonNotes.FirstOrDefaultAsync(n =>
                n.ClassId == req.ClassId && n.SubjectId == req.SubjectId &&
                n.Quarter == req.Quarter && n.Date == req.Date && n.Period == req.Period &&
                n.SubGroup == subGroup && n.OwnerKind == ownerKind);
            if (note is null)
            {
                db.LessonNotes.Add(new LessonNote
                {
                    ClassId = req.ClassId,
                    SubjectId = req.SubjectId,
                    Quarter = req.Quarter,
                    Date = req.Date,
                    Period = req.Period,
                    SubGroup = subGroup,
                    OwnerKind = ownerKind,
                    Conducted = true,
                });
                await db.SaveChangesAsync();
            }
            else if (!note.Conducted)
            {
                note.Conducted = true;
                await db.SaveChangesAsync();
            }
        }

        // Avtomatik push: farzandi baho olsa yoki davomatda belgilansa, oila ilovasiga xabar.
        await NotifyEntryAsync(db, fcm, req, student, oldGrade, oldReason);
        return null;
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
        var kind = await OwnerKindAsync(db, classId);
        var entries = db.JournalEntries.Where(e =>
            e.ClassId == classId && e.SubjectId == subjectId && e.Quarter == quarter &&
            e.StudentId == studentId && e.Date == date && e.Period == period &&
            (kind == null || e.OwnerKind == kind));
        db.JournalEntries.RemoveRange(entries);
        await db.SaveChangesAsync();
    }

    /* ---------- Chorak (yakuniy) bahosi ---------- */

    /// <summary>Fan+chorak bo'yicha har o'quvchining chorak bahosi (explicit) va tavsiyasi (kunlik o'rtacha).
    /// Faqat baho yoki kunlik baholari bor o'quvchilar qaytadi.</summary>
    public static async Task<List<QuarterGradeRowDto>> GetQuarterGradesAsync(
        IAppDbContext db, string classId, string subjectId, int quarter)
    {
        var kind = await OwnerKindAsync(db, classId);
        var explicitGrades = await db.QuarterGrades
            .Where(g => g.ClassId == classId && g.SubjectId == subjectId && g.Quarter == quarter)
            .Where(g => kind == null || g.OwnerKind == kind)
            .ToListAsync();
        var recommended = (await db.JournalEntries
                .Where(e => e.ClassId == classId && e.SubjectId == subjectId
                            && e.Quarter == quarter && e.Grade != null)
                .Where(e => kind == null || e.OwnerKind == kind).ToListAsync())
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
        var ownerKind = await OwnerKindAsync(db, req.ClassId) ?? LessonOwnerKind.Class;
        var existing = await db.QuarterGrades.FirstOrDefaultAsync(g =>
            g.ClassId == req.ClassId && g.SubjectId == req.SubjectId &&
            g.Quarter == req.Quarter && g.StudentId == req.StudentId &&
            g.OwnerKind == ownerKind);

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
                OwnerKind = ownerKind,
            });
        }
        else
        {
            existing.Grade = req.Grade.Value;
        }
        await db.SaveChangesAsync();
    }

    public static async Task<List<JournalTopicDto>> GetNotesAsync(
        IAppDbContext db, string classId, string subjectId, int quarter)
    {
        var kind = await OwnerKindAsync(db, classId);
        return await db.LessonNotes
            .Where(n => n.ClassId == classId && n.SubjectId == subjectId && n.Quarter == quarter)
            .Where(n => kind == null || n.OwnerKind == kind)
            .Select(n => new JournalTopicDto(n.Date, n.Period, n.Topic, n.Homework, n.Conducted, n.SubGroup))
            .ToListAsync();
    }

    /// <returns>
    /// <c>null</c> — yozildi. Aks holda foydalanuvchiga ko'rsatiladigan xato matni
    /// (§5.5 <c>is_student_grade_required</c>) — chaqiruvchi uni 400 bilan qaytaradi.
    /// </returns>
    public static async Task<string?> SetNoteAsync(IAppDbContext db, SetLessonNoteRequest req)
    {
        var owner = await LessonRoster.OwnerAsync(db, req.ClassId);
        if (owner is not null && owner.IsGroup)
        {
            // O'chirgich o'chiq ekan guruh jurnaliga yozib bo'lmaydi (§4.3).
            if (!await LessonRoster.GroupLessonsEnabledAsync(db)) return GroupLessonsOffMessage;
            // Guruhda sinf ichidagi bo'linish yo'q — server rad etadi (§2.1.4).
            if (req.SubGroup != 0) return SubGroupOnGroupMessage;
        }
        var ownerKind = owner?.Kind ?? LessonOwnerKind.Class;

        // §5.5 — darsni ATAYLAB yopish: baholar to'liq bo'lmasa rad etiladi.
        if (req.Conducted)
        {
            var flags = await JournalSettingsGuard.FlagsAsync(db);
            if (flags.GradeRequired && !await JournalSettingsGuard.SlotFullyGradedAsync(
                    db, req.ClassId, req.SubjectId, req.Quarter, req.Date, req.Period, req.SubGroup))
                return JournalSettingsGuard.GradesRequiredMessage;
        }

        var note = await db.LessonNotes.FirstOrDefaultAsync(n =>
            n.ClassId == req.ClassId && n.SubjectId == req.SubjectId &&
            n.Quarter == req.Quarter && n.Date == req.Date && n.Period == req.Period &&
            n.SubGroup == req.SubGroup && n.OwnerKind == ownerKind);

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
                OwnerKind = ownerKind,
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
        return null;
    }

    // ---------- Mavzular Excel shablon / import (mavzu + uy vazifa; darsni "o'tilgan" QILMAYDI) ----------

    public static readonly string[] TopicHeaders =
        { "Dars raqami", "Mavzu", "Uy vazifa" };

    /// <summary>Tanlangan sinf+fan+chorak uchun mavzular shabloni (.xlsx): "Dars raqami" jadval tartibida
    /// (1, 2, 3, ...) oldindan to'ldirilgan — sana va guruh shu raqamdan avtomatik aniqlanadi;
    /// "Mavzu"/"Uy vazifa" ustunlari foydalanuvchi to'ldirishi uchun (mavjudi ham ko'rsatiladi).</summary>
    public static async Task<byte[]> TopicTemplateXlsxAsync(IAppDbContext db, string classId, string subjectId, int quarter)
    {
        var kind = await OwnerKindAsync(db, classId);
        var cols = await ComputeColumnsAsync(db, classId, subjectId, quarter);
        var notes = (await db.LessonNotes
                .Where(n => n.ClassId == classId && n.SubjectId == subjectId && n.Quarter == quarter)
                .Where(n => kind == null || n.OwnerKind == kind).ToListAsync())
            .GroupBy(n => (n.Date, n.Period, n.SubGroup)).ToDictionary(g => g.Key, g => g.First());

        // Har bir dars slotiga jadval tartibidagi raqam beriladi (1-asosli); sana/guruh shu raqamdan kelib chiqadi.
        var rows = cols.Select((c, i) =>
        {
            notes.TryGetValue((c.Date, c.Period, c.SubGroup), out var n);
            return (IReadOnlyList<string>)new[]
            {
                (i + 1).ToString(), n?.Topic ?? "", n?.Homework ?? "",
            };
        }).ToList();

        var info = new List<IReadOnlyList<string>>
        {
            new[] { "Dars raqami", "O'zgartirmang — jadval tartibidagi dars raqami (sana va guruh avtomatik aniqlanadi)" },
            new[] { "Mavzu", "Dars mavzusini yozing" },
            new[] { "Uy vazifa", "Uyga vazifani yozing (ixtiyoriy)" },
            new[] { "", "" },
            new[] { "Eslatma:", "Import faqat mavzu/uy vazifani to'ldiradi — darsni \"o'tilgan\" QILMAYDI (buni jurnalda o'zingiz belgilaysiz)." },
        };

        return ExcelExport.Build(new[]
        {
            new ExcelExport.SheetSpec("Mavzular", TopicHeaders, rows),
            new ExcelExport.SheetSpec("Yo'riqnoma", new[] { "Ustun", "Izoh" }, info),
        });
    }

    /// <summary>Excel qatorlaridan mavzu+uy vazifani jurnalga import qiladi. MUHIM: darsni "o'tilgan"
    /// QILMAYDI — yangi yozuvda Conducted=false, mavjud yozuvda Conducted o'zgarmaydi. Faqat "Dars raqami"
    /// (jadval tartibidagi 1-asosli raqam) o'qiladi; mos sana/dars/guruh shu raqamdan avtomatik aniqlanadi.</summary>
    public static async Task<TopicImportResultDto> ImportTopicsAsync(
        IAppDbContext db, string classId, string subjectId, int quarter, List<string[]> rows)
    {
        // Tartiblangan dars ketma-ketligi — "Dars raqami" shu ro'yxatga 1-asosli indeks.
        var ownerKind = await OwnerKindAsync(db, classId) ?? LessonOwnerKind.Class;
        var cols = await ComputeColumnsAsync(db, classId, subjectId, quarter);
        var notes = (await db.LessonNotes
                .Where(n => n.ClassId == classId && n.SubjectId == subjectId && n.Quarter == quarter
                            && n.OwnerKind == ownerKind).ToListAsync())
            .GroupBy(n => (n.Date, n.Period, n.SubGroup)).ToDictionary(g => g.Key, g => g.First());

        var errors = new List<TopicImportRowErrorDto>();
        int imported = 0, skipped = 0;
        for (var i = 1; i < rows.Count; i++) // 0-qator = sarlavha
        {
            var r = rows[i];
            var excelRow = i + 1;
            if (r.All(string.IsNullOrWhiteSpace)) { skipped++; continue; }

            var lessonOk = int.TryParse((r.ElementAtOrDefault(0) ?? "").Trim(), out var lessonNo);
            var topic = (r.ElementAtOrDefault(1) ?? "").Trim();
            var homework = (r.ElementAtOrDefault(2) ?? "").Trim();

            if (!lessonOk || lessonNo <= 0)
            { errors.Add(new TopicImportRowErrorDto(excelRow, "Dars raqami noto'g'ri")); continue; }
            if (topic.Length == 0 && homework.Length == 0) { skipped++; continue; }
            if (lessonNo > cols.Count)
            { errors.Add(new TopicImportRowErrorDto(excelRow, $"Dars raqami {lessonNo} jadvalda yo'q (jami {cols.Count} ta dars)")); continue; }

            var slot = cols[lessonNo - 1];
            var key = (slot.Date, slot.Period, slot.SubGroup);

            if (notes.TryGetValue(key, out var n))
            {
                n.Topic = topic;
                n.Homework = homework;
                // Conducted O'ZGARMAYDI — import darsni o'tilgan qilmaydi.
            }
            else
            {
                var fresh = new LessonNote
                {
                    ClassId = classId, SubjectId = subjectId, Quarter = quarter,
                    Date = slot.Date, Period = slot.Period, SubGroup = slot.SubGroup,
                    OwnerKind = ownerKind,
                    Topic = topic, Homework = homework, Conducted = false,
                };
                db.LessonNotes.Add(fresh);
                notes[key] = fresh; // bir slot ikki marta kelsa qayta qo'shmaymiz
            }
            imported++;
        }
        if (imported > 0) await db.SaveChangesAsync();
        return new TopicImportResultDto(imported, skipped, errors.Count, errors);
    }
}
