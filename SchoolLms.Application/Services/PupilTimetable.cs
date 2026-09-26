using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  O'QUVCHINING HAFTASI — G-5 (docs/modules/students-parity.md §2.1.6).
// ===========================================================================
//
//  Bugun o'quvchining jadvali BITTA manbadan keladi: sinfining haftaga
//  biriktirilgan shabloni, keyin `SubGroup` filtri
//  (`PortalSchedule.LessonsForWeekAsync` + `ForStudent`). Guruh darsi
//  qo'shilganda manba IKKITA bo'ladi — sinf va har bir faol guruh — va bu
//  yig'indini o'quvchi portali, ota-ona Mini App'i, davomat, hisobotlar va
//  turniket (G-14) BIR XIL ko'rishi shart. Shu sababli yig'indi shu yerda,
//  bir marta yasaladi.
//
//  O'CHIRGICH O'CHIQ = BUGUNGI JADVAL
//  ----------------------------------
//  `group_lessons_enabled` o'chiq bo'lsa <see cref="LessonRoster.OwnersOfAsync"/>
//  faqat sinfni qaytaradi, ya'ni natija — bugungi ikki qatorning aynan o'zi.
//  Buni `LessonRosterTests` isbotlaydi.
//
//  BO'LINISH (SubGroup) — FAQAT SINFDA
//  -----------------------------------
//  Guruh darsida bo'linish yo'q (guruhning o'zi allaqachon tanlangan
//  bolalar to'plami), shuning uchun filtr faqat sinf darslariga qo'llanadi.
// ===========================================================================

/// <summary>O'quvchining jadvalidagi bitta dars — egasi bilan birga.</summary>
public sealed record PupilLesson(
    LessonOwner Owner,
    int Day,
    int Period,
    string SubjectId,
    string TeacherId,
    int SubGroup);

/// <summary>
/// "Bu o'quvchining shu haftadagi darslari qaysi?" — yagona javob beruvchi.
/// Fayl boshidagi izohda qoidalar.
/// </summary>
public static class PupilTimetable
{
    /// <summary>
    /// O'quvchining (chorak, hafta) dagi darslari: sinfi + har bir faol guruhi.
    /// Tartib — kun, keyin dars raqami (ekranlar shu tartibda ko'rsatadi).
    /// </summary>
    public static async Task<List<PupilLesson>> ForWeekAsync(
        IAppDbContext db, Student student, int quarter, int week, CancellationToken ct = default)
    {
        var owners = await LessonRoster.OwnersOfAsync(db, student, ct: ct);
        if (owners.Count == 0) return [];

        var ownerIds = owners.Select(o => o.Id).ToList();
        var assignments = await db.WeekAssignments.AsNoTracking()
            .Where(a => ownerIds.Contains(a.ClassId) && a.Quarter == quarter
                        && a.Week == week && a.TemplateId != null)
            .Select(a => new { a.ClassId, a.OwnerKind, a.TemplateId })
            .ToListAsync(ct);
        if (assignments.Count == 0) return [];

        var templateIds = assignments.Select(a => a.TemplateId!).Distinct().ToList();
        var templates = (await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons)
                .Where(t => templateIds.Contains(t.Id)).ToListAsync(ct))
            .ToDictionary(t => t.Id, StringComparer.Ordinal);

        var byId = owners.ToDictionary(o => o.Id, StringComparer.Ordinal);

        // Yo'nalish guruhi SINF O'RNIDA turadi (docs/modules/track-groups-as-classes.md):
        // o'quvchining yo'nalish guruhida shu hafta jadvali bo'lsa, uy sinfining (9-A ...)
        // darslari ko'rsatilmaydi — aks holda eski sinf jadvali bilan ikki marta chiqardi.
        // Yo'nalish jadvali hali kiritilmagan haftada sinf jadvali ko'rinishda qoladi.
        var trackHasWeek = assignments.Any(a =>
            byId.TryGetValue(a.ClassId, out var o) && o.IsGroup && o.IsTrack && o.Kind == a.OwnerKind);

