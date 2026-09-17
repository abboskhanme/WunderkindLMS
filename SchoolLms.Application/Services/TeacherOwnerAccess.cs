using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  O'QITUVCHI QAYSI EGAGA YETADI — G-12 (docs/modules/students-parity.md §2.1.6).
// ===========================================================================
//
//  MUAMMO
//  ------
//  "Bu o'qituvchi shu sinfda shu fanni o'qitadimi?" degan savolga bugun UCHTA
//  joyda MUSTAQIL javob beriladi va uchalasi ham jadval shablonlaridan
//  hisoblanadi (o'qituvchi↔sinf↔fan jadvali yo'q):
//
//    · `TeacherPortalController.Teaches` / `TeachesClass`
//    · `TelegramTeacherController.TeachesAsync`
//    · `ChatService.ClassNamesForUserAsync` (kanal ro'yxati)
//
//  Guruh darsi qo'shilganda uchalasi ham alohida tuzatilishi kerak bo'lardi,
//  va bittasi unutilsa — yo teshik ochilardi, yo guruh o'qituvchisi o'z
//  jurnaliga kira olmay qolardi. Shuning uchun qoida shu yerda, BIR MARTA.
//
//  BUGUNGI XATTI-HARAKAT — BAYT-BA-BAYT
//  ------------------------------------
//  Sinf egasi uchun bu fayl bugungi so'rovning AYNAN o'zini bajaradi:
//    · `Teaches`      — shu sinfning shablonlarida o'qituvchining shu fandan
//                       darsi bormi (sinf mavjudligi TEKSHIRILMAYDI — bugungi
//                       kod ham tekshirmaydi, "yetim" shablon o'tib ketadi);
//    · `Reaches`      — sinf rahbari bo'lsa YOKI shu sinfda darsi bo'lsa.
//  `owner_kind` filtri sinf egasi uchun hech narsani o'zgartirmaydi: guruh
//  shabloni `class_id` ustunida GURUH id'sini saqlaydi, u esa sinf id'si bilan
//  mos kela olmaydi.
//
//  O'CHIRGICH O'CHIQ = BUGUNGI JAVOB
//  ---------------------------------
//  `school_meta.group_lessons_enabled` o'chiq ekan guruh egasi UMUMAN yo'q deb
//  qaraladi: guruh shabloni qoralama bo'lib qoladi, guruh o'qituvchisi esa
//  bugungidek 403 oladi. Ya'ni cut-over'gacha bu fayl hech kimga bir qadam ham
//  ortiq ruxsat bermaydi.
//
//  GURUH EGASIDA "SINF RAHBARI" NIMA
//  ---------------------------------
//  Sinfda ikkita yo'l bor: dars berish va SINF RAHBARLIGI (rahbar ro'yxatni
//  ko'radi, jurnal kataklariga esa yetmaydi — bu bugungi nomutanosiblik
//  `TeacherAccessTests` da qadab qo'yilgan). Guruhning aynan shunday ikkinchi
//  yo'li — `study_group_teachers`: guruh biriktirilgan o'qituvchi ro'yxatni
//  ko'radi, jurnalga esa faqat jadvalda o'z darsi bo'lsa yetadi.
// ===========================================================================

/// <summary>
/// O'qituvchi yetadigan bitta ega (sinf yoki guruh) — u yerda o'qitadigan
/// fanlari bilan.
/// </summary>
/// <param name="Owner">Sinf yoki o'quv guruhi (<see cref="LessonOwner"/>).</param>
/// <param name="IsHomeroom">
/// Sinf uchun — sinf rahbarimi; guruh uchun — guruhga biriktirilgan
/// o'qituvchimi (<c>study_group_teachers</c>).
/// </param>
/// <param name="SubjectIds">Shu egada o'qituvchi dars beradigan fanlar.</param>
public sealed record TeacherOwner(LessonOwner Owner, bool IsHomeroom, List<string> SubjectIds);

