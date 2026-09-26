using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  O'QUVCHI ZIDDIYATI — G-11 (docs/modules/students-parity.md §2.1.4, §2.1.6).
// ===========================================================================
//
//  NEGA BUGUNGACHA KERAK BO'LMAGAN
//  -------------------------------
//  Bugun bola BITTA sinfda o'qiydi. Sinfning jadvalida esa bir (kun, dars)
//  katagida bitta dars turadi. Ya'ni bolani ikki joyga birdan yozib qo'yish
//  MUMKIN EMAS edi — tekshiruv ham yozilmagan (§2.1.3: "no pupil-conflict
//  check exists at all").
//
//  GURUH BUNI BUZADI
//  -----------------
//  Guruh — sinfdan YUQORIDA turadigan ikkinchi to'plam. 5-A ning seshanba
//  3-darsida matematika bo'lsa va o'sha bola seshanba 3-darsda ingliz tili
//  guruhida bo'lsa — u ikki joyda birdan. Auditning "genuinely hard part"
//  deb atagani aynan shu.
//
//  QOIDA (§2.1.4 jadvali, Q2)
//  --------------------------
//  "Bola bir (kun, dars) da ham sinf darsida, ham guruh darsida bo'la
//  olmaydi." Tekshiruv HAFTAGA BIRIKTIRILGAN shablonlarga qarshi yuritiladi:
//  biriktirilmagan shablon hali hech kimning jadvalida emas, uni tahrirlashga
//  to'sqinlik qilish tahrirni imkonsiz qilib qo'yardi (shablon avval
//  to'ldiriladi, keyin biriktiriladi).
//
//  RAD ETISH — 409 VA ISMLAR
//  -------------------------
//  "Ziddiyat bor" degan xabar foydalanuvchiga hech narsa bermaydi. Kim,
//  qayerda — shuni aytamiz (<see cref="Message"/>).
//
//  O'CHIRGICH O'CHIQ = HECH NARSA RAD ETILMAYDI
//  --------------------------------------------
//  `group_lessons_enabled` o'chiq bo'lsa guruh darsi umuman yo'q, bola esa
//  bitta sinfda — ziddiyat MATEMATIK jihatdan mumkin emas. Shuning uchun
//  tekshiruv darhol bo'sh qaytadi: bugungi har bir saqlash bugungidek
//  o'tadi va har katak saqlashda ortiqcha so'rov yubormaymiz.
//
//  YO'NALISH GURUHLARI (2026-09-26): ular o'chirgichga qaramaydi. Faol
//  yo'nalish guruhi bor ekan tekshiruv ishlaydi — sinf (masalan 9-A) bilan
//  yo'nalish guruhi orasidagi o'quvchi/o'qituvchi to'qnashuvi ham ziddiyat.
// ===========================================================================

/// <summary>
/// Bitta ziddiyat: falon kun/dars katagida falon egadagi dars bilan to'qnashdi,
/// to'qnashgan bolalar — <paramref name="StudentNames"/>.
/// </summary>
public sealed record LessonConflict(
    int Day,
    int Period,
    LessonOwner Other,
    string OtherTemplateName,
    IReadOnlyList<string> StudentNames);

/// <summary>
/// "Bu darsni saqlasak, birorta bola ikki joyda qolib ketadimi?" — G-11 ning
/// eng nozik qismi. Fayl boshidagi izohda qoida va nega u bugungacha
/// kerak bo'lmagani yozilgan.
/// </summary>
public static class ScheduleConflicts
{
    private const int MaxNamesInMessage = 10;

    /// <summary>Hafta kunlari — xabarda ko'rinadi.</summary>
    private static readonly string[] WeekDays =
        ["Dushanba", "Seshanba", "Chorshanba", "Payshanba", "Juma", "Shanba"];

