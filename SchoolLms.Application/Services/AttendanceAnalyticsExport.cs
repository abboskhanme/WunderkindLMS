using SchoolLms.Application.Dtos;
using static SchoolLms.Application.Services.ExcelExport;

namespace SchoolLms.Application.Services;

/// <summary>
/// Davomat analitikasi Excel'da (mijoz, 2026-10-11: "sabablarini yuklab olish va batafsil malumot
/// qismi"). Ekrandagi raqamlarning o'zi — <see cref="AttendanceAnalytics.BuildAsync"/> natijasidan.
/// </summary>
public static class AttendanceAnalyticsExport
{
    /// <summary>"Kim, qaysi kuni" eksportda shuncha qatorgacha (ekranda 5000).</summary>
    public const int MaxEvents = 50_000;

    public static byte[] Build(AttendanceAnalyticsDto d)
    {
        var reasonName = d.Reasons.ToDictionary(r => r.ReasonId, r => r.Name, StringComparer.Ordinal);
        var period = $"{Show(d.From)} – {Show(d.To)}";

        // 1. Dars qoldirganlar — sababsiz kunlari ko'pidan.
        var absentees = (d.Students ?? [])
            .Where(s => s.Tally.Absent > 0 || s.Tally.Partial > 0)
            .OrderByDescending(s => s.Tally.Unexcused).ThenByDescending(s => s.Tally.Absent)
            .ThenBy(s => s.FullName, StringComparer.Ordinal)
            .ToList();
        var studentRows = absentees.Select((s, i) => (IReadOnlyList<XlsxCell>)new List<XlsxCell>
        {
            XlsxCell.Num(i + 1), s.FullName, s.RowName,
            XlsxCell.Num(s.Tally.Unexcused), XlsxCell.Num(s.Tally.Excused), XlsxCell.Num(s.Tally.Absent),
            XlsxCell.Num(s.Tally.Partial), XlsxCell.Num(s.Tally.Late),
            s.Tally.PresentPct is { } p ? XlsxCell.Num((decimal)Math.Round(p, 1)) : XlsxCell.Of(null),
            s.LastPresent is { } last ? Show(last) : "—",
        }).ToList();

        // 2. Kunlar va sabablar — har bir "keldi" bo'lmagan kun.
        var eventRows = (d.Events ?? []).Select(e => (IReadOnlyList<XlsxCell>)new List<XlsxCell>
        {
            Show(e.Date), e.FullName, e.RowName, StatusLabel(e.Status),
            string.Join(", ", e.ReasonIds.Select(id => reasonName.GetValueOrDefault(id, "O'chirilgan sabab"))),
            e.Status == "absent" || e.Status == "partial" ? (e.Unexcused ? "Sababsiz" : "Sababli") : "",
            e.Periods.Count > 0 ? string.Join(", ", e.Periods) : "",
            XlsxCell.Num(e.HeldLessons),
        }).ToList();

        // 3. Sabablar.
        var reasonRows = d.Reasons.Where(r => r.Count > 0).Select(r => (IReadOnlyList<XlsxCell>)new List<XlsxCell>
        {
            r.Name, r.Short, r.IsLate ? "Kech keldi" : r.Unexcused ? "Sababsiz" : "Sababli", XlsxCell.Num(r.Count),
        }).ToList();

        // 4. Dars soatlari — butun davr.
        var periodRows = (d.PeriodsRange ?? []).Select(p => (IReadOnlyList<XlsxCell>)new List<XlsxCell>
        {
            XlsxCell.Num(p.Period),
            p.StartTime is null ? "" : $"{p.StartTime}–{p.EndTime}",
            XlsxCell.Num(p.Tally.Opportunities), XlsxCell.Num(p.Tally.Present), Pct(p.Tally.PresentPct),
            XlsxCell.Num(p.Tally.Absent), Pct(p.Tally.AbsentPct),
            XlsxCell.Num(p.Tally.Unchecked), Pct(p.Tally.UncheckedPct),
        }).ToList();

        return BuildTables(
        [
            new TableSpec("Dars qoldirganlar",
                ["№", "F.I.SH", "Sinf / guruh", "Sababsiz kun", "Sababli kun", "Jami kelmagan kun",
                 "Ayrim darslarga kelmagan", "Kech qolgan", "Davomat %", "Oxirgi kelgan"],
                studentRows, null, $"Dars qoldirgan o'quvchilar, {period}"),
            new TableSpec("Kunlar va sabablar",
                ["Sana", "F.I.SH", "Sinf / guruh", "Holat", "Sabab", "Turi", "Qoldirgan darslari", "O'tilgan darslar"],
                eventRows, null,
                d.EventsTruncated ? $"Kim, qaysi kuni, nima sababdan, {period} (birinchi {MaxEvents} ta)"
                    : $"Kim, qaysi kuni, nima sababdan, {period}"),
            new TableSpec("Sabablar", ["Sabab", "Qisqa", "Turi", "Kunlar soni"], reasonRows, null,
                $"Sabablar bo'yicha, {period}"),
            new TableSpec("Dars soatlari",
                ["Dars", "Vaqti", "O'quvchilar", "Keldi", "Keldi %", "Kelmadi", "Kelmadi %", "Tekshirilmagan", "Tekshirilmagan %"],
                periodRows, null, $"Dars soatlari bo'yicha, {period}"),
        ]);
    }

    private static string StatusLabel(string status) => status switch
    {
        "absent" => "Kelmadi",
        "partial" => "Ayrim darslarga kelmadi",
        "late" => "Kech keldi",
        "unchecked" => "Belgilanmagan",
        _ => status,
    };

    private static XlsxCell Pct(double? v) => v is { } p ? XlsxCell.Num((decimal)Math.Round(p, 1)) : XlsxCell.Of(null);

    /// <summary>"2026-10-09" → "09.10.2026".</summary>
    private static string Show(string iso) => iso.Length == 10 ? $"{iso[8..10]}.{iso[5..7]}.{iso[..4]}" : iso;
}
