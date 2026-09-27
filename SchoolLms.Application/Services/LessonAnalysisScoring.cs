using System.Globalization;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Dars tahlili — scoring (docs/modules/lesson-analysis.md §4).
//
//  THE NUMBERS MUST MATCH THE OLD APP (acceptance test: every migrated
//  teacher's rating equals the old rating board). The old app computed in
//  Python floats and rounded with `round()`, which rounds the EXACT binary
//  value of the double half-to-even (round(3.125, 2) == 3.12, round(2.675, 2)
//  == 2.67). `Math.Round(double)` does not do that, so every rounding here goes
//  through <see cref="PyRound(double,int)"/>, and averages are summed as doubles
//  in the same way the old code (and PostgreSQL float8 avg) did.
// ===========================================================================

/// <summary>Scoring rules and Python-compatible rounding. Pure functions.</summary>
public static class LessonAnalysisScoring
{
    /// <summary>
    /// Python's <c>round(value, digits)</c>: half-to-even on the exact binary value of the
    /// double. The double is expanded to 20 decimals (exact formatting since .NET Core 3.0),
    /// which is far below the smallest distance between a 0–100 double and a rounding midpoint.
    /// </summary>
    public static decimal PyRoundDecimal(double value, int digits)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0m;
        var exact = decimal.Parse(
            value.ToString("F20", CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture);
        return Math.Round(exact, digits, MidpointRounding.ToEven);
    }

    /// <summary><see cref="PyRoundDecimal"/> as a double (for JSON numbers).</summary>
    public static double PyRound(double value, int digits) =>
        double.Parse(
            PyRoundDecimal(value, digits).ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);

    /// <summary>Old <c>_percentage</c>: round(part / whole * 100, 1), 0 when whole is 0.</summary>
    public static double Percentage(int part, int whole) =>
        whole == 0 ? 0.0 : PyRound((double)part / whole * 100, 1);

    /// <summary>
    /// Old <c>recompute_score</c>: only ANSWERED details count (is_done not null);
    /// percent = round(done / total * 100, 2), 0 when nothing is answered.
    /// </summary>
    public static void Recompute(LessonObservation observation)
    {
        var total = 0;
        var done = 0;
        foreach (var d in observation.Details)
        {
            if (d.IsDone is not { } isDone) continue;
            total++;
            if (isDone) done++;
        }
        observation.ScoreDone = done;
        observation.ScoreTotal = total;
        observation.ScorePercent = total == 0 ? 0m : PyRoundDecimal((double)done / total * 100, 2);
    }

    /// <summary>
    /// Mean of stored percents the way the old app got it: a float sum divided by the count
    /// (PostgreSQL <c>avg(float8)</c>), not rounded. null for an empty set.
    /// </summary>
    public static double? Mean(IEnumerable<decimal> percents)
    {
        double sum = 0;
        var n = 0;
        foreach (var p in percents)
        {
            sum += (double)p;
            n++;
        }
        return n == 0 ? null : sum / n;
    }

    /// <summary>Python <c>f"{value:.0f}"</c> — half-to-even on the exact double.</summary>
    public static string FormatWhole(double value) =>
        PyRoundDecimal(value, 0).ToString("0", CultureInfo.InvariantCulture);
}
