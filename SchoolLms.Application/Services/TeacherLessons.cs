using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  O'QITUVCHI DARSLARI — G-5 (docs/modules/students-parity.md §2.1.6).
// ===========================================================================
//
//  NEGA ALOHIDA FAYL
//  -----------------
//  "O'qituvchi haftada nechta dars beradi?" degan savolga bugun ikki joy
//  BIR-BIRIDAN MUSTAQIL javob beradi:
//    · `TeacherSalaryCalc.LessonsByWeekdayAsync` — maosh uchun;
//    · `TurnstileService.FirstPeriodByWeekdayAsync` — kutilgan kelish uchun.
//  Ikkalasi ham "har sinfning eng ko'p darsli shabloni" qoidasini alohida
//  yozgan. Guruh darsi qo'shilganda bitta tuzatilib, ikkinchisi unutilsa —
//  o'qituvchi maoshi bilan davomati bir-biriga mos kelmay qolardi.
//
//  MAOSHDAGI JIM TUZOQ (§2.1.5, G-16)
//  ----------------------------------
//  Bugungi filtr: `classSet.Contains(t.ClassId)` — ya'ni egasi MAVJUD,
//  arxivlanmagan SINF bo'lmagan har qanday shablon JIMGINA tashlab
//  yuboriladi. Guruh shabloni aynan shunday: uning `class_id` da guruh id'si
//  turadi, sinflar ro'yxatida esa u yo'q. Natijada guruh darslari maoshdan
//  butunlay tushib qolardi — xato emas, XABARSIZ KAMAYISH.
//
//  VA NEGA "BIR MARTA"
//  -------------------
//  Guruhni bir nechta sinf boqishi mumkin. Agar guruh darsi har boquvchi
//  sinfning jadvaliga NUSXA qilib qo'yilsa, maosh N marta hisoblanardi.
//  Shu sababli guruh — <see cref="LessonRoster.LiveOwnersAsync"/> da BITTA
//  ega, va uning "asosiy" shabloni ham BITTA (quyida
//  <see cref="MainTemplatesAsync"/>).
//
//  O'CHIRGICH O'CHIQ = BUGUNGI RAQAM
//  ---------------------------------
//  `group_lessons_enabled` o'chiq ekan tirik egalar ro'yxatida sinflar va
//  YO'NALISH guruhlari bo'ladi (yo'nalish guruhi o'chirgichga qaramaydi,
//  docs/modules/track-groups-as-classes.md); oddiy guruhlar — yo'q.
// ===========================================================================

/// <summary>Egasi bilan birga bitta dars katagi (jadval satri).</summary>
public sealed record OwnedLesson(
    LessonOwner Owner,
    string TemplateId,
    string TemplateName,
    int Day,
    int Period,
    string SubjectId,
    string TeacherId,
    int SubGroup);

/// <summary>
/// O'qituvchining darslari — sinf VA guruh jadvallari bo'yicha. Fayl
/// boshidagi izohda nega kerakligi yozilgan.
/// </summary>
public static class TeacherLessons
{
    /// <summary>
    /// Har TIRIK eganing "asosiy" (eng ko'p darsli, tenglikda eng kichik id'li)
    /// jadval shabloni. Bugungi qoidaning aynan o'zi, faqat endi ega sinf ham,
    /// guruh ham bo'lishi mumkin.
    ///
    /// <para>
    /// <c>owner_kind</c> ham solishtiriladi: id tasodifan mos kelib qolsa ham
    /// sinf shabloni guruh egasiga (yoki aksincha) yopishib qolmaydi.
    /// </para>
    /// </summary>
    public static async Task<List<(LessonOwner Owner, ScheduleTemplate Template)>> MainTemplatesAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var owners = await LessonRoster.LiveOwnersAsync(db, ct);
        var templates = await db.ScheduleTemplates.Include(t => t.Lessons).ToListAsync(ct);

