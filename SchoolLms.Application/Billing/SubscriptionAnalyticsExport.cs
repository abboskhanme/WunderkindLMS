using System.Globalization;
using SchoolLms.Application.Services;
using static SchoolLms.Application.Services.ExcelExport;

namespace SchoolLms.Application.Billing;

/// <summary>
/// "Abonementlar analitikasi" Excel'da (mijoz, 2026-10-09: "excelga kerakli malumotlarni tortib olish
/// ham mumkin bo'lsin"). Ekrandagi raqamlarning o'zi — <see cref="SubscriptionAnalyticsQuery"/> dan,
/// summalar haqiqiy son kataklari (Excel'da qo'shiladi).
/// </summary>
public static class SubscriptionAnalyticsExport
{
    /// <summary>Butun hisobot: sinflar × abonementlar, abonement turlari va o'quvchilar ro'yxati.</summary>
    public static byte[] Workbook(SubscriptionAnalyticsDto overview, SubscriptionStudentsDto pupils)
    {
        var plans = overview.Plans
            .Where(p => overview.PlanTotals.Any(t => t.PlanId == p.PlanId && (t.Students > 0 || t.Billed != 0m)))
            .ToList();
        var period = Period(overview.From, overview.To);

        var classHeaders = new List<string> { "Sinf", "O'quvchilar" };
        classHeaders.AddRange(plans.Select(p => $"{p.CategoryName}: {p.Name}"));
        classHeaders.AddRange(["O'qish abonementisiz", "Hisoblangan", "To'langan", "Qarz"]);
        var classRows = overview.Classes.Select(r =>
        {
            var cells = new List<XlsxCell> { r.ClassName, Num(r.Students) };
            cells.AddRange(plans.Select(p => Num(r.Plans.FirstOrDefault(c => c.PlanId == p.PlanId)?.Students ?? 0)));
            cells.AddRange([Num(r.WithoutTuition), Num(r.Billed), Num(r.Paid), Num(r.Debt)]);
            return (IReadOnlyList<XlsxCell>)cells;
        }).ToList();
        var classTotals = new List<XlsxCell> { "Jami", Num(overview.Students) };
        classTotals.AddRange(plans.Select(p => Num(overview.PlanTotals.First(t => t.PlanId == p.PlanId).Students)));
        classTotals.AddRange([Num(overview.WithoutTuition), Num(overview.Billed), Num(overview.Paid), Num(overview.Debt)]);

        var planRows = plans.Select(p =>
        {
            var t = overview.PlanTotals.First(x => x.PlanId == p.PlanId);
            return (IReadOnlyList<XlsxCell>)new List<XlsxCell>
            {
                p.CategoryName, p.Name, p.MonthlyAmount == 0m ? Of(null) : Num(p.MonthlyAmount),
                Num(t.Students), Num(t.Billed), Num(t.Paid), Num(t.Debt),
            };
        }).ToList();

        return BuildTables(
        [
            new TableSpec("Sinflar", classHeaders, classRows, classTotals, $"Abonementlar analitikasi — sinflar, {period}"),
            new TableSpec("Abonement turlari",
                ["Toifa", "Abonement", "Oylik narx", "O'quvchilar", "Hisoblangan", "To'langan", "Qarz"],
                planRows,
                ["Jami", "", "", "", Num(overview.Billed), Num(overview.Paid), Num(overview.Debt)],
                $"Abonement turlari, {period}"),
            PupilsSheet("O'quvchilar", pupils.Plans, pupils.Students, $"O'quvchilar va abonementlari, {period}"),
        ]);
    }

    /// <summary>Bitta ro'yxat: sinf sahifasi yoki karta/abonement filtri.</summary>
    public static byte[] Pupils(string title, List<SubscriptionPlanColumnDto> plans, List<SubscriptionStudentRowDto> rows,
        DateOnly from, DateOnly to) =>
        BuildTables([PupilsSheet("O'quvchilar", plans, rows, $"{title}, {Period(from, to)}")]);

    private static TableSpec PupilsSheet(
        string name, List<SubscriptionPlanColumnDto> plans, List<SubscriptionStudentRowDto> rows, string title)
    {
        // Toifa ustunlari — ro'yxatda uchraganlari, abonementlar tartibida (o'qish birinchi).
        var used = rows.SelectMany(r => r.Subscriptions.Select(s => s.CategoryCode)).ToHashSet(StringComparer.Ordinal);
        var categories = plans.Where(p => used.Contains(p.CategoryCode))
            .Select(p => (p.CategoryCode, p.CategoryName)).Distinct().ToList();
        var noPlan = rows.Any(r => r.NoPlanBilled != 0m);

        var headers = new List<string> { "№", "F.I.SH", "Sinf" };
        headers.AddRange(categories.Select(c => c.CategoryName));
        if (noPlan) headers.Add("Abonementsiz hisob");
        headers.AddRange(["Hisoblangan", "To'langan", "Qarz"]);

        var data = rows.Select((r, i) =>
        {
            var cells = new List<XlsxCell> { Num(i + 1), r.FullName, r.ClassName };
            cells.AddRange(categories.Select(c => (XlsxCell)string.Join("; ",
                r.Subscriptions.Where(s => s.CategoryCode == c.CategoryCode).Select(Describe))));
            if (noPlan) cells.Add(r.NoPlanBilled == 0m ? Of(null) : Num(r.NoPlanBilled));
            cells.AddRange([Num(r.Billed), Num(r.Paid), Num(r.Debt)]);
            return (IReadOnlyList<XlsxCell>)cells;
        }).ToList();

        var totals = new List<XlsxCell> { "Jami", $"{rows.Count} o'quvchi", "" };
        totals.AddRange(categories.Select(_ => Of(null)));
        if (noPlan) totals.Add(Num(rows.Sum(r => r.NoPlanBilled)));
        totals.AddRange([Num(rows.Sum(r => r.Billed)), Num(rows.Sum(r => r.Paid)), Num(rows.Sum(r => r.Debt))]);
        return new TableSpec(name, headers, data, totals, title);
    }

    /// <summary>"5-11 sinflar (1 690 000/oy, 15.10.2026 dan)".</summary>
    private static string Describe(SubscriptionStudentPlanDto s)
    {
        var term = s.EndsOn is { } end
            ? $"{s.StartsOn:dd.MM.yyyy} – {end:dd.MM.yyyy}"
            : $"{s.StartsOn:dd.MM.yyyy} dan";
        var price = s.MonthlyAmount.ToString("#,0", CultureInfo.GetCultureInfo("ru-RU"));
        return $"{s.PlanName} ({price}/oy, {term})";
    }

    private static XlsxCell Num(decimal v) => XlsxCell.Num(v);

    private static XlsxCell Of(string? v) => XlsxCell.Of(v);

    private static string Period(DateOnly from, DateOnly to) => $"{from:dd.MM.yyyy} – {to:dd.MM.yyyy}";
}