    /// <summary>
    /// SHABLON katagini saqlashdan oldingi tekshiruv. Shablon qaysi
    /// (chorak, hafta) larga biriktirilgan bo'lsa, o'sha haftalardagi BOSHQA
    /// egalarning darslari bilan solishtiriladi.
    /// </summary>
    /// <param name="cells">Saqlanayotgan kataklar: (kun, dars, bo'linish).</param>
    public static async Task<List<LessonConflict>> ForTemplateAsync(
        IAppDbContext db, LessonOwner owner, string templateId,
        IReadOnlyList<(int Day, int Period, int SubGroup)> cells, CancellationToken ct = default)
    {
        if (cells.Count == 0) return [];
        // Guruh darsi umuman tirik bo'lmasa (o'chirgich o'chiq va yo'nalish guruhi yo'q) —
        // ziddiyat mumkin emas. Yo'nalish guruhi o'chirgichga qaramaydi.
        if (!(await LessonRoster.GroupScopeAsync(db, ct)).AnyGroup) return [];

        var slots = await db.WeekAssignments.AsNoTracking()
            .Where(a => a.TemplateId == templateId && a.ClassId == owner.Id
                        && a.OwnerKind == owner.Kind)
            .Select(a => new { a.Quarter, a.Week })
            .Distinct().ToListAsync(ct);
        if (slots.Count == 0) return [];

        return await CheckAsync(db, owner, [.. slots.Select(s => (s.Quarter, s.Week, cells))], ct);
    }

    /// <summary>
    /// HAFTAGA BIRIKTIRISHdan oldingi tekshiruv: taklif qilingan
    /// (hafta → shablon) juftliklari boshqa egalarning o'sha haftadagi
    /// darslari bilan to'qnashmaydimi.
    /// </summary>
    public static async Task<List<LessonConflict>> ForAssignmentsAsync(
        IAppDbContext db, LessonOwner owner, int quarter,
        IReadOnlyList<(int Week, string? TemplateId)> assignments, CancellationToken ct = default)
    {
        // Guruh darsi umuman tirik bo'lmasa (o'chirgich o'chiq va yo'nalish guruhi yo'q) —
        // ziddiyat mumkin emas. Yo'nalish guruhi o'chirgichga qaramaydi.
        if (!(await LessonRoster.GroupScopeAsync(db, ct)).AnyGroup) return [];

        var wanted = assignments.Where(a => !string.IsNullOrEmpty(a.TemplateId)).ToList();
        if (wanted.Count == 0) return [];

        var templateIds = wanted.Select(a => a.TemplateId!).Distinct().ToList();
        var cellsByTemplate = (await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons)
                .Where(t => templateIds.Contains(t.Id)).ToListAsync(ct))
            .ToDictionary(
                t => t.Id,
                t => (IReadOnlyList<(int, int, int)>)[.. t.Lessons.Select(l => (l.Day, l.Period, l.SubGroup))],
                StringComparer.Ordinal);

        var work = new List<(int Quarter, int Week, IReadOnlyList<(int Day, int Period, int SubGroup)> Cells)>();
        foreach (var a in wanted)
            if (cellsByTemplate.TryGetValue(a.TemplateId!, out var cells) && cells.Count > 0)
                work.Add((quarter, a.Week, cells));