        return [.. templates
            .Where(t => owners.TryGetValue(t.ClassId, out var o) && o.Kind == t.OwnerKind)
            .GroupBy(t => t.ClassId, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(t => t.Lessons.Count).ThenBy(t => t.Id, StringComparer.Ordinal).First())
            .Select(t => (owners[t.ClassId], t))];
    }

    /// <summary>Asosiy shablonlardagi barcha darslar, egasi bilan birga.</summary>
    public static async Task<List<OwnedLesson>> MainLessonsAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var result = new List<OwnedLesson>();
        foreach (var (owner, tpl) in await MainTemplatesAsync(db, ct))
            foreach (var l in tpl.Lessons)
                result.Add(new OwnedLesson(
                    owner, tpl.Id, tpl.Name, l.Day, l.Period, l.SubjectId, l.TeacherId, l.SubGroup));
        return result;
    }

    /// <summary>
    /// teacherId → int[6]: hafta kuni kesimidagi darslar soni (0=Dushanba …
    /// 5=Shanba). Maosh (G-16) va turniket (G-14) uchun YAGONA manba.
    /// </summary>
    public static async Task<Dictionary<string, int[]>> ByWeekdayAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var result = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var l in await MainLessonsAsync(db, ct))
        {
            if (string.IsNullOrEmpty(l.TeacherId) || l.Day is < 0 or >= 6) continue;
            if (!result.TryGetValue(l.TeacherId, out var arr)) result[l.TeacherId] = arr = new int[6];
            arr[l.Day]++;
        }
        return result;
    }

    /// <summary>
    /// teacherId → int[6]: har hafta kunidagi ENG ERTA dars raqami (0 = dars
    /// yo'q). Turniketning kutilgan kelish vaqti shundan hisoblanadi.
    /// </summary>
    public static async Task<Dictionary<string, int[]>> FirstPeriodByWeekdayAsync(
        IAppDbContext db, CancellationToken ct = default)
    {
        var result = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var l in await MainLessonsAsync(db, ct))
        {
            if (string.IsNullOrEmpty(l.TeacherId) || l.Day is < 0 or >= 6 || l.Period <= 0) continue;
            if (!result.TryGetValue(l.TeacherId, out var arr)) result[l.TeacherId] = arr = new int[6];
            if (arr[l.Day] == 0 || l.Period < arr[l.Day]) arr[l.Day] = l.Period;
        }
        return result;
    }

    /// <summary>
    /// HAFTAGA BIRIKTIRILGAN darslar (chorak, hafta) — sinf va guruh birga.
    /// Portal jadvali (<see cref="PortalSchedule.TeacherWeekAsync"/>) shundan
    /// oziqlanadi.
    ///
    /// <para>
    /// <paramref name="teacherId"/> null bo'lsa — hamma o'qituvchi (ziddiyat
    /// tekshiruvi shu ko'rinishdan foydalanadi).
    /// </para>
    /// </summary>
    public static async Task<List<OwnedLesson>> ForWeekAsync(
        IAppDbContext db, string? teacherId, int quarter, int week, CancellationToken ct = default)
    {
        var assignments = await db.WeekAssignments.AsNoTracking()
            .Where(x => x.Quarter == quarter && x.Week == week && x.TemplateId != null)
            .Select(x => new { x.ClassId, x.OwnerKind, x.TemplateId })
            .ToListAsync(ct);
        if (assignments.Count == 0) return [];

        // Arxivlangan sinfning darsi ham nomi bilan ko'rinadi — bugungi
        // `PortalSchedule.TeacherWeekAsync` shunday qiladi (u `db.Classes` ni
        // arxiv filtrisiz o'qiydi) va biz uni o'zgartirmaymiz.
        var owners = await LessonRoster.AllOwnersAsync(db, ct);
        // Yo'nalish guruhi o'chirgichga qaramaydi; oddiy guruh — faqat yoqilganda.
        var scope = await LessonRoster.GroupScopeAsync(db, ct);
        var templateIds = assignments.Select(a => a.TemplateId!).Distinct().ToList();
        var templates = (await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons)
                .Where(t => templateIds.Contains(t.Id)).ToListAsync(ct))
            .ToDictionary(t => t.Id, StringComparer.Ordinal);

        var result = new List<OwnedLesson>();
        foreach (var a in assignments)
        {
            // O'chirgich o'chiq — guruh biriktirishi (bo'lsa ham) KO'RINMAYDI.
            if (!scope.Allows(a.OwnerKind, a.ClassId)) continue;
            // Egasi topilmagan "yetim" biriktirish: bugungi kod uni tashlamaydi,
            // shunchaki nomsiz ko'rsatadi — aynan shuni takrorlaymiz.
            if (!owners.TryGetValue(a.ClassId, out var owner) || owner.Kind != a.OwnerKind)
                owner = new LessonOwner(a.OwnerKind, a.ClassId, "");
            if (!templates.TryGetValue(a.TemplateId!, out var tpl)) continue;
            foreach (var l in tpl.Lessons)
            {
                if (teacherId is not null && l.TeacherId != teacherId) continue;
                result.Add(new OwnedLesson(
                    owner, tpl.Id, tpl.Name, l.Day, l.Period, l.SubjectId, l.TeacherId, l.SubGroup));
            }
        }
        return result;
    }
}
