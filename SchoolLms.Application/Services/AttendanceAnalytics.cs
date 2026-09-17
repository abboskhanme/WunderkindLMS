using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Davomat analitikasi (Analitika #5) — bor jurnal belgilaridan quriladi, yangi jadval yo'q.
///
/// <para><b>Ta'rif bitta va u yerda-bu yerda takrorlanmaydi.</b> Present/absent/unchecked
/// ta'rifi <c>Analytics.BuildClass</c> va <c>DashboardController.AttendanceByPeriod</c> bilan
/// aynan bir xil:</para>
/// <list type="bullet">
///   <item><b>Maxraj</b> — FAQAT o'tilgan darslar (<c>LessonNote.Conducted = true</c>) ×
///     shu darsga tegishli o'quvchilar. O'tilmagan dars hisobga kirmaydi: unda tekshiriladigan
///     narsa yo'q.</item>
///   <item><b>Guruh (SubGroup)</b> — bo'lingan darsda o'quvchi faqat O'Z guruhining (yoki butun
///     sinf, <c>SubGroup = 0</c>) darslari bo'yicha hisoblanadi. Boshqa guruh darsi uning
///     maxrajiga kirmaydi.</item>
///   <item><b>Kech keldi</b> (<c>AbsenceReason.IsLate</c>) — yo'qlik EMAS: o'quvchi darsda
///     qatnashgan. U <c>Present</c> ichida qoladi va alohida son sifatida ko'rsatiladi.</item>
///   <item><b>Tekshirilmagan</b> — dars o'tilgan, lekin o'quvchi uchun jurnalda YOZUV YO'Q.
///     U hech qachon "keldi"ga qo'shilmaydi (bosh sahifadagi ustun bilan bir xil sabab).</item>
///   <item><b>O'quv guruhi</b> (G-13, students-parity.md §2.1.4) — guruh darsi ham
///     o'quvchining IMKONIYATI: u ham o'tilgan dars, unda ham davomat belgilanadi.
///     Guruh darsi har bir qatnashuvchining O'Z SINFI qatoriga yoziladi (§2.1.6 G-15:
///     guruh baholari o'quvchining sinfi ostida sanaladi), guruhning o'zi alohida
///     qator bo'lmaydi — aks holda "sinfda nechta o'quvchi" ustuni ma'nosini yo'qotardi.
///     Cut-over o'chirgichi (<c>group_lessons_enabled</c>) o'chiq bo'lsa guruh UMUMAN
///     yo'q: quyidagi sikl bugungi siklning aynan o'zi bo'lib qoladi.</item>
/// </list>
///
/// <para><b>Sababli / sababsiz.</b> Bazada "sababli" bayrog'i YO'Q va uni qo'shish bu ish
/// doirasidan tashqarida (migratsiyasiz). Shuning uchun ajratish MAVJUD ma'lumotdan olinadi:
/// sabab nomida "sababsiz" bo'lsa YOKI sababga manfiy intizomiy ball berilgan bo'lsa
/// (<c>AbsenceReason.Points &lt; 0</c> — "Ball sabablar" bo'limida maktabning o'zi belgilaydi)
/// — sababsiz; qolgan yo'qliklar — sababli. Xom haqiqat baribir yo'qolmaydi: hisobot har bir
/// sabab bo'yicha alohida sonni ham qaytaradi (<c>Reasons</c>).</para>
/// </summary>
public static class AttendanceAnalytics
{
    /// <summary>Sukut bo'yicha davr uzunligi (kun) — bugundan orqaga.</summary>
    private const int DefaultRangeDays = 13;

    /// <summary>Yig'ib borish uchun o'zgaruvchan hisoblagich (DTO — faqat chiqishda).</summary>
    private sealed class Tally
    {
        public int Lessons, Opportunities, Present, Absent, Excused, Unexcused, Late, Unchecked;

        public AttendanceTallyDto ToDto()
        {
            double? Pct(int part) => Opportunities > 0
                ? Math.Round(part * 100.0 / Opportunities, 1)
                : null;
            return new AttendanceTallyDto(
                Lessons, Opportunities, Present, Absent, Excused, Unexcused, Late, Unchecked,
                Pct(Present), Pct(Absent), Pct(Unchecked));
        }
    }

    /// <summary>
    /// Davomat roll-up'i: davr bo'yicha jami, sinflar kesimi, tanlangan kunning dars soatlari
    /// kesimi, kunlik trend va sabablar taqsimoti.
    /// </summary>
    /// <param name="classId">Bitta sinf; bo'sh/null bo'lsa — butun maktab.</param>
    /// <param name="from">Davr boshi "yyyy-MM-dd" (noto'g'ri bo'lsa — sukut davri).</param>
    /// <param name="to">Davr oxiri "yyyy-MM-dd".</param>
    /// <param name="day">Dars soatlari kesimi uchun kun; berilmasa — davrning oxirgi kuni.</param>
    public static async Task<AttendanceAnalyticsDto> BuildAsync(
        IAppDbContext db, string? classId, string? from, string? to, string? day)
    {
        var (fromDate, toDate) = NormalizeRange(from, to);
        var dayIso = IsIso(day) && string.CompareOrdinal(day!, fromDate) >= 0
                     && string.CompareOrdinal(day!, toDate) <= 0
            ? day!
            : toDate;

        var wantClass = string.IsNullOrWhiteSpace(classId) ? null : classId;

        // Arxivlangan sinf/o'quvchi hisobotga kirmaydi — ular bugungi davomat emas, tarix.
        var classes = await db.Classes.AsNoTracking()
            .Where(c => !c.IsArchived && (wantClass == null || c.Id == wantClass))
            .OrderBy(c => c.Grade).ThenBy(c => c.Name)
            .ToListAsync();

        var reasonRows = await db.AbsenceReasons.AsNoTracking().ToListAsync();
        var lateIds = reasonRows.Where(r => r.IsLate).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var unexcusedIds = reasonRows.Where(IsUnexcused).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        if (classes.Count == 0)
            return Empty(fromDate, toDate, wantClass, dayIso, reasonRows, unexcusedIds);

        var classIds = classes.Select(c => c.Id).ToList();
        var classNames = classes.Select(c => c.Name).ToList();

        var students = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived && classNames.Contains(s.ClassName))
            .Select(s => new { s.Id, s.ClassName, s.SubGroup })
            .ToListAsync();

        // Sinf nomi -> sinf id (o'quvchi sinfga NOM orqali bog'langan — loyihaning mavjud modeli).
        var classIdByName = classes
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        var studentsByClass = students
            .Where(s => classIdByName.ContainsKey(s.ClassName))
            .GroupBy(s => classIdByName[s.ClassName], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(s => (s.Id, s.SubGroup)).ToList(), StringComparer.Ordinal);

        // O'quv guruhlari (G-13). O'chirgich o'chiq bo'lsa bu lug'at BO'SH bo'ladi va
        // quyidagi hamma narsa bugungidek ishlaydi.
        var groupRoster = await GroupRosterAsync(db, students
            .Where(s => classIdByName.ContainsKey(s.ClassName))
            .ToDictionary(s => s.Id, s => classIdByName[s.ClassName], StringComparer.Ordinal));
        var ownerIds = classIds.Concat(groupRoster.Keys).ToList();

        // Sanalar "yyyy-MM-dd" satr — leksikografik taqqoslash xronologik bilan bir xil.
        var notes = await db.LessonNotes.AsNoTracking()
            .Where(n => n.Conducted && ownerIds.Contains(n.ClassId)
                        && string.Compare(n.Date, fromDate) >= 0
                        && string.Compare(n.Date, toDate) <= 0)
            .Select(n => new { n.ClassId, n.SubjectId, n.Date, n.Period, n.SubGroup, n.OwnerKind })
            .ToListAsync();

        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => ownerIds.Contains(e.ClassId)
                        && string.Compare(e.Date, fromDate) >= 0
                        && string.Compare(e.Date, toDate) <= 0)
            .Select(e => new { e.ClassId, e.SubjectId, e.Date, e.Period, e.StudentId, e.ReasonId })
            .ToListAsync();

        // (sinf, fan, sana, dars raqami, o'quvchi) -> sabab id (yoki null = belgilangan, sababsiz emas).
        // Bir kalitga ikkita yozuv tushsa birinchisi qoladi: bunday holat ma'lumot nuqsoni,
        // uni jimgina ikki marta sanash notog'ri bo'lardi.
        var entryByKey = new Dictionary<(string, string, string, int, string), string?>();
        foreach (var e in entries)
            entryByKey.TryAdd((e.ClassId, e.SubjectId, e.Date, e.Period, e.StudentId), e.ReasonId);

        var total = new Tally();
        var byClass = classIds.ToDictionary(id => id, _ => new Tally(), StringComparer.Ordinal);
        var byPeriod = new Dictionary<int, Tally>();
        var byDate = new Dictionary<string, Tally>(StringComparer.Ordinal);
        var reasonCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var n in notes)
        {
            var isGroup = n.OwnerKind == LessonOwnerKind.Group;

            // Shu darsda kim qatnashadi va uning qatori qaysi SINFda. Sinf darsida —
            // sinfning o'z o'quvchilari (bo'linish filtri bilan); guruh darsida —
            // guruhning faol a'zolari, har biri O'Z sinfi qatoriga.
            List<(string StudentId, string ClassId)> participants;
            if (isGroup)
            {
                if (!groupRoster.TryGetValue(n.ClassId, out var members)) continue;
                participants = members;
            }
            else
            {
                if (!studentsByClass.TryGetValue(n.ClassId, out var classStudents)) continue;
                participants = [.. classStudents
                    // Bo'lingan darsda faqat o'z guruhi (yoki butun sinf darsi).
                    .Where(s => n.SubGroup == 0 || n.SubGroup == s.SubGroup)
                    .Select(s => (s.Id, n.ClassId))];
            }

            var dateTally = Get(byDate, n.Date);
            // Dars soatlari kesimi FAQAT tanlangan kun uchun — boshqa kunlarni aralashtirish
            // "3-darsda 40 ta yo'q" degan ma'nosiz raqam berardi.
            var periodTally = n.Date == dayIso ? Get(byPeriod, n.Period) : null;

            total.Lessons++;
            dateTally.Lessons++;
            if (periodTally is not null) periodTally.Lessons++;
            // Sinf darsi — o'sha sinfning bitta darsi. Guruh darsi esa har bir BOQUVCHI
            // sinf uchun ham bitta dars (o'sha sinfning bolalari o'sha kuni yana bir
            // darsda bo'lgan), lekin jami (`total`) bo'yicha — BITTA.
            foreach (var feedingClassId in isGroup
                         ? participants.Select(p => p.ClassId).Distinct(StringComparer.Ordinal)
                         : [n.ClassId])
                if (byClass.TryGetValue(feedingClassId, out var t)) t.Lessons++;

            foreach (var (studentId, homeroomId) in participants)
            {
                var has = entryByKey.TryGetValue(
                    (n.ClassId, n.SubjectId, n.Date, n.Period, studentId), out var reasonId);

                Add(total); Add(dateTally);
                if (byClass.TryGetValue(homeroomId, out var classTally)) Add(classTally);
                if (periodTally is not null) Add(periodTally);

                if (reasonId is not null)
                    reasonCounts[reasonId] = reasonCounts.GetValueOrDefault(reasonId) + 1;

                void Add(Tally t)
                {
                    t.Opportunities++;
                    if (!has) { t.Unchecked++; return; }
                    if (reasonId is null) { t.Present++; return; }
                    if (lateIds.Contains(reasonId)) { t.Present++; t.Late++; return; }
                    t.Absent++;
                    if (unexcusedIds.Contains(reasonId)) t.Unexcused++; else t.Excused++;
                }
            }
        }

        var lessonTimes = await db.LessonTimes.AsNoTracking().ToListAsync();
        var timeByPeriod = lessonTimes
            .GroupBy(t => t.Period)
            .ToDictionary(g => g.Key, g => g.First());

        var classRows = classes.Select(c => new AttendanceClassRowDto(
                c.Id, c.Name, c.Grade,
                studentsByClass.TryGetValue(c.Id, out var list) ? list.Count : 0,
                byClass[c.Id].ToDto()))
            .ToList();

        var periodRows = byPeriod.OrderBy(kv => kv.Key)
            .Select(kv => new AttendancePeriodRowDto(
                kv.Key,
                timeByPeriod.TryGetValue(kv.Key, out var lt) ? lt.StartTime : null,
                timeByPeriod.TryGetValue(kv.Key, out var lt2) ? lt2.EndTime : null,
                kv.Value.ToDto()))
            .ToList();

        var trend = byDate.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new AttendanceTrendPointDto(kv.Key, kv.Value.ToDto()))
            .ToList();

        return new AttendanceAnalyticsDto(
            fromDate, toDate, wantClass, dayIso, students.Count, total.ToDto(),
            classRows, periodRows, trend,
            ReasonRows(reasonRows, unexcusedIds, reasonCounts));
    }

    // ------------------------------------------------------------------
    // Yordamchilar
    // ------------------------------------------------------------------

    private static Tally Get<TKey>(Dictionary<TKey, Tally> map, TKey key) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var t)) map[key] = t = new Tally();
        return t;
    }

    /// <summary>
    /// Arxivlanmagan o'quv guruhlarining faol a'zolari: guruh id → (o'quvchi,
    /// uning SINFI). Hisobotga kirgan sinflarda bo'lmagan o'quvchi tashlanadi —
    /// shu tufayli <c>classId</c> filtri guruh darslariga ham o'z-o'zidan tarqaladi.
    ///
    /// <para>
    /// Cut-over o'chirgichi o'chiq bo'lsa — BO'SH lug'at, ya'ni guruh darsi
    /// umuman yo'q va butun hisobot bugungi raqamni beradi (§4.3).
    /// </para>
    /// </summary>
    private static async Task<Dictionary<string, List<(string StudentId, string ClassId)>>> GroupRosterAsync(
        IAppDbContext db, Dictionary<string, string> classIdByStudent)
    {
        var result = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal);
        if (classIdByStudent.Count == 0) return result;
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
            if (!classIdByStudent.TryGetValue(m.StudentId, out var classId)) continue;
            var key = m.GroupId.ToString();
            if (!result.TryGetValue(key, out var list)) result[key] = list = [];
            list.Add((m.StudentId, classId));
        }
        return result;
    }

    /// <summary>
    /// Sababsiz yo'qlik ta'rifi — fayl boshidagi izohga qarang. Bazada bayroq yo'q, shuning
    /// uchun nom yoki maktab bergan manfiy intizomiy ball asos bo'ladi.
    /// </summary>
    private static bool IsUnexcused(AbsenceReason r) =>
        !r.IsLate
        && (r.Name.Contains("sababsiz", StringComparison.OrdinalIgnoreCase) || r.Points < 0);

    private static List<AttendanceReasonRowDto> ReasonRows(
        List<AbsenceReason> reasons,
        HashSet<string> unexcusedIds,
        Dictionary<string, int> counts) =>
        [.. reasons
            .Select(r => new AttendanceReasonRowDto(
                r.Id, r.Name, r.Short, r.IsLate, unexcusedIds.Contains(r.Id),
                counts.GetValueOrDefault(r.Id)))
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];

    private static AttendanceAnalyticsDto Empty(
        string from, string to, string? classId, string day,
        List<AbsenceReason> reasons, HashSet<string> unexcusedIds) =>
        new(from, to, classId, day, 0, new Tally().ToDto(), [], [], [],
            ReasonRows(reasons, unexcusedIds, new Dictionary<string, int>()));

    private static bool IsIso(string? s) =>
        !string.IsNullOrWhiteSpace(s)
        && DateOnly.TryParseExact(s, "yyyy-MM-dd", out _);

    /// <summary>
    /// Davrni tozalaydi: noto'g'ri sana — sukut davri (bugundan orqaga ikki hafta),
    /// teskari berilgan chegaralar almashtiriladi.
    /// </summary>
    private static (string From, string To) NormalizeRange(string? from, string? to)
    {
        var today = AppClock.Today;
        var f = IsIso(from) ? from! : today.AddDays(-DefaultRangeDays).ToString("yyyy-MM-dd");
        var t = IsIso(to) ? to! : today.ToString("yyyy-MM-dd");
        return string.CompareOrdinal(f, t) <= 0 ? (f, t) : (t, f);
    }
}