        var result = new List<PupilLesson>();
        foreach (var a in assignments)
        {
            if (!byId.TryGetValue(a.ClassId, out var owner) || owner.Kind != a.OwnerKind) continue;
            if (trackHasWeek && owner.IsClass) continue;
            if (!templates.TryGetValue(a.TemplateId!, out var tpl)) continue;
            foreach (var l in tpl.Lessons)
            {
                // Bo'linish filtri — faqat sinf darsida (fayl boshidagi izoh).
                if (owner.IsClass && l.SubGroup != 0 && l.SubGroup != student.SubGroup) continue;
                result.Add(new PupilLesson(owner, l.Day, l.Period, l.SubjectId, l.TeacherId, l.SubGroup));
            }
        }
        return [.. result.OrderBy(r => r.Day).ThenBy(r => r.Period)];
    }

    /// <summary>
    /// Hafta kuni kesimidagi BIRINCHI va OXIRGI dars raqami: int[6] juftligi
    /// (0 = o'sha kuni dars yo'q). Turniketning kutilgan kelish/ketish vaqti
    /// (G-14) shundan hisoblanadi.
    /// </summary>
    public static (int[] First, int[] Last) Bounds(IEnumerable<PupilLesson> lessons)
    {
        var first = new int[6];
        var last = new int[6];
        foreach (var l in lessons)
        {
            if (l.Day is < 0 or >= 6 || l.Period <= 0) continue;
            if (first[l.Day] == 0 || l.Period < first[l.Day]) first[l.Day] = l.Period;
            if (l.Period > last[l.Day]) last[l.Day] = l.Period;
        }
        return (first, last);
    }

    /// <summary>
    /// Guruh darslarining hafta kuni chegaralari — o'quvchi id'si bo'yicha,
    /// BITTA yurishda (turniket hisoboti minglab o'quvchini ko'radi, sikl
    /// ichida so'rov bo'lishi mumkin emas).
    ///
    /// <para>
    /// SINF darslari bu yerda YO'Q: ular bugungi manbadan (sinf nomi bo'yicha)
    /// olinadi va o'zgarishsiz qoladi. Bu metod faqat USTIGA qo'shiladigan
    /// guruh chegaralarini beradi, ya'ni o'chirgich o'chiq bo'lsa — bo'sh.
    /// </para>
    /// <para>
    /// Manba — har guruhning ASOSIY (eng ko'p darsli) shabloni, chunki
    /// turniket hisoboti oraliq bo'yicha ishlaydi va haftama-hafta yurmaydi:
    /// sinf chegaralari bugun aynan shunday hisoblanadi
    /// (<c>TurnstileAnalyticsQueries.ClassBoundsAsync</c>).
    /// </para>
    /// </summary>
    public static async Task<Dictionary<string, (int[] First, int[] Last)>> GroupBoundsByStudentAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var result = new Dictionary<string, (int[] First, int[] Last)>(StringComparer.Ordinal);
        var byStudent = await LessonRoster.GroupOwnersByStudentAsync(db, ct);
        if (byStudent.Count == 0) return result;

        var mains = (await TeacherLessons.MainTemplatesAsync(db, ct))
            .Where(x => x.Owner.IsGroup)
            .ToDictionary(x => x.Owner.Id, x => x.Template, StringComparer.Ordinal);
        if (mains.Count == 0) return result;

        foreach (var (studentId, owners) in byStudent)
        {
            var first = new int[6];
            var last = new int[6];
            var any = false;
            foreach (var owner in owners)
            {
                if (!mains.TryGetValue(owner.Id, out var tpl)) continue;
                foreach (var l in tpl.Lessons)
                {
                    if (l.Day is < 0 or >= 6 || l.Period <= 0) continue;
                    any = true;
                    if (first[l.Day] == 0 || l.Period < first[l.Day]) first[l.Day] = l.Period;
                    if (l.Period > last[l.Day]) last[l.Day] = l.Period;
                }
            }
            if (any) result[studentId] = (first, last);
        }
        return result;
    }
}
