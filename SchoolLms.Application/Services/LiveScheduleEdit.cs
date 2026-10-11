using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  FAOL JADVALGA O'ZGARTIRISH KIRITISH (2026-10-11)
// ===========================================================================
//
//  Mijoz: "Dars jadvaliga o'zgartirish kiritish ... muharrir noqulay". Ilgari faol jadval
//  (haftalarga biriktirilgan shablon) umumiy jadvalda QULF edi: bitta darsni almashtirish uchun
//  butun jadvalni yangi qoralamaga nusxalab, tahrirlab, keyin qayta nashr qilish kerak edi.
//  Sabab — shablonni joyida o'zgartirish O'TGAN haftalarni ham o'zgartirardi (jurnal, davomat,
//  maosh o'sha haftalarning jadvali bo'yicha hisoblanadi).
//
//  ENDI: katak to'g'ridan-to'g'ri tahrirlanadi, tarix esa avtomatik saqlanadi:
//    · shablon joriy haftadan OLDINGI birorta haftaga biriktirilmagan bo'lsa — joyida o'zgaradi;
//    · biriktirilgan bo'lsa — shablonning NUSXASI olinadi, joriy va keyingi haftalar nusxaga
//      o'tkaziladi, eski shablon "… · dd.MM.yyyy gacha" deb qayta nomlanadi va o'tgan haftalarda
//      qoladi; o'zgarish nusxaga yoziladi. Nusxa eski nomni oladi — ekranlar uni avvalgidek topadi;
//    · faqat o'tgan haftalarda ishlatilgan (eski versiya) shablon o'zgartirilmaydi.
//
//  CHEGARA — joriy KALENDAR haftasining dushanbasi, YAKSHANBA kuni esa ertangi dushanba (o'tib
//  bo'lgan Du–Sha qayta yozilmasin). Sanalar bo'yicha, chorak raqami bo'yicha emas: ta'til paytida
//  ham o'tgan chorak haftalari "o'tgan" bo'lib qoladi. Hafta bo'linmaydi — o'zgarish joriy
//  haftaning hammasiga tegishli ("Nashr qilish" bilan bir xil qoida).
//
//  ASOSIY SHABLON (maosh, turniket, MCP — <see cref="WithoutHistory"/>): egada joriy yoki keyingi
//  haftalarga biriktirilgan shablon bor ekan, FAQAT o'tgan haftalarda ishlatilganlari (eski versiya
//  "… gacha") tanlovga kirmaydi — aks holda darslar soni bo'yicha eski versiya "yutib" ketardi.
//  Qolgan hamma holatda qoida avvalgidek: eng ko'p darsli (TeacherSalaryCalcTests qotirgan qoida).
// ===========================================================================

/// <summary>Faol jadval katagini tahrirlashdan oldingi tayyorlov natijasi.</summary>
/// <param name="Template">O'zgarish yoziladigan shablon (o'zi yoki yangi nusxasi).</param>
/// <param name="Archived">Nusxa olingan bo'lsa — o'tgan haftalarda qolgan eski shablon.</param>
/// <param name="Error">Tahrirlab bo'lmasa — foydalanuvchiga xabar.</param>
public sealed record LiveEditTarget(ScheduleTemplate? Template, ScheduleTemplate? Archived, string? Error);

/// <inheritdoc cref="LiveEditTarget"/>
public static class LiveScheduleEdit
{
    /// <summary>
    /// Shablonni o'zgarishga tayyorlaydi (fayl boshidagi qoida). Nusxa va hafta ko'chirishlari
    /// kontekstga qo'shiladi, lekin SAQLANMAYDI — chaqiruvchi tranzaksiya ichida saqlaydi.
    /// </summary>
    public static async Task<LiveEditTarget> PrepareAsync(
        IAppDbContext db, ScheduleTemplate tpl, CancellationToken ct = default)
    {
        var uses = await db.WeekAssignments
            .Where(a => a.TemplateId == tpl.Id && a.ClassId == tpl.ClassId)
            .ToListAsync(ct);
        if (uses.Count == 0) return new LiveEditTarget(tpl, null, null);

        var monday = EditBoundary();
        var weeks = await WeeksAsync(db, ct);
        bool Past(WeekAssignment a) =>
            weeks.TryGetValue((a.Quarter, a.Week), out var w) && string.CompareOrdinal(w.Start, monday) < 0;

        var past = uses.Where(Past).ToList();
        if (past.Count == 0) return new LiveEditTarget(tpl, null, null);
        if (past.Count == uses.Count)
            return new LiveEditTarget(null, null,
                "Bu jadval faqat o'tgan haftalarda ishlatilgan — tarixni saqlash uchun u o'zgartirilmaydi.");

        var copy = new ScheduleTemplate
        {
            ClassId = tpl.ClassId,
            Name = tpl.Name,
            OwnerKind = tpl.OwnerKind,
        };
        copy.Lessons = [.. tpl.Lessons.Select(l => new ScheduleLesson
        {
            TemplateId = copy.Id, Day = l.Day, Period = l.Period, SubjectId = l.SubjectId,
            TeacherId = l.TeacherId, SubGroup = l.SubGroup,
        })];
        db.ScheduleTemplates.Add(copy);
        foreach (var a in uses.Except(past)) a.TemplateId = copy.Id;

        var until = DateOnly.ParseExact(monday, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(-1);
        tpl.Name = $"{tpl.Name} · {until.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} gacha";
        return new LiveEditTarget(copy, tpl, null);
    }

    /// <summary>
    /// Faqat o'tgan haftalarda ishlatilgan shablonlar (eski versiyalar) — o'qituvchi bandligi kabi
    /// "hozir kim qayerda" savollarida hisobga olinmaydi.
    /// </summary>
    public static async Task<HashSet<string>> PastOnlyTemplateIdsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var rank = await TemplateRankAsync(db, ct);
        return (await db.WeekAssignments.AsNoTracking()
                .Where(a => a.TemplateId != null).Select(a => a.TemplateId!).Distinct().ToListAsync(ct))
            .Where(id => rank(id) == RankPastOnly)
            .ToHashSet(StringComparer.Ordinal);
    }

