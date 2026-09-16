using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// O'zlashtirish (fanlar bo'yicha) — Analitika #3. Bu MAVJUD "Baholar hisoboti" oilasining
/// davomi, yangi hisobot oilasi EMAS: baho manbai va qoidalari
/// <c>grades-report/class</c> (<c>ClassAnalyticsController.ClassReport</c>) bilan aynan bir xil.
///
/// <para><b>Bitta o'quvchining bitta fan bo'yicha chorak qiymati:</b> o'qituvchi qo'ygan
/// RASMIY chorak bahosi (<see cref="QuarterGrade"/>) bo'lsa — o'sha; bo'lmasa shu chorakdagi
/// kunlik baholar o'rtachasi. Rasmiy baho har doim ustun turadi.</para>
///
/// <para><b>Katak o'rtachasi</b> — shu sinf o'quvchilarining (o'quvchi × chorak) qiymatlari
/// o'rtachasi. Ya'ni ikki chorak tanlansa, ikkala chorak teng vaznda qatnashadi. Avval
/// o'quvchi bo'yicha, keyin sinf bo'yicha o'rtachalash boshqa (biroz boshqacha) raqam berardi;
/// tanlangan yo'l <c>grades-report/class</c> jadvalidagi ustunlar bilan MOS keladi — hisobot
/// va uning manbasi bir-biriga qarama-qarshi chiqmasligi uchun shunday.</para>
///
/// <para><b>Baho yo'q = null, nol EMAS.</b> Bahosi yo'q fan katagi bo'sh ko'rsatiladi; nol
/// bilan to'ldirish sinf o'rtachasini asossiz pastga tortardi.</para>
/// </summary>
public static class SubjectAttainmentReport
{
    /// <summary>Bitta katak (yoki qator) bo'yicha yig'ib boruvchi hisoblagich.</summary>
    private sealed class Acc
    {
        private int _count;
        private double _sum;
        private int _quality;

        public void Add(double v)
        {
            _count++;
            _sum += v;
            // "Sifat" — 4 va 5 lar ulushi. Yaxlitlash ClassAnalyticsController.Classify bilan bir xil.
            if (Math.Round(v, MidpointRounding.AwayFromZero) >= 4) _quality++;
        }

        public int Values => _count;
        public double? Average => _count > 0 ? Math.Round(_sum / _count, 2) : null;
        public double QualityPct => _count > 0 ? Math.Round(_quality * 100.0 / _count, 1) : 0;
    }

    /// <summary>
    /// Sinf × fan pivoti: har katakda tanlangan choraklar bo'yicha o'rtacha baho, sifat foizi va
    /// chorak yoyilmasi; oxirida maktab o'rtachasi qatori.
    /// </summary>
    public static async Task<SubjectAttainmentReportDto> BuildAsync(
        IAppDbContext db, IReadOnlyCollection<string> classIds, IReadOnlyCollection<int> quarters)
    {
        var quarterList = quarters.Where(q => q is >= 1 and <= 4).Distinct().OrderBy(q => q).ToList();
        if (classIds.Count == 0 || quarterList.Count == 0)
            return new SubjectAttainmentReportDto([], [], [], SchoolRow([], new Acc(), new Dictionary<string, Acc>(), new Dictionary<(string, int), Acc>(), 0));

        var idList = classIds.Distinct().ToList();
        var classes = await db.Classes.AsNoTracking()
            .Where(c => idList.Contains(c.Id))
            .OrderBy(c => c.Grade).ThenBy(c => c.Name)
            .ToListAsync();
        if (classes.Count == 0)
            return new SubjectAttainmentReportDto(quarterList, [], [], SchoolRow([], new Acc(), new Dictionary<string, Acc>(), new Dictionary<(string, int), Acc>(), 0));

        var allSubjects = await db.Subjects.AsNoTracking().ToListAsync();
        var subjectNames = allSubjects.ToDictionary(s => s.Id, s => s.Name, StringComparer.Ordinal);
        var presentIds = classes.Select(c => c.Id).ToList();
        var classNames = classes.Select(c => c.Name).ToList();

        var students = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived && classNames.Contains(s.ClassName))
            .Select(s => new { s.Id, s.ClassName })
            .ToListAsync();

