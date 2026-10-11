using static SchoolLms.Application.Services.ExcelExport;

namespace SchoolLms.Application.Services;

/// <summary>Kechki dars / yotoqxona statistikasi Excel'da — ekrandagi raqamlarning o'zi (2026-10-11).</summary>
public static class BoardingAnalyticsExport
{
    public static byte[] Build(BoardingAnalyticsDto d, Dictionary<string, string> reasonNames)
    {
        var title = d.Session == "dorm" ? "Yotoqxona" : "Kechki dars";
        var period = $"{Show(d.From)} – {Show(d.To)}";

        IReadOnlyList<XlsxCell> TallyCells(BoardingTallyDto t) =>
        [
            XlsxCell.Num(t.StudentDays), XlsxCell.Num(t.Present), Pct(t.PresentPct), XlsxCell.Num(t.Absent),
            XlsxCell.Num(t.Excused), XlsxCell.Num(t.Late), XlsxCell.Num(t.Unchecked),
        ];
        string[] tallyHeaders = ["O'quvchi-kun", "Keldi", "Keldi %", "Kelmadi (sababsiz)", "Sababli", "Kech keldi", "Belgilanmagan"];

        var days = d.Trend.Select(p => (IReadOnlyList<XlsxCell>)new List<XlsxCell> { Show(p.Date) }.Concat(TallyCells(p.Tally)).ToList()).ToList();
        var groups = d.Groups.Select(g => (IReadOnlyList<XlsxCell>)new List<XlsxCell> { g.Name, XlsxCell.Num(g.Students) }.Concat(TallyCells(g.Tally)).ToList()).ToList();
        var students = d.Students.Select((s, i) => (IReadOnlyList<XlsxCell>)new List<XlsxCell>
            { XlsxCell.Num(i + 1), s.FullName, s.ClassName, s.GroupName }
            .Concat(TallyCells(s.Tally)).Append(s.LastAbsent is null ? "—" : Show(s.LastAbsent)).ToList()).ToList();
        var events = d.Events.Select(e => (IReadOnlyList<XlsxCell>)new List<XlsxCell>
        {
            Show(e.Date), e.FullName, e.GroupName, Status(e.Status),
            e.ReasonId is null ? "" : reasonNames.GetValueOrDefault(e.ReasonId, "O'chirilgan sabab"),
        }).ToList();

        return BuildTables(
        [
            new TableSpec("Kunlar", ["Sana", .. tallyHeaders], days, null, $"{title} — kunlar bo'yicha, {period} ({d.HeldDays} yo'qlama kuni)"),
            new TableSpec("Guruhlar", ["Guruh", "O'quvchilar", .. tallyHeaders], groups, null, $"{title} — guruhlar bo'yicha, {period}"),
            new TableSpec("O'quvchilar", ["№", "F.I.SH", "Sinf", "Guruh", .. tallyHeaders, "Oxirgi kelmagan"], students, null,
                $"{title} — o'quvchilar, {period}"),
            new TableSpec("Kunlar va sabablar", ["Sana", "F.I.SH", "Guruh", "Holat", "Sabab"], events, null,
                $"{title} — kim, qaysi kuni, nima sababdan, {period}"),
        ]);
    }

    private static string Status(string s) => s switch
    {
        "absent" => "Kelmadi",
        "excused" => "Sababli",
        "late" => "Kech keldi",
        "unchecked" => "Belgilanmagan",
        _ => s,
    };

    private static XlsxCell Pct(double? v) => v is { } p ? XlsxCell.Num((decimal)p) : XlsxCell.Of(null);

    private static string Show(string iso) => iso.Length == 10 ? $"{iso[8..10]}.{iso[5..7]}.{iso[..4]}" : iso;
}