    private const int RankDraft = 0, RankPastOnly = 1, RankUpcoming = 2, RankCurrent = 3;

    /// <summary>
    /// Bitta eganing shablonlaridan "asosiy"likka nomzodlar: egada joriy/keyingi haftalarga biriktirilgan
    /// shablon bo'lsa — faqat o'tgan haftalardagilar (eski versiyalar) chiqarib tashlanadi, aks holda
    /// hammasi (yozgi ta'tilda haqiqiy jadval ham faqat o'tgan haftalarda bo'ladi).
    /// </summary>
    public static IEnumerable<ScheduleTemplate> WithoutHistory(IEnumerable<ScheduleTemplate> owned, Func<string, int> rank)
    {
        var list = owned as IReadOnlyCollection<ScheduleTemplate> ?? [.. owned];
        return list.Any(t => rank(t.Id) >= RankUpcoming) ? list.Where(t => rank(t.Id) != RankPastOnly) : list;
    }

    /// <summary>
    /// Shablonning haftalarga biriktirilishi bo'yicha holati: 3 — joriy haftaga biriktirilgan
    /// (yakshanba kuni — kelgusi haftaga), 2 — chegara va undan keyingi haftalarga, 1 — faqat o'tgan
    /// haftalarda (eski versiya yoki yozgi ta'tildagi haqiqiy jadval), 0 — biriktirilmagan qoralama.
    /// </summary>
    public static async Task<Func<string, int>> TemplateRankAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var boundary = EditBoundary();
        var reference = ReferenceDay();
        var weeks = await WeeksAsync(db, ct);
        var ranks = (await db.WeekAssignments.AsNoTracking()
                .Where(a => a.TemplateId != null)
                .Select(a => new { a.TemplateId, a.Quarter, a.Week }).ToListAsync(ct))
            .GroupBy(a => a.TemplateId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(a =>
            {
                if (!weeks.TryGetValue((a.Quarter, a.Week), out var w)) return RankPastOnly;
                if (string.CompareOrdinal(w.Start, reference) <= 0 && string.CompareOrdinal(reference, w.End) <= 0)
                    return RankCurrent;
                return string.CompareOrdinal(w.Start, boundary) >= 0 ? RankUpcoming : RankPastOnly;
            }), StringComparer.Ordinal);
        return id => ranks.TryGetValue(id, out var r) ? r : RankDraft;
    }

    /// <summary>
    /// Shablon chegaradan oldingi (o'tgan) birorta haftaga biriktirilganmi — bunday shablon
    /// o'chirilmaydi: jurnal, davomat va maosh tarixi unga tayanadi.
    /// </summary>
    public static async Task<bool> UsedInPastAsync(IAppDbContext db, ScheduleTemplate tpl, CancellationToken ct = default)
    {
        var uses = await db.WeekAssignments.AsNoTracking()
            .Where(a => a.TemplateId == tpl.Id && a.ClassId == tpl.ClassId)
            .Select(a => new { a.Quarter, a.Week }).ToListAsync(ct);
        if (uses.Count == 0) return false;
        var boundary = EditBoundary();
        var weeks = await WeeksAsync(db, ct);
        return uses.Any(a => weeks.TryGetValue((a.Quarter, a.Week), out var w) && string.CompareOrdinal(w.Start, boundary) < 0);
    }

    /// <summary>Joriy kalendar haftasining dushanbasi ("yyyy-MM-dd").</summary>
    public static string ThisMonday() =>
        ScheduleMath.MondayOfISO(AppClock.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>
    /// O'zgarish kuchga kiradigan birinchi kun: joriy haftaning dushanbasi, yakshanba kuni esa
    /// ertangi dushanba — o'tib bo'lgan Du–Sha tarix bo'lib qoladi.
    /// </summary>
    public static string EditBoundary() =>
        AppClock.Today.DayOfWeek == DayOfWeek.Sunday
            ? AppClock.Today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : ThisMonday();

    /// <summary>"Joriy hafta" qaysi kunga qarab topiladi: bugun, yakshanba kuni — ertaga.</summary>
    private static string ReferenceDay() =>
        (AppClock.Today.DayOfWeek == DayOfWeek.Sunday ? AppClock.Today.AddDays(1) : AppClock.Today)
        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>(chorak, hafta) → haftaning boshlanish va tugash sanasi.</summary>
    private static async Task<Dictionary<(int Quarter, int Week), (string Start, string End)>> WeeksAsync(
        IAppDbContext db, CancellationToken ct)
    {
        var result = new Dictionary<(int, int), (string, string)>();
        foreach (var q in await db.Quarters.AsNoTracking().ToListAsync(ct))
        {
            if (string.IsNullOrEmpty(q.StartDate) || string.IsNullOrEmpty(q.EndDate)) continue;
            foreach (var w in ScheduleMath.GetQuarterWeeks(q.StartDate, q.EndDate))
                result[(q.Quarter, w.Week)] = (w.StartISO, w.EndISO);
        }
        return result;
    }
}
