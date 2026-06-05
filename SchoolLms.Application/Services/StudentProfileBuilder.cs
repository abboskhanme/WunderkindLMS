using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// O'quvchi shaxsiy daftari — bitta o'quvchi haqida BARCHA ma'lumotni jamlaydi:
/// profil, o'zlashtirish (<see cref="StudentReportBuilder"/>), qatnashish (<see cref="Analytics"/>
/// mantig'i), davomat sabablari, intizomiy ball, topshiriqlar (<see cref="AssignmentService"/>),
/// oylik baholash va jurnaldagi uy vazifa/xulq belgilari.
/// </summary>
public static class StudentProfileBuilder
{
    public static async Task<StudentNotebookDto> BuildAsync(IAppDbContext db, Student st)
    {
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Name == st.ClassName);
        var classId = cls?.Id;

        var report = await StudentReportBuilder.BuildAsync(db, st);
        var entries = await db.JournalEntries.Where(e => e.StudentId == st.Id).ToListAsync();
        var reasons = await db.AbsenceReasons.ToListAsync();
        var reasonMap = reasons.ToDictionary(r => r.Id);
        var lateSet = reasons.Where(r => r.IsLate).Select(r => r.Id).ToHashSet();

        // ---- Qatnashish (o'tilgan / qatnashgan) — Analytics bilan bir xil ----
        var conductedNotes = classId is null
            ? new List<(string SubjectId, string Date, int Period, int SubGroup)>()
            : (await db.LessonNotes.Where(n => n.Conducted && n.ClassId == classId)
                    .Select(n => new { n.SubjectId, n.Date, n.Period, n.SubGroup }).ToListAsync())
                .Select(n => (n.SubjectId, n.Date, n.Period, n.SubGroup)).ToList();
        var studentConducted = conductedNotes
            .Where(c => c.SubGroup == 0 || c.SubGroup == st.SubGroup)
            .Select(c => (c.SubjectId, c.Date, c.Period)).ToHashSet();
        var conducted = studentConducted.Count;
        var absent = entries.Count(e => e.ReasonId != null && !lateSet.Contains(e.ReasonId)
            && studentConducted.Contains((e.SubjectId, e.Date, e.Period)));
        var attended = Math.Max(0, conducted - absent);
        var pct = conducted > 0 ? (int)Math.Round((double)attended / conducted * 100) : 0;

        // ---- Davomat sabablari taqsimoti (barcha belgilar) ----
        var reasonCounts = entries
            .Where(e => e.ReasonId != null && reasonMap.ContainsKey(e.ReasonId))
            .GroupBy(e => e.ReasonId!)
            .Select(g =>
            {
                var r = reasonMap[g.Key];
                return new AttendanceReasonCountDto(r.Id, r.Name, r.Short, r.IsLate, g.Count());
            })
            .OrderByDescending(x => x.Count).ToList();

        // ---- Intizomiy ball (100 + plus − minus) ----
        var manual = await db.DisciplinePoints.Where(p => p.StudentId == st.Id).ToListAsync();
        int plus = 0, minus = 0;
        void Apply(int pts) { if (pts > 0) plus += pts; else if (pts < 0) minus += -pts; }
        foreach (var m in manual) Apply(m.Points);
        foreach (var e in entries)
            if (e.ReasonId != null && reasonMap.TryGetValue(e.ReasonId, out var r) && r.Points != 0) Apply(r.Points);
        var disciplineScore = 100 + plus - minus;

        var dPoints = manual.Select(p => new DisciplinePointDto(
            p.Id, p.StudentId, string.IsNullOrEmpty(p.ReasonName) ? "—" : p.ReasonName,
            p.Points, p.Note, p.CreatedAt, p.CreatedBy, "manual")).ToList();
        foreach (var e in entries)
            if (e.ReasonId != null && reasonMap.TryGetValue(e.ReasonId, out var r) && r.Points != 0)
                dPoints.Add(new DisciplinePointDto(e.Id, st.Id, r.Name, r.Points, "Jurnal davomati", e.Date, "", "attendance"));
        dPoints = dPoints.OrderByDescending(p => p.CreatedAt, StringComparer.Ordinal).ToList();

        // ---- Topshiriqlar ballari ----
        var assignments = classId is null
            ? new StudentAssignmentScoresDto(0, 0, 0, 0, [])
            : await AssignmentService.ScoresForStudentAsync(db, classId, st.Id);

