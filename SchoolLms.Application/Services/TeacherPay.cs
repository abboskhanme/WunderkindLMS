using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// One teacher's pay for one month, every part named (client, 2026-10-15: "jarima va o'rindosh ustozning oylik
/// summasi biryola moliyaga bog'langan bo'lishi — avtomatik oylikdagi hisob-kitob" and "sertifikat va ustama
/// qismlarini ... to'g'ridan to'g'ri moliyaga bog'lash").
///
/// <para><see cref="LessonPay"/> + <see cref="UstamaAmount"/> is exactly <see cref="TeacherSalaryCalc.MonthlyForMonth"/>
/// (lessons due after absences and substitutions × category rate, plus <see cref="Teacher.BonusPct"/>). On top come
/// the teacher's certificates and named allowances (<see cref="TeacherAllowance"/>) and the month's bonuses and fines
/// from Moliya → Bonus / Jarima (<see cref="PayrollAdjustment"/>, cancelled ones left out). A month never goes below 0.</para>
/// </summary>
public sealed record TeacherPayBreakdown(
    string Month,
    int PlannedLessons,
    int MissedLessons,
    int SubstitutionLessons,
    decimal Rate,
    decimal LessonPay,
    decimal UstamaPct,
    decimal UstamaAmount,
    decimal AllowancePct,
    decimal AllowanceFixed,
    decimal AllowanceAmount,
    decimal Bonus,
    decimal Penalty,
    decimal Total);

/// <summary>Bonus and fine sums of one person for one month.</summary>
public readonly record struct AdjustmentTotals(decimal Bonus, decimal Penalty);

/// <summary>
/// Moliya → Bonus / Jarima as money per person and month. A cancellation is a mirror row pointing at the original
/// (<see cref="PayrollAdjustment.ReversalOf"/>): both are left out, the same reading as the KPI fines.
/// </summary>
public static class PayrollAdjustmentTotals
{
    /// <summary>Teachers: (teachers.id, "yyyy-MM") → totals. <paramref name="teacherId"/> narrows to one.</summary>
    public static Task<Dictionary<(string Id, string Month), AdjustmentTotals>> ForTeachersAsync(
        IAppDbContext db, string? teacherId = null, CancellationToken ct = default) =>
        LoadAsync(db, teacherId, staff: false, ct);

    /// <summary>Non-teacher staff: (users.id, "yyyy-MM") → totals.</summary>
    public static Task<Dictionary<(string Id, string Month), AdjustmentTotals>> ForStaffAsync(
        IAppDbContext db, string? userId = null, CancellationToken ct = default) =>
        LoadAsync(db, userId, staff: true, ct);

    /// <summary>Base pay + bonus − fine, never below 0.</summary>
    public static decimal Apply(decimal basePay, AdjustmentTotals adj) =>
        Math.Max(0m, basePay + adj.Bonus - adj.Penalty);

    private static async Task<Dictionary<(string Id, string Month), AdjustmentTotals>> LoadAsync(
        IAppDbContext db, string? personId, bool staff, CancellationToken ct)
    {
        var reversed = db.PayrollAdjustments.Where(x => x.ReversalOf != null).Select(x => x.ReversalOf);
        var q = db.PayrollAdjustments.AsNoTracking()
            .Where(x => x.ReversalOf == null && !reversed.Contains(x.Id));
        q = staff
            ? q.Where(x => x.UserId != null && (personId == null || x.UserId == personId))
            : q.Where(x => x.TeacherId != null && (personId == null || x.TeacherId == personId));
        var rows = await q
            .Select(x => new { x.TeacherId, x.UserId, x.PeriodYear, x.PeriodMonth, x.Kind, x.Amount })
            .ToListAsync(ct);
        return rows
            .GroupBy(r => ((staff ? r.UserId : r.TeacherId)!, $"{r.PeriodYear:D4}-{r.PeriodMonth:D2}"))
            .ToDictionary(g => g.Key, g => new AdjustmentTotals(
                g.Where(r => r.Kind == AdjustmentKind.Bonus).Sum(r => r.Amount),
                g.Where(r => r.Kind == AdjustmentKind.Penalty).Sum(r => r.Amount)));
    }
}

/// <summary>
/// Everything the teacher salary needs, read once — the salary table, the salary detail, the ledger and the
/// finance salary report all compute through <see cref="For"/>, so the four never disagree.
/// </summary>
public sealed class TeacherPayContext
{
    private readonly SchoolMeta? meta;
    private readonly Dictionary<string, int[]> byWeekday;
    private readonly List<(string Start, string End)> quarters;
    private readonly Dictionary<(string TeacherId, string Month), List<string>> absences;
    private readonly Dictionary<(string TeacherId, string Month), int> substitutions;
    private readonly Dictionary<string, List<TeacherAllowance>> allowances;
    private readonly Dictionary<(string Id, string Month), AdjustmentTotals> adjustments;

