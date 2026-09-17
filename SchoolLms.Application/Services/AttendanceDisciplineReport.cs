using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Davomat intizomi bo'yicha hisobot (docs/modules/existing-module-gaps.md §4, #6):
/// <b>kim davomat orqali intizomiy ball yo'qotmoqda va qaysi sinfda</b>.
///
/// <para>
/// Manbalar — hammasi allaqachon bazada: jurnaldagi davomat belgilari
/// (<c>JournalEntry.ReasonId</c>), sababning balli (<c>AbsenceReason.Points</c>) va qo'lda
/// kiritilgan intizomiy ballar (<c>DisciplinePoint</c>). Yangi jadval ham, yangi ustun ham
/// qo'shilmagan.
/// </para>
///
/// <para>
/// <b>IKKI XIL TA'RIF ATAYLAB SAQLANGAN — chalkashmang.</b>
/// </para>
/// <list type="number">
///   <item>
///     <b>Yo'qlik / kechikish / tekshirilmagan</b> — <c>DashboardController</c> va
///     <c>Analytics.cs</c> dagi ta'rifning AYNAN o'zi: hisob faqat O'TILGAN darslar bo'yicha
///     (<c>LessonNote.Conducted</c>), "kech keldi" turidagi sabab yo'qlik EMAS, davomati
///     belgilanmagan katak esa hech qachon "keldi" deb hisoblanmaydi — u alohida
///     <c>Unchecked</c> ustuni. Aks holda hech kim davomat qo'ymagan kun 100% bo'lib ko'rinardi.
///   </item>
///   <item>
///     <b>Ball</b> — <c>DisciplineController.GetScores</c> ta'rifining aynan o'zi: sababi
///     balli bo'lgan HAR davomat belgisi ballga ta'sir qiladi, dars "o'tilgan" deb
///     belgilanganidan qat'i nazar, va "kech keldi" ham (uning balli bor — masalan −5).
///     Nega farq qiladi: ball — ikkinchi ekranda (Ballar nazorati) ko'rinadigan HAQIQIY
///     qoldiq. Agar bu hisobot ballni boshqacha sanaganda, ikki ekran bir o'quvchi uchun
///     ikki xil raqam ko'rsatardi va ikkalasiga ham ishonib bo'lmasdi.
///   </item>
/// </list>
///
/// <para>
/// <c>Remaining</c> (qoldi) — BUTUN TARIX bo'yicha: 100 + qo'lda kiritilgan ballar + jurnal
/// davomati ballari. Davr filtri unga ta'sir qilmaydi, chunki qoldiq davrga bo'linmaydi.
/// </para>
///
/// <para>
/// <b>O'QUV GURUHI</b> (G-13, students-parity.md §2.1.4). Guruh darsi ham o'tiladi va unda
/// ham davomat belgilanadi, ya'ni u o'quvchining imkoniyatlari (maxraji) va ballariga
/// SINF darsi kabi kiradi. Qator esa o'quvchining O'Z SINFI ostida qoladi — hisobot
/// "kim qaysi sinfda ball yo'qotmoqda" degan savolga javob beradi, guruh alohida qator
/// bo'lmaydi. Cut-over o'chirgichi o'chiq bo'lsa guruh umuman yo'q va hisobot bugungi
/// raqamning aynan o'zini beradi (§4.3).
/// </para>
/// </summary>
public static class AttendanceDisciplineReport
{
    /// <summary>Har o'quvchi shundan boshlaydi — <c>DisciplineController.BaseScore</c> bilan bir xil.</summary>
    private const int BaseScore = 100;

    /// <summary>Bitta dars kataqchasi: fan + sana + dars raqami.</summary>
    private readonly record struct Slot(string SubjectId, string Date, int Period);

