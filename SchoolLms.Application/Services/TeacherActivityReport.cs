using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// O'qituvchilarning faollik hisobotini quradi: dars o'tyaptimi (jadvalga nisbatan bajarilish),
/// baho qo'ymoqdami, mavzu va uy vazifa bermoqdami.
///
/// <para>Jurnal yozuvlarida (LessonNote/JournalEntry) o'qituvchi id'si yo'q — kim o'qitishi dars
/// jadvalidan (ScheduleLesson.TeacherId) (ClassId, SubjectId, SubGroup) kaliti orqali aniqlanadi.
/// "Reja" (Expected) = haftalarga biriktirilgan jadvaldan kelib chiqib BUGUNGACHA bo'lishi kerak
/// bo'lgan dars sonidir.</para>
/// </summary>
public static class TeacherActivityReport
{
    /// <summary>Bir (o'qituvchi, sinf, fan, guruh) kesimi bo'yicha yig'ma sonlar.</summary>
    private sealed class Agg
    {
        public int Expected, Conducted, Topic, Homework, Grades;
    }

    private sealed class Computed
    {
        public Dictionary<(string Teacher, string Class, string Subject, int Sub), Agg> ByKey = new();
        public Dictionary<string, string> LastActivity = new(); // teacherId -> ISO sana
    }

    /// <summary>Umumiy ko'rinish: barcha o'qituvchilar bo'yicha bitta-bitta qator.</summary>
    public static async Task<List<TeacherReportRowDto>> BuildOverviewAsync(IAppDbContext db, int quarter)
    {
        var (c, teachers, _, _) = await ComputeAsync(db, quarter);
        var rows = teachers
            .Select(t => Row(t.Id, t.FullName, t.IsArchived, KeysFor(c, t.Id), c.LastActivity.GetValueOrDefault(t.Id)))
            .OrderBy(r => r.IsArchived).ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return rows;
    }

    /// <summary>Bitta o'qituvchining batafsil hisoboti (sinf/fan yoyilmasi bilan).</summary>
    public static async Task<TeacherReportDetailDto?> BuildDetailAsync(IAppDbContext db, string teacherId, int quarter)
    {
        var (c, teachers, classNames, subjectNames) = await ComputeAsync(db, quarter);
        var teacher = teachers.FirstOrDefault(t => t.Id == teacherId);
        if (teacher is null) return null;

        var keys = KeysFor(c, teacherId);
        var total = Row(teacher.Id, teacher.FullName, teacher.IsArchived, keys, c.LastActivity.GetValueOrDefault(teacherId));

        var breakdown = keys
            .Select(kv => new TeacherReportBreakdownDto(
                classNames.GetValueOrDefault(kv.Key.Class, kv.Key.Class),
                subjectNames.GetValueOrDefault(kv.Key.Subject, kv.Key.Subject),
                kv.Key.Sub,
                kv.Value.Expected, kv.Value.Conducted, Pct(kv.Value.Conducted, kv.Value.Expected, cap: true),
                kv.Value.Grades, Pct(kv.Value.Topic, kv.Value.Conducted), Pct(kv.Value.Homework, kv.Value.Conducted)))
            .OrderBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.SubjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.SubGroup)
            .ToList();