    private TeacherPayContext(
        SchoolMeta? meta, Dictionary<string, int[]> byWeekday, List<(string Start, string End)> quarters,
        Dictionary<(string TeacherId, string Month), List<string>> absences,
        Dictionary<(string TeacherId, string Month), int> substitutions,
        Dictionary<string, List<TeacherAllowance>> allowances,
        Dictionary<(string Id, string Month), AdjustmentTotals> adjustments)
    {
        this.meta = meta;
        this.byWeekday = byWeekday;
        this.quarters = quarters;
        this.absences = absences;
        this.substitutions = substitutions;
        this.allowances = allowances;
        this.adjustments = adjustments;
    }

    public SchoolMeta? Meta => meta;

    public IReadOnlyList<(string Start, string End)> Quarters => quarters;

    /// <summary>Loads for every teacher, or for one (<paramref name="teacherId"/>) — the shared parts stay whole.</summary>
    public static async Task<TeacherPayContext> LoadAsync(
        IAppDbContext db, string? teacherId = null, CancellationToken ct = default)
    {
        var meta = await db.SchoolMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        var byWeekday = await TeacherSalaryCalc.LessonsByWeekdayAsync(db);
        var quarters = await TeacherSalaryCalc.QuarterRangesAsync(db);
        var absences = (await db.TeacherAttendances.AsNoTracking()
                .Where(a => a.Status == "absent" && a.Date.Length >= 7 && (teacherId == null || a.TeacherId == teacherId))
                .Select(a => new { a.TeacherId, a.Date }).ToListAsync(ct))
            .GroupBy(a => (a.TeacherId, a.Date[..7]))
            .ToDictionary(g => g.Key, g => g.Select(a => a.Date).ToList());
        var substitutions = await LessonSubstitutionService.MonthDeltaAsync(db, ct);
        var allowances = (await db.TeacherAllowances.AsNoTracking()
                .Where(a => teacherId == null || a.TeacherId == teacherId)
                .ToListAsync(ct))
            .GroupBy(a => a.TeacherId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var adjustments = await PayrollAdjustmentTotals.ForTeachersAsync(db, teacherId, ct);
        return new TeacherPayContext(meta, byWeekday, quarters, absences, substitutions, allowances, adjustments);
    }

    /// <summary>Lessons per weekday (0 = Monday … 5 = Saturday).</summary>
    public int[] WeekdaysOf(string teacherId) => byWeekday.GetValueOrDefault(teacherId) ?? new int[6];

    /// <summary>The month's absent days that count (in the start month — only from the start day).</summary>
    public List<string> AbsentDates(Teacher t, string month)
    {
        var raw = absences.GetValueOrDefault((t.Id, month)) ?? [];
        var startDate = TeacherSalaryCalc.StartDateOf(t);
        return startDate is { Length: >= 10 } && startDate[..7] == month
            ? raw.Where(d => string.CompareOrdinal(d, startDate) >= 0).ToList()
            : raw;
    }

    /// <summary>The allowances paid in <paramref name="month"/>.</summary>
    public List<TeacherAllowance> AllowancesIn(string teacherId, string month) =>
        (allowances.GetValueOrDefault(teacherId) ?? []).Where(a => a.ActiveIn(month)).ToList();

    public AdjustmentTotals AdjustmentsOf(string teacherId, string month) =>
        adjustments.GetValueOrDefault((teacherId, month));

    public TeacherPayBreakdown For(Teacher t, string month)
    {
        var weekdays = WeekdaysOf(t.Id);
        var startDate = TeacherSalaryCalc.StartDateOf(t);
        var planned = TeacherSalaryCalc.PlannedLessonsForMonth(weekdays, month, startDate, quarters);
        var missed = TeacherSalaryCalc.MissedLessons(weekdays, AbsentDates(t, month), quarters);
        var delta = substitutions.GetValueOrDefault((t.Id, month));
        var rate = TeacherSalaryCalc.RateFor(meta, t.Category);
        var lessonPay = Math.Max(0, planned - missed + delta) * rate;
        var ustama = lessonPay * t.BonusPct / 100m;

        var active = AllowancesIn(t.Id, month);
        var pct = active.Where(a => a.AmountKind == TeacherAllowanceKinds.Percent).Sum(a => a.Amount);
        // A fixed allowance follows the lessons: no lessons planned that month (summer, before the start) — none.
        var fixedSum = planned > 0 ? active.Where(a => a.AmountKind == TeacherAllowanceKinds.Fixed).Sum(a => a.Amount) : 0m;
        var allowanceAmount = lessonPay * pct / 100m + fixedSum;

        var adj = AdjustmentsOf(t.Id, month);
        var total = PayrollAdjustmentTotals.Apply(lessonPay + ustama + allowanceAmount, adj);
        return new TeacherPayBreakdown(
            month, planned, missed, delta, rate, lessonPay, t.BonusPct, ustama,
            pct, fixedSum, allowanceAmount, adj.Bonus, adj.Penalty, total);
    }

    /// <summary>What one allowance adds in <paramref name="month"/> given that month's breakdown.</summary>
    public static decimal ValueIn(TeacherAllowance a, TeacherPayBreakdown month) =>
        a.AmountKind == TeacherAllowanceKinds.Percent
            ? month.LessonPay * a.Amount / 100m
            : month.PlannedLessons > 0 ? a.Amount : 0m;
}