    /// <summary>
    /// Hisobotni quradi.
    /// </summary>
    /// <param name="db">Baza konteksti.</param>
    /// <param name="from">Boshlanish sanasi, ISO "yyyy-MM-dd" (shu kun kiradi).</param>
    /// <param name="to">Tugash sanasi, ISO "yyyy-MM-dd" (shu kun kiradi).</param>
    /// <param name="classId">Faqat bitta sinf kerak bo'lsa — uning id'si.</param>
    public static async Task<AttendanceDisciplineReportDto> BuildAsync(
        IAppDbContext db, string from, string to, string? classId = null)
    {
        // `DisciplinePoint.CreatedAt` — to'liq vaqt ("o" formati), sana emas. Shuning uchun
        // yuqori chegara KEYINGI kunning boshi: "2026-09-16T23:59" < "2026-09-17".
        var toExclusive = DateOnly.TryParseExact(to, "yyyy-MM-dd", out var toDate)
            ? toDate.AddDays(1).ToString("yyyy-MM-dd")
            : to + "z";

        var classes = await db.Classes.AsNoTracking()
            .Where(c => classId == null || c.Id == classId)
            .OrderBy(c => c.Grade).ThenBy(c => c.Name)
            .ToListAsync();
        var classNames = classes.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

        // Arxivlangan o'quvchi hisobotga kirmaydi — Ballar nazorati va bosh sahifadagi kabi.
        var students = (await db.Students.AsNoTracking()
                .Where(s => !s.IsArchived)
                .Select(s => new { s.Id, s.FullName, s.ClassName, s.SubGroup })
                .ToListAsync())
            .Where(s => classNames.Contains(s.ClassName))
            .ToList();

        var reasons = await db.AbsenceReasons.AsNoTracking()
            .Select(r => new { r.Id, r.Name, r.IsLate, r.Points })
            .ToListAsync();
        var reasonById = reasons.ToDictionary(r => r.Id, StringComparer.Ordinal);

        // O'quvchi → uning faol o'quv guruhlari (G-13). O'chirgich o'chiq bo'lsa — bo'sh.
        var groupsOn = await LessonRoster.GroupLessonsEnabledAsync(db);
        var groupsByStudent = await GroupIdsByStudentAsync(db);
        var groupIdsInScope = students
            .SelectMany(s => groupsByStudent.GetValueOrDefault(s.Id) ?? [])
            .Distinct(StringComparer.Ordinal).ToList();

        // O'tilgan darslar — davomat maxraji. O'tilmagan dars umuman hisobga olinmaydi.
        var conducted = (await db.LessonNotes.AsNoTracking()
                .Where(n => n.Conducted
                    && (groupsOn || n.OwnerKind != LessonOwnerKind.Group)
                    && (classId == null || n.ClassId == classId || groupIdsInScope.Contains(n.ClassId))
                    && string.Compare(n.Date, from) >= 0 && string.Compare(n.Date, to) <= 0)
                .Select(n => new { n.ClassId, n.SubjectId, n.Date, n.Period, n.SubGroup })
                .ToListAsync())
            .GroupBy(n => n.ClassId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(n => (Slot: new Slot(n.SubjectId, n.Date, n.Period), n.SubGroup)).ToList(),
                StringComparer.Ordinal);

        // O'chirgich o'chiq — guruh qatorlari UMUMAN o'qilmaydi (sabablar kesimiga ham
        // tushmasin: §4.3 "hech bir raqam qimirlamaydi").
        var entries = (await db.JournalEntries.AsNoTracking()
                .Where(e => (groupsOn || e.OwnerKind != LessonOwnerKind.Group)
                    && (classId == null || e.ClassId == classId || groupIdsInScope.Contains(e.ClassId))
                    && string.Compare(e.Date, from) >= 0 && string.Compare(e.Date, to) <= 0)
                .Select(e => new { e.ClassId, e.StudentId, e.SubjectId, e.Date, e.Period, e.ReasonId })
                .ToListAsync())
            .GroupBy(e => e.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var manualInRange = await db.DisciplinePoints.AsNoTracking()
            .Where(p => string.Compare(p.CreatedAt, from) >= 0
                && string.Compare(p.CreatedAt, toExclusive) < 0)
            .Select(p => new { p.StudentId, p.ReasonId, p.ReasonName, p.Points })
            .ToListAsync();
        var manualByStudent = manualInRange
            .GroupBy(p => p.StudentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Points), StringComparer.Ordinal);

        // ---- Qoldi (butun tarix) — Ballar nazorati bilan bir xil raqam chiqsin ----
        var allManual = (await db.DisciplinePoints.AsNoTracking()
                .GroupBy(p => p.StudentId)
                .Select(g => new { StudentId = g.Key, Points = g.Sum(p => p.Points) })
                .ToListAsync())
            .ToDictionary(x => x.StudentId, x => x.Points, StringComparer.Ordinal);
        var allJournalPoints = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in await db.JournalEntries.AsNoTracking()
            .Where(e => e.ReasonId != null)
            .GroupBy(e => new { e.StudentId, e.ReasonId })
            .Select(g => new { g.Key.StudentId, g.Key.ReasonId, Count = g.Count() })
            .ToListAsync())
        {
            if (row.ReasonId is null || !reasonById.TryGetValue(row.ReasonId, out var r) || r.Points == 0) continue;
            allJournalPoints[row.StudentId] = allJournalPoints.GetValueOrDefault(row.StudentId) + r.Points * row.Count;
        }