/// <summary>
/// "Bu o'qituvchi shu egaga (sinf yoki guruh) yetadimi?" — yagona javob
/// beruvchi. Fayl boshidagi izohda nega kerakligi va nima kafolatlanishi
/// yozilgan.
/// </summary>
public static class TeacherOwnerAccess
{
    /// <summary>
    /// Jurnal KATAGIGA yetish sharti: shu eganing jadvalida o'qituvchining shu
    /// fandan darsi bormi.
    ///
    /// <para>
    /// Guruh egasi uchun qo'shimcha shart — cut-over o'chirgichi yoqilgan
    /// bo'lishi. O'chiq ekan javob bugungidek <c>false</c>.
    /// </para>
    /// </summary>
    public static async Task<bool> TeachesAsync(
        IAppDbContext db, string teacherId, string ownerId, string subjectId,
        CancellationToken ct = default)
    {
        var owner = await LessonRoster.OwnerAsync(db, ownerId, ct);
        if (owner is not null && owner.IsGroup && !await LessonRoster.GroupLessonsEnabledAsync(db, ct))
            return false;

        return await HasLessonAsync(db, teacherId, ownerId, owner?.Kind, subjectId, ct);
    }

    /// <summary>
    /// Ro'yxat darajasidagi shart: o'qituvchi shu eganing o'quvchilarini
    /// ko'ra oladimi. Sinfda — rahbarlik yoki dars; guruhda — biriktirilganlik
    /// yoki dars.
    /// </summary>
    public static async Task<bool> ReachesAsync(
        IAppDbContext db, Teacher teacher, string ownerId, CancellationToken ct = default)
    {
        var owner = await LessonRoster.OwnerAsync(db, ownerId, ct);
        if (owner is null) return false;

        if (owner.IsGroup)
        {
            if (!await LessonRoster.GroupLessonsEnabledAsync(db, ct)) return false;
            if (await IsGroupTeacherAsync(db, teacher.Id, owner, ct)) return true;
        }
        else if (!string.IsNullOrEmpty(teacher.HomeroomClass) && teacher.HomeroomClass == owner.Name)
        {
            return true;
        }

        return await HasLessonAsync(db, teacher.Id, ownerId, owner.Kind, subjectId: null, ct);
    }

    /// <summary>
    /// O'qituvchi yetadigan HAMMA ega — sinflar (rahbarlik yoki dars) va
    /// guruhlar (biriktirilganlik yoki dars). Tartib: avval sinf darajasi va
    /// nomi, keyin guruhlar nomi bo'yicha.
    ///
    /// <para>
    /// O'chirgich o'chiq bo'lsa ro'yxatda guruh YO'Q — ya'ni bugungi ro'yxat.
    /// </para>
    /// </summary>
    public static async Task<List<TeacherOwner>> OwnersAsync(
        IAppDbContext db, Teacher teacher, CancellationToken ct = default)
    {
        var templates = await db.ScheduleTemplates.AsNoTracking().Include(x => x.Lessons).ToListAsync(ct);

        // ownerId -> shu egada o'qituvchi o'qitadigan fanlar.
        var taught = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var tpl in templates)
            foreach (var l in tpl.Lessons.Where(l => l.TeacherId == teacher.Id))
            {
                var key = OwnerKey(tpl.ClassId, tpl.OwnerKind);
                if (!taught.TryGetValue(key, out var set)) taught[key] = set = new(StringComparer.Ordinal);
                set.Add(l.SubjectId);
            }

        var result = new List<TeacherOwner>();

        var classes = await db.Classes.AsNoTracking()
            .OrderBy(c => c.Grade).ThenBy(c => c.Name).ToListAsync(ct);
        foreach (var cls in classes)
        {
            var isHomeroom = !string.IsNullOrEmpty(teacher.HomeroomClass) && teacher.HomeroomClass == cls.Name;
            taught.TryGetValue(OwnerKey(cls.Id, LessonOwnerKind.Class), out var subjIds);
            if (!isHomeroom && (subjIds is null || subjIds.Count == 0)) continue;
            result.Add(new TeacherOwner(
                new LessonOwner(LessonOwnerKind.Class, cls.Id, cls.Name),
                isHomeroom,
                [.. subjIds ?? []]));
        }

        if (!await LessonRoster.GroupLessonsEnabledAsync(db, ct)) return result;

        var myGroupIds = (await db.StudyGroupTeachers.AsNoTracking()
            .Where(g => g.TeacherId == teacher.Id).Select(g => g.GroupId).ToListAsync(ct)).ToHashSet();