        // ---- Oylik baholash (fan kesimida + umumiy fanlar o'rtachasi) ----
        var evalTypes = await db.EvaluationTypes.OrderBy(t => t.CreatedAt)
            .Select(t => new EvaluationTypeDto(t.Id, t.Name, t.Description)).ToListAsync();
        var subjNames = report.Subjects.ToDictionary(x => x.Id, x => x.Name);
        // Faqat fan kesimidagi baholar — "Umumiy" (SubjectId="") ga baho qo'yilmaydi,
        // umumiy statistika butunlay fanlar o'rtachasidan hisoblanadi.
        var evalGrades = (await db.EvaluationGrades.Where(g => g.StudentId == st.Id).ToListAsync())
            .Where(g => !string.IsNullOrEmpty(g.Month) && !string.IsNullOrEmpty(g.SubjectId))
            .ToList();

        // Fan kesimida: har fan uchun (oy → turlar bo'yicha baho) + fan o'rtachasi.
        var evalsBySubject = evalGrades
            .GroupBy(g => g.SubjectId ?? "")
            .Select(sg =>
            {
                var months = sg.GroupBy(g => g.Month)
                    .OrderBy(m => m.Key, StringComparer.Ordinal)
                    .Select(m =>
                    {
                        var dict = m.GroupBy(x => x.EvaluationTypeId).ToDictionary(x => x.Key, x => x.First().Score);
                        return new MonthlyEvaluationDto(m.Key, dict, dict.Count > 0 ? Math.Round(dict.Values.Average(), 1) : 0);
                    }).ToList();
                var subjAvg = sg.Any() ? Math.Round(sg.Average(x => x.Score), 1) : 0;
                var name = string.IsNullOrEmpty(sg.Key) ? "Umumiy" : subjNames.GetValueOrDefault(sg.Key, "—");
                return new SubjectEvaluationDto(sg.Key, name, subjAvg, months);
            })
            .OrderBy(x => x.SubjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Umumiy: oy → tur bo'yicha FANLAR O'RTACHASI (yaxlitlangan); oylik avg = o'sha oy barcha baholari o'rtachasi.
        var evals = evalGrades
            .GroupBy(g => g.Month)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var dict = g.GroupBy(x => x.EvaluationTypeId)
                    .ToDictionary(x => x.Key, x => (int)Math.Round(x.Average(v => v.Score)));
                var avg = g.Any() ? Math.Round(g.Average(v => v.Score), 1) : 0;
                return new MonthlyEvaluationDto(g.Key, dict, avg);
            }).ToList();

        // ---- Uy vazifa + xulq (jami + choraklik trend) ----
        var hwDone = entries.Count(e => e.Homework == 1);
        var hwMissed = entries.Count(e => e.Homework == 2);
        var bGood = entries.Count(e => e.Behavior == 1);
        var bBad = entries.Count(e => e.Behavior == 2);
        // Choraklik — jurnaldagidek: belgilangan choraklar (1..4) bo'yicha (sozlanmagan bo'lsa
        // mavjud bo'lganlari). Belgisi yo'q choraklarda 0 ko'rinadi.
        var markByQ = entries.Where(e => e.Homework != 0 || e.Behavior != 0)
            .GroupBy(e => e.Quarter).ToDictionary(g => g.Key, g => g.ToList());
        var quarterNos = await db.Quarters.OrderBy(q => q.Quarter).Select(q => q.Quarter).ToListAsync();
        if (quarterNos.Count == 0) quarterNos = markByQ.Keys.OrderBy(k => k).ToList();
        var trend = quarterNos.Select(q =>
        {
            var list = markByQ.GetValueOrDefault(q) ?? new List<JournalEntry>();
            return new QuarterMarksDto(q,
                list.Count(e => e.Homework == 1), list.Count(e => e.Homework == 2),
                list.Count(e => e.Behavior == 1), list.Count(e => e.Behavior == 2));
        }).ToList();

        // ---- O'rtacha baho (barcha fan/chorak baholarining o'rtachasi) ----
        var allGradeVals = report.Grades.Values.SelectMany(d => d.Values).ToList();
        var avgGrade = allGradeVals.Count > 0 ? Math.Round(allGradeVals.Average(), 1) : 0;

        return new StudentNotebookDto(
            st.Id, st.FullName, st.ClassName, report.HomeroomTeacher,
            st.ParentFullName, st.ParentPhone, st.Gender, st.BirthDate,
            st.EnrollmentDate, st.Balance, st.BirthCertificateUrl,
            st.Address, st.DiscountPct, st.DiscountAmount, st.DiscountNote,
            st.SubGroup, st.ParentPassportUrl,
            report.Subjects, report.Grades, avgGrade,
            report.Attendance, conducted, attended, pct, reasonCounts,
            disciplineScore, plus, minus, dPoints,
            assignments,
            evalTypes, evals, evalsBySubject,
            hwDone, hwMissed, bGood, bBad, trend);
    }
}