        return await CheckAsync(db, owner, work, ct);
    }

    /// <summary>
    /// Ziddiyatlarni bitta o'zbekcha xabarga yig'adi — 409 javobining matni.
    /// Ismlar <see cref="MaxNamesInMessage"/> tadan ko'p bo'lsa qisqartiriladi
    /// (xabar o'qilmaydigan bo'lib ketmasin).
    /// </summary>
    public static string Message(IReadOnlyList<LessonConflict> conflicts)
    {
        var parts = new List<string>();
        foreach (var c in conflicts.Take(5))
        {
            var day = c.Day is >= 0 and < 6 ? WeekDays[c.Day] : $"{c.Day}-kun";
            var names = c.StudentNames.Take(MaxNamesInMessage).ToList();
            var tail = c.StudentNames.Count > names.Count
                ? $" va yana {c.StudentNames.Count - names.Count} ta o'quvchi"
                : "";
            parts.Add($"{day}, {c.Period}-dars — \"{c.Other.Name}\" ({c.OtherTemplateName}): "
                      + string.Join(", ", names) + tail);
        }
        var more = conflicts.Count > 5 ? $" (va yana {conflicts.Count - 5} ta to'qnashuv)" : "";
        return "Bu darsni saqlab bo'lmaydi — quyidagi o'quvchilar shu soatda boshqa darsda: "
               + string.Join("; ", parts) + more + ".";
    }

    // =====================================================================
    //  Ichki ish
    // =====================================================================

    private static async Task<List<LessonConflict>> CheckAsync(
        IAppDbContext db, LessonOwner owner,
        IReadOnlyList<(int Quarter, int Week, IReadOnlyList<(int Day, int Period, int SubGroup)> Cells)> work,
        CancellationToken ct)
    {
        if (work.Count == 0) return [];

        var quarters = work.Select(w => w.Quarter).Distinct().ToList();
        var weeks = work.Select(w => w.Week).Distinct().ToList();

        // O'sha (chorak, hafta) larda BOSHQA egalarning biriktirishlari.
        var others = await db.WeekAssignments.AsNoTracking()
            .Where(a => quarters.Contains(a.Quarter) && weeks.Contains(a.Week)
                        && a.TemplateId != null && a.ClassId != owner.Id)
            .Select(a => new { a.Quarter, a.Week, a.ClassId, a.OwnerKind, a.TemplateId })
            .ToListAsync(ct);
        if (others.Count == 0) return [];

        var owners = await LessonRoster.LiveOwnersAsync(db, ct);
        var otherTemplateIds = others.Select(a => a.TemplateId!).Distinct().ToList();
        var otherTemplates = (await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons)
                .Where(t => otherTemplateIds.Contains(t.Id)).ToListAsync(ct))
            .ToDictionary(t => t.Id, StringComparer.Ordinal);

        var rosters = new Dictionary<string, List<Student>>(StringComparer.Ordinal);
        var conflicts = new List<LessonConflict>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (quarter, week, cells) in work)
        {
            foreach (var other in others.Where(a => a.Quarter == quarter && a.Week == week))
            {
                if (!owners.TryGetValue(other.ClassId, out var otherOwner)
                    || otherOwner.Kind != other.OwnerKind) continue;
                if (!otherTemplates.TryGetValue(other.TemplateId!, out var otherTpl)) continue;

                foreach (var (day, period, subGroup) in cells)
                {
                    foreach (var l in otherTpl.Lessons.Where(l => l.Day == day && l.Period == period))
                    {
                        var mine = await RosterAsync(db, rosters, owner, subGroup, ct);
                        if (mine.Count == 0) continue;
                        var theirs = await RosterAsync(db, rosters, otherOwner, l.SubGroup, ct);
                        if (theirs.Count == 0) continue;

                        var theirIds = theirs.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
                        var clashing = mine.Where(s => theirIds.Contains(s.Id))
                            .Select(s => s.FullName).ToList();
                        if (clashing.Count == 0) continue;

                        // Bir xil (kun, dars, ega) bir necha haftada takrorlanadi —
                        // xabarda bir marta ko'rinsin.
                        if (!seen.Add($"{day}|{period}|{otherOwner.Id}|{l.SubGroup}")) continue;
                        conflicts.Add(new LessonConflict(day, period, otherOwner, otherTpl.Name, clashing));
                    }
                }
            }
        }

        return [.. conflicts.OrderBy(c => c.Day).ThenBy(c => c.Period)];
    }

    private static async Task<List<Student>> RosterAsync(
        IAppDbContext db, Dictionary<string, List<Student>> cache,
        LessonOwner owner, int subGroup, CancellationToken ct)
    {
        var key = owner.Id + "|" + subGroup;
        if (cache.TryGetValue(key, out var cached)) return cached;
        var list = await LessonRoster.ForLessonAsync(db, owner, subGroup, ct: ct);
        cache[key] = list;
        return list;
    }
}