        var templates = await db.ScheduleTemplates.AsNoTracking()
            .Include(t => t.Lessons)
            .Where(t => presentIds.Contains(t.ClassId))
            .ToListAsync();

        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => presentIds.Contains(e.ClassId) && e.Grade != null
                        && quarterList.Contains(e.Quarter))
            .Select(e => new { e.ClassId, e.SubjectId, e.Quarter, e.StudentId, e.Grade })
            .ToListAsync();

        var quarterGrades = await db.QuarterGrades.AsNoTracking()
            .Where(g => presentIds.Contains(g.ClassId) && quarterList.Contains(g.Quarter))
            .Select(g => new { g.ClassId, g.SubjectId, g.Quarter, g.StudentId, g.Grade })
            .ToListAsync();

        // Kunlik baholar: (sinf, fan, chorak, o'quvchi) -> o'rtacha.
        var dailyAvg = entries
            .GroupBy(e => (e.ClassId, e.SubjectId, e.Quarter, e.StudentId))
            .ToDictionary(g => g.Key, g => g.Average(e => (double)e.Grade!.Value));
        // Rasmiy chorak baholari — kunlik o'rtachaning USTIDAN yozadi.
        var officialGrade = new Dictionary<(string, string, int, string), double>();
        foreach (var g in quarterGrades)
            officialGrade[(g.ClassId, g.SubjectId, g.Quarter, g.StudentId)] = g.Grade;

        // Ustunlar to'plami: har sinfning jadvalidagi fanlar birlashmasi (jadval bo'sh bo'lsa —
        // baho qo'yilgan fanlar). Faqat NOMI bor fanlar ustun bo'ladi.
        var studentsByClass = students
            .GroupBy(s => s.ClassName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToList(), StringComparer.Ordinal);
        var subjectsByClass = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var cls in classes)
        {
            var fromSchedule = templates.Where(t => t.ClassId == cls.Id)
                .SelectMany(t => t.Lessons).Select(l => l.SubjectId).Distinct().ToList();
            var fromGrades = entries.Where(e => e.ClassId == cls.Id).Select(e => e.SubjectId)
                .Concat(quarterGrades.Where(g => g.ClassId == cls.Id).Select(g => g.SubjectId))
                .Distinct().ToList();
            subjectsByClass[cls.Id] = fromSchedule.Union(fromGrades, StringComparer.Ordinal)
                .Where(subjectNames.ContainsKey)
                .ToList();
        }

        var columns = subjectsByClass.Values.SelectMany(x => x).Distinct(StringComparer.Ordinal)
            .Select(id => new SubjectDto(id, subjectNames[id]))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var schoolBySubject = columns.ToDictionary(s => s.Id, _ => new Acc(), StringComparer.Ordinal);
        var schoolBySubjectQuarter = new Dictionary<(string, int), Acc>();
        var schoolTotal = new Acc();
        var schoolStudents = 0;

        var rows = new List<SubjectAttainmentRowDto>();
        foreach (var cls in classes)
        {
            var classStudents = studentsByClass.GetValueOrDefault(cls.Name) ?? [];
            schoolStudents += classStudents.Count;

            var bySubject = columns.ToDictionary(s => s.Id, _ => new Acc(), StringComparer.Ordinal);
            var bySubjectQuarter = new Dictionary<(string, int), Acc>();
            var classTotal = new Acc();
            var taught = subjectsByClass[cls.Id].ToHashSet(StringComparer.Ordinal);

            foreach (var subjectId in taught)
                foreach (var q in quarterList)
                    foreach (var studentId in classStudents)
                    {
                        var key = (cls.Id, subjectId, q, studentId);
                        double value;
                        if (officialGrade.TryGetValue(key, out var official)) value = official;
                        else if (dailyAvg.TryGetValue(key, out var daily)) value = daily;
                        else continue; // bu chorakda bu fandan bahosi yo'q — katakka kirmaydi

                        bySubject[subjectId].Add(value);
                        Bucket(bySubjectQuarter, (subjectId, q)).Add(value);
                        classTotal.Add(value);

                        schoolBySubject[subjectId].Add(value);
                        Bucket(schoolBySubjectQuarter, (subjectId, q)).Add(value);
                        schoolTotal.Add(value);
                    }

            rows.Add(new SubjectAttainmentRowDto(
                "class", cls.Id, cls.Name, cls.Grade, classStudents.Count,
                Cells(columns, bySubject, bySubjectQuarter, quarterList),
                classTotal.Average, classTotal.QualityPct));
        }

        return new SubjectAttainmentReportDto(
            quarterList, columns, rows,
            SchoolRow(columns, schoolTotal, schoolBySubject, schoolBySubjectQuarter, schoolStudents,
                quarterList));
    }

    // ------------------------------------------------------------------
    // Yordamchilar
    // ------------------------------------------------------------------

    private static Acc Bucket<TKey>(Dictionary<TKey, Acc> map, TKey key) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var a)) map[key] = a = new Acc();
        return a;
    }

    /// <summary>Kataklar HAR DOIM ustunlar ro'yxati bilan bir xil tartibda va uzunlikda qaytadi.</summary>
    private static List<SubjectAttainmentCellDto> Cells(
        List<SubjectDto> columns,
        Dictionary<string, Acc> bySubject,
        Dictionary<(string, int), Acc> bySubjectQuarter,
        List<int> quarters) =>
        [.. columns.Select(s =>
        {
            var acc = bySubject.GetValueOrDefault(s.Id) ?? new Acc();
            var byQuarter = new Dictionary<int, double>();
            foreach (var q in quarters)
                if (bySubjectQuarter.TryGetValue((s.Id, q), out var qa) && qa.Average is double v)
                    byQuarter[q] = v;
            return new SubjectAttainmentCellDto(s.Id, acc.Values, acc.Average, acc.QualityPct, byQuarter);
        })];

    private static SubjectAttainmentRowDto SchoolRow(
        List<SubjectDto> columns,
        Acc total,
        Dictionary<string, Acc> bySubject,
        Dictionary<(string, int), Acc> bySubjectQuarter,
        int students,
        List<int>? quarters = null) =>
        new("school", "", "Maktab", 0, students,
            Cells(columns, bySubject, bySubjectQuarter, quarters ?? []),
            total.Average, total.QualityPct);
}