        var groups = await db.StudyGroups.AsNoTracking()
            .Where(g => !g.IsArchived).OrderBy(g => g.Name).ToListAsync(ct);
        foreach (var grp in groups)
        {
            var id = grp.Id.ToString();
            var attached = myGroupIds.Contains(grp.Id);
            taught.TryGetValue(OwnerKey(id, LessonOwnerKind.Group), out var subjIds);
            if (!attached && (subjIds is null || subjIds.Count == 0)) continue;
            result.Add(new TeacherOwner(
                new LessonOwner(LessonOwnerKind.Group, id, grp.Name, grp.SubjectId),
                attached,
                // Guruhda fan BITTA — jadvalda darsi bo'lmasa ham guruhning fani
                // ko'rsatiladi, aks holda biriktirilgan o'qituvchi bo'sh ro'yxat ko'rardi.
                [.. subjIds ?? [grp.SubjectId]]));
        }

        return result;
    }

    /// <summary>
    /// O'qituvchining (ega, fan) juftliklari — "so'nggi yozuvlar" kabi ro'yxatlar
    /// uchun. O'chirgich o'chiq bo'lsa guruh juftliklari CHIQMAYDI (guruh
    /// shabloni qoralama bo'lib qoladi).
    /// </summary>
    public static async Task<HashSet<(string OwnerId, string OwnerKind, string SubjectId)>> PairsAsync(
        IAppDbContext db, string teacherId, CancellationToken ct = default)
    {
        var groupsOn = await LessonRoster.GroupLessonsEnabledAsync(db, ct);
        var templates = await db.ScheduleTemplates.AsNoTracking().Include(x => x.Lessons).ToListAsync(ct);

        return [.. templates
            .Where(tpl => groupsOn || tpl.OwnerKind != LessonOwnerKind.Group)
            .SelectMany(tpl => tpl.Lessons
                .Where(l => l.TeacherId == teacherId)
                .Select(l => (tpl.ClassId, tpl.OwnerKind, l.SubjectId)))];
    }

    /// <summary>
    /// O'qituvchining shu egadagi, shu fandagi, shu kun va dars raqamidagi
    /// darsining BO'LINISHI (0/1/2).
    ///
    /// <para>
    /// Guruhda bo'linish YO'Q — javob har doim 0 (§2.1.4: guruhning o'zi
    /// allaqachon tanlangan bolalar to'plami).
    /// </para>
    /// </summary>
    public static async Task<int> SubGroupAsync(
        IAppDbContext db, string teacherId, LessonOwner owner, string subjectId,
        int dayIndex, int period, CancellationToken ct = default)
    {
        if (owner.IsGroup) return 0;

        var groups = await db.ScheduleTemplates.AsNoTracking()
            .Where(t => t.ClassId == owner.Id && t.OwnerKind == owner.Kind)
            .SelectMany(t => t.Lessons)
            .Where(l => l.TeacherId == teacherId && l.SubjectId == subjectId
                        && l.Day == dayIndex && l.Period == period)
            .Select(l => l.SubGroup)
            .Distinct()
            .ToListAsync(ct);
        return groups.Count == 1 ? groups[0] : 0;
    }

    /// <summary>Guruh o'qituvchisi ro'yxatida bormi (<c>study_group_teachers</c>).</summary>
    private static async Task<bool> IsGroupTeacherAsync(
        IAppDbContext db, string teacherId, LessonOwner owner, CancellationToken ct)
    {
        if (!Guid.TryParse(owner.Id, out var groupId)) return false;
        return await db.StudyGroupTeachers.AsNoTracking()
            .AnyAsync(g => g.GroupId == groupId && g.TeacherId == teacherId, ct);
    }

    /// <summary>
    /// Bugungi so'rovning o'zi: shu eganing shablonlarida o'qituvchining darsi
    /// bormi. <paramref name="subjectId"/> null bo'lsa — fandan qat'i nazar.
    ///
    /// <para>
    /// <paramref name="ownerKind"/> null (ega topilmadi, "yetim" id) bo'lsa
    /// tur bo'yicha filtr QO'YILMAYDI — bugungi kod ham qo'ymaydi.
    /// </para>
    /// </summary>
    private static async Task<bool> HasLessonAsync(
        IAppDbContext db, string teacherId, string ownerId, string? ownerKind, string? subjectId,
        CancellationToken ct)
    {
        var query = db.ScheduleTemplates.AsNoTracking().Where(t => t.ClassId == ownerId);
        if (ownerKind is not null) query = query.Where(t => t.OwnerKind == ownerKind);

        return await query
            .SelectMany(t => t.Lessons)
            .AnyAsync(l => l.TeacherId == teacherId && (subjectId == null || l.SubjectId == subjectId), ct);
    }

    private static string OwnerKey(string ownerId, string ownerKind) => ownerKind + " " + ownerId;
}