        // ---- O'quvchi qatorlari ----
        var studentRows = new List<AttendanceDisciplineStudentDto>(students.Count);
        var classAgg = new Dictionary<string, (int Students, int Opp, int Abs, int Late, int Unchecked, int Points)>(
            StringComparer.Ordinal);

        foreach (var cls in classes)
        {
            var classStudents = students.Where(s => s.ClassName == cls.Name).ToList();
            conducted.TryGetValue(cls.Id, out var classSlots);
            classSlots ??= [];

            var agg = (Students: classStudents.Count, Opp: 0, Abs: 0, Late: 0, Unchecked: 0, Points: 0);

            foreach (var st in classStudents)
            {
                // Bo'lingan darsda o'quvchi faqat O'Z guruhining (yoki butun sinf) darslarida
                // qatnashadi — Analytics.cs dagi qoidaning aynan o'zi.
                var mySlots = classSlots
                    .Where(c => c.SubGroup == 0 || c.SubGroup == st.SubGroup)
                    .Select(c => c.Slot)
                    .ToHashSet();

                // O'quv guruhi darslari ham shu o'quvchining imkoniyati (G-13). Guruhda
                // sinf ichidagi bo'linish yo'q — hamma faol a'zo qatnashadi.
                var myGroupIds = groupsByStudent.GetValueOrDefault(st.Id) ?? [];
                foreach (var groupId in myGroupIds)
                    if (conducted.TryGetValue(groupId, out var groupSlots))
                        mySlots.UnionWith(groupSlots.Select(g => g.Slot));

                entries.TryGetValue(st.Id, out var myEntries);
                myEntries ??= [];
                var myClassEntries = myEntries
                    .Where(e => e.ClassId == cls.Id || myGroupIds.Contains(e.ClassId, StringComparer.Ordinal))
                    .ToList();

                var onConducted = myClassEntries
                    .Where(e => mySlots.Contains(new Slot(e.SubjectId, e.Date, e.Period)))
                    .ToList();

                var absences = 0;
                var lates = 0;
                foreach (var e in onConducted)
                {
                    if (e.ReasonId is null || !reasonById.TryGetValue(e.ReasonId, out var r)) continue;
                    if (r.IsLate) lates++; else absences++;
                }

                var opportunities = mySlots.Count;
                var marked = onConducted
                    .Select(e => new Slot(e.SubjectId, e.Date, e.Period))
                    .ToHashSet().Count;
                var unchecked_ = Math.Max(0, opportunities - marked);

                // Ball — O'TILGANLIK FILTRISIZ (yuqoridagi 2-izoh).
                var attendancePoints = 0;
                foreach (var e in myClassEntries)
                {
                    if (e.ReasonId is null || !reasonById.TryGetValue(e.ReasonId, out var r)) continue;
                    attendancePoints += r.Points;
                }

                var manualPoints = manualByStudent.GetValueOrDefault(st.Id);
                var remaining = BaseScore + allManual.GetValueOrDefault(st.Id) + allJournalPoints.GetValueOrDefault(st.Id);

                agg.Opp += opportunities;
                agg.Abs += absences;
                agg.Late += lates;
                agg.Unchecked += unchecked_;
                agg.Points += attendancePoints;

                // Hisobot mavzusi — belgi tushganlar. Toza o'quvchi qatorlari jadvalni
                // ko'mib tashlardi; sinf va jamlama raqamlari esa HAMMA o'quvchi bo'yicha.
                if (absences == 0 && lates == 0 && attendancePoints == 0 && manualPoints == 0) continue;

                studentRows.Add(new AttendanceDisciplineStudentDto(
                    st.Id, st.FullName, st.ClassName,
                    opportunities, absences, lates, unchecked_,
                    attendancePoints, manualPoints, remaining,
                    opportunities > 0
                        ? Math.Round((double)(opportunities - absences) / opportunities * 100, 1)
                        : null));
            }

            if (agg.Students > 0) classAgg[cls.Id] = agg;
        }