        return new TeacherReportDetailDto(
            total.TeacherId, total.FullName, total.IsArchived,
            total.Expected, total.Conducted, total.DonePct,
            total.Grades, total.TopicPct, total.HomeworkPct,
            total.LastActivity, total.Status, breakdown);
    }

    // ---------- Ichki hisoblash ----------

    private static List<KeyValuePair<(string Teacher, string Class, string Subject, int Sub), Agg>>
        KeysFor(Computed c, string teacherId) =>
        c.ByKey.Where(kv => kv.Key.Teacher == teacherId).ToList();

    private static TeacherReportRowDto Row(
        string teacherId, string fullName, bool isArchived,
        List<KeyValuePair<(string Teacher, string Class, string Subject, int Sub), Agg>> keys,
        string? lastActivity)
    {
        var exp = keys.Sum(k => k.Value.Expected);
        var cond = keys.Sum(k => k.Value.Conducted);
        var topic = keys.Sum(k => k.Value.Topic);
        var hw = keys.Sum(k => k.Value.Homework);
        var grades = keys.Sum(k => k.Value.Grades);

        var donePct = Pct(cond, exp, cap: true);
        // Holat: rejaga nisbatan bajarilish (reja bo'lmasa — faollik bor-yo'qligiga qarab).
        var score = exp > 0 ? donePct ?? 0 : (cond > 0 ? 100 : 0);
        var status = (exp == 0 && cond == 0 && grades == 0)
            ? "none"
            : (score >= 70 ? "active" : "low");

        return new TeacherReportRowDto(
            teacherId, fullName, isArchived,
            exp, cond, donePct, grades,
            Pct(topic, cond), Pct(hw, cond), lastActivity, status);
    }

    /// <summary>a/b foizi (butun son). b=0 → null. cap=true bo'lsa 100 dan oshmaydi.</summary>
    private static int? Pct(int a, int b, bool cap = false)
    {
        if (b <= 0) return null;
        var p = (int)Math.Round(a * 100.0 / b);
        return cap && p > 100 ? 100 : p;
    }

    private static async Task<(
        Computed Computed,
        List<Teacher> Teachers,
        Dictionary<string, string> ClassNames,
        Dictionary<string, string> SubjectNames)> ComputeAsync(IAppDbContext db, int quarter)
    {
        var teachers = await db.Teachers.ToListAsync();
        var classes = await db.Classes.ToListAsync();
        var classNames = classes.ToDictionary(c => c.Id, c => c.Name);
        var subjectNames = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);
        var templates = await db.ScheduleTemplates.Include(t => t.Lessons).ToListAsync();
        var assignments = await db.WeekAssignments.ToListAsync();

        var quartersQ = db.Quarters.AsQueryable();
        if (quarter > 0) quartersQ = quartersQ.Where(q => q.Quarter == quarter);
        var quarters = await quartersQ.OrderBy(q => q.Quarter).ToListAsync();

        var notesQ = db.LessonNotes.AsQueryable();
        if (quarter > 0) notesQ = notesQ.Where(n => n.Quarter == quarter);
        var notes = await notesQ.ToListAsync();

        var entriesQ = db.JournalEntries.Where(e => e.Grade != null);
        if (quarter > 0) entriesQ = entriesQ.Where(e => e.Quarter == quarter);
        var entries = await entriesQ.ToListAsync();

        // (ClassId, SubjectId, SubGroup) -> TeacherId; va (ClassId, SubjectId) -> o'qituvchilar to'plami.
        var exact = new Dictionary<(string, string, int), string>();
        var bySubject = new Dictionary<(string, string), HashSet<string>>();
        foreach (var tpl in templates)
            foreach (var l in tpl.Lessons)
            {
                if (string.IsNullOrEmpty(l.TeacherId)) continue;
                exact[(tpl.ClassId, l.SubjectId, l.SubGroup)] = l.TeacherId;
                if (!bySubject.TryGetValue((tpl.ClassId, l.SubjectId), out var set))
                    bySubject[(tpl.ClassId, l.SubjectId)] = set = new();
                set.Add(l.TeacherId);
            }

        string? Attribute(string classId, string subjectId, int sub)
        {
            if (exact.TryGetValue((classId, subjectId, sub), out var t1)) return t1;
            if (exact.TryGetValue((classId, subjectId, 0), out var t0)) return t0;
            if (bySubject.TryGetValue((classId, subjectId), out var set) && set.Count == 1) return set.First();
            return null;
        }

        var c = new Computed();
        Agg Key(string teacher, string classId, string subjectId, int sub)
        {
            var k = (teacher, classId, subjectId, sub);
            if (!c.ByKey.TryGetValue(k, out var a)) c.ByKey[k] = a = new();
            return a;
        }
        void Touch(string teacher, string date)
        {
            if (string.IsNullOrEmpty(date)) return;
            if (!c.LastActivity.TryGetValue(teacher, out var cur) || string.CompareOrdinal(date, cur) > 0)
                c.LastActivity[teacher] = date;
        }

        // --- Reja (Expected): jadval × biriktirilgan haftalar, bugungacha ---
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        foreach (var q in quarters)
        {
            var weeks = ScheduleMath.GetQuarterWeeks(q.StartDate, q.EndDate);
            foreach (var cls in classes)
                foreach (var w in weeks)
                {
                    var a = assignments.FirstOrDefault(x =>
                        x.ClassId == cls.Id && x.Quarter == q.Quarter && x.Week == w.Week);
                    if (a?.TemplateId is null) continue;
                    var tpl = templates.FirstOrDefault(t => t.Id == a.TemplateId);
                    if (tpl is null) continue;
                    var monday = ScheduleMath.MondayOfISO(w.StartISO);
                    foreach (var l in tpl.Lessons)
                    {
                        if (string.IsNullOrEmpty(l.TeacherId)) continue;
                        var date = ScheduleMath.AddDaysISO(monday, l.Day);
                        if (string.CompareOrdinal(date, q.StartDate) < 0 ||
                            string.CompareOrdinal(date, q.EndDate) > 0) continue;
                        if (string.CompareOrdinal(date, today) > 0) continue; // kelajak dars — hali reja emas
                        Key(l.TeacherId, cls.Id, l.SubjectId, l.SubGroup).Expected++;
                    }
                }
        }

        // --- O'tilgan darslar + mavzu/uy vazifa (LessonNote) ---
        foreach (var n in notes)
        {
            if (!n.Conducted) continue;
            var teacher = Attribute(n.ClassId, n.SubjectId, n.SubGroup);
            if (teacher is null) continue;
            var agg = Key(teacher, n.ClassId, n.SubjectId, n.SubGroup);
            agg.Conducted++;
            if (!string.IsNullOrWhiteSpace(n.Topic)) agg.Topic++;
            if (!string.IsNullOrWhiteSpace(n.Homework)) agg.Homework++;
            Touch(teacher, n.Date);
        }

        // --- Qo'yilgan baholar (JournalEntry) ---
        foreach (var e in entries)
        {
            var teacher = Attribute(e.ClassId, e.SubjectId, e.SubGroup);
            if (teacher is null) continue;
            Key(teacher, e.ClassId, e.SubjectId, e.SubGroup).Grades++;
            Touch(teacher, e.Date);
        }

        return (c, teachers, classNames, subjectNames);
    }
}