        var classRows = classes
            .Where(c => classAgg.ContainsKey(c.Id))
            .Select(c =>
            {
                var a = classAgg[c.Id];
                return new AttendanceDisciplineClassDto(
                    c.Id, c.Name, a.Students, a.Opp, a.Abs, a.Late, a.Unchecked, a.Points,
                    a.Opp > 0 ? Math.Round((double)(a.Opp - a.Abs) / a.Opp * 100, 1) : null,
                    a.Students > 0 ? Math.Round((double)Math.Abs(Math.Min(0, a.Points)) / a.Students, 1) : 0);
            })
            .OrderBy(r => r.AttendancePoints)
            .ThenBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ---- Sabablar kesimi ----
        var studentIds = students.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var reasonRows = new List<AttendanceDisciplineReasonDto>();
        var attendanceReasonCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in entries)
        {
            if (!studentIds.Contains(kv.Key)) continue;
            foreach (var e in kv.Value)
            {
                if (e.ReasonId is null || !reasonById.ContainsKey(e.ReasonId)) continue;
                attendanceReasonCounts[e.ReasonId] = attendanceReasonCounts.GetValueOrDefault(e.ReasonId) + 1;
            }
        }
        foreach (var (reasonId, count) in attendanceReasonCounts)
        {
            var r = reasonById[reasonId];
            reasonRows.Add(new AttendanceDisciplineReasonDto(
                r.Id, r.Name, "attendance", r.Points, count, r.Points * count));
        }

        foreach (var g in manualInRange
            .Where(p => studentIds.Contains(p.StudentId))
            .GroupBy(p => (p.ReasonId, p.ReasonName, p.Points)))
        {
            reasonRows.Add(new AttendanceDisciplineReasonDto(
                g.Key.ReasonId,
                string.IsNullOrWhiteSpace(g.Key.ReasonName) ? "—" : g.Key.ReasonName,
                "manual", g.Key.Points, g.Count(), g.Key.Points * g.Count()));
        }

        reasonRows = reasonRows
            .OrderBy(r => r.Kind, StringComparer.Ordinal)
            .ThenBy(r => r.TotalPoints)
            .ThenByDescending(r => r.Count)
            .ToList();

        // Jamlama BARCHA faol o'quvchilar bo'yicha — jadvaldagi qatorlar esa faqat belgisi
        // borlar. Ikkisi ataylab farq qiladi: "sinfda 40 ta katak belgilanmagan" degan xabar
        // faqat hamma o'quvchi hisobga olinganda chiqadi.
        var totalOpportunities = classAgg.Values.Sum(a => a.Opp);
        var totalAbsences = classAgg.Values.Sum(a => a.Abs);
        var totals = new AttendanceDisciplineTotalsDto(
            students.Count,
            totalOpportunities,
            totalAbsences,
            classAgg.Values.Sum(a => a.Late),
            classAgg.Values.Sum(a => a.Unchecked),
            classAgg.Values.Sum(a => a.Points),
            manualInRange.Where(p => studentIds.Contains(p.StudentId)).Sum(p => p.Points),
            totalOpportunities > 0
                ? Math.Round((double)(totalOpportunities - totalAbsences) / totalOpportunities * 100, 1)
                : null);

        // Eng ko'p ball yo'qotgan birinchi turadi (ball manfiy — o'sish tartibi).
        studentRows = studentRows
            .OrderBy(r => r.AttendancePoints + r.ManualPoints)
            .ThenByDescending(r => r.Absences)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AttendanceDisciplineReportDto(from, to, totals, classRows, studentRows, reasonRows);
    }

    /// <summary>
    /// O'quvchi id → uning faol (arxivlanmagan) o'quv guruhlarining id'lari, satr
    /// ko'rinishida (<c>class_id</c> ustunidagi qiymat bilan bir xil).
    ///
    /// <para>
    /// Cut-over o'chirgichi o'chiq bo'lsa — BO'SH lug'at, ya'ni hisobot bugungi
    /// raqamning aynan o'zini beradi (§4.3).
    /// </para>
    /// </summary>
    private static async Task<Dictionary<string, List<string>>> GroupIdsByStudentAsync(IAppDbContext db)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (!await LessonRoster.GroupLessonsEnabledAsync(db)) return result;

        var groupIds = (await db.StudyGroups.AsNoTracking()
            .Where(g => !g.IsArchived).Select(g => g.Id).ToListAsync()).ToHashSet();
        if (groupIds.Count == 0) return result;

        var members = await db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.LeftOn == null)
            .Select(m => new { m.GroupId, m.StudentId })
            .ToListAsync();

        foreach (var m in members)
        {
            if (!groupIds.Contains(m.GroupId)) continue;
            if (!result.TryGetValue(m.StudentId, out var list)) result[m.StudentId] = list = [];
            list.Add(m.GroupId.ToString());
        }
        return result;
    }
}
