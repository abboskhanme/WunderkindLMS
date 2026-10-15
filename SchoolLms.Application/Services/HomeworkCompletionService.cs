using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  "Uy vazifasi bajarilishi" — pupil × lesson table of homework statuses
//  (client, 2026-10-15; docs/modules/homework-completion.md).
// ===========================================================================
//
//  NO NEW TABLE. A status is `journal_entries.homework` — the same journal cell as the grade and
//  the attendance reason (values: HomeworkStatus). The sheet only reads it differently: by date
//  range instead of by quarter, one column per lesson, with tallies.
//
//  COLUMNS — the lessons of the owner (class or group) + subject from `from` to min(`to`, today):
//    · the timetable's lessons (JournalService.ComputeColumnsAsync per quarter — the very columns
//      of the journal, holidays and the group cut-over switch included), plus
//    · lessons that are not on the timetable but happened: a conducted note or an attendance mark
//      (HeldLessons), a lesson note with homework text, or a stored homework status — so nothing
//      written is ever hidden. Future lessons are left out: there is nothing to check yet.
//  ROWS — the journal's own roster (LessonRoster: active pupils of the class / active group members).
//  CELL — the stored status; if none is stored and the pupil has a NON-late absence reason in that
//  lesson's cell, it shows as Kelmadi (Y) with `Derived = true`. A derived Kelmadi is never written.
//
//  WRITES (SaveAsync) touch only `homework` of the cell: the grade, reason, behaviour and mastery stay
//  as they are. A missing cell is created; a cell left with nothing at all is removed (the journal's
//  own "Tozalash" rule). The journal's guards apply: archived pupils, the group cut-over switch,
//  the roster, and the automatic "dars o'tildi" of a filled cell (JournalService.AutoConductAsync).
// ===========================================================================

/// <summary>
/// Counts of one row, one column or the whole sheet. <see cref="Percent"/> =
/// (done + ½·partial + ½·late) ÷ <see cref="Marked"/> × 100, one decimal; <c>null</c> when nothing is marked.
/// <see cref="Marked"/> counts done, partial, not done and late — Kelmadi (stored or derived) stays out.
/// </summary>
public record HomeworkTallyDto(int Done, int Partial, int NotDone, int Late, int Absent, int Marked, decimal? Percent);

/// <summary>
/// One lesson column. <see cref="Held"/> — conducted or attendance taken; <see cref="Homework"/> — the
/// homework text set in that lesson (its lesson note), <see cref="HasHomework"/> when it is not blank.
/// </summary>
public record HomeworkLessonDto(
    string Date, int Period, int SubGroup, bool Held, string? Topic, string? Homework, bool HasHomework,
    HomeworkTallyDto Summary);

/// <summary>
/// One cell. <see cref="InLesson"/> = false — the pupil is in the other half of a split lesson (no status).
/// <see cref="Derived"/> = true — not stored: Kelmadi shown from the attendance reason.
/// </summary>
public record HomeworkCellDto(int Status, bool Derived, bool InLesson);

/// <summary>One pupil: cells aligned with <see cref="HomeworkSheetDto.Lessons"/>, and their tally.</summary>
public record HomeworkRowDto(string StudentId, string FullName, int SubGroup, List<HomeworkCellDto> Cells, HomeworkTallyDto Summary);

public record HomeworkSheetDto(
    string OwnerId, string OwnerKind, string OwnerName, bool IsTrack, string SubjectId, string SubjectName,
    string From, string To, List<HomeworkLessonDto> Lessons, List<HomeworkRowDto> Rows, HomeworkTallyDto Total);

/// <summary>One status to write. <see cref="SubGroup"/> — the lesson's half (0 = whole class, always 0 for a group).</summary>
public record HomeworkMarkItem(string StudentId, string Date, int Period, int SubGroup, int Status);

/// <summary>Bulk write for one owner + subject. <see cref="OwnerKind"/> is optional (checked when given).</summary>
public record SaveHomeworkMarksRequest(string OwnerId, string SubjectId, List<HomeworkMarkItem>? Marks, string? OwnerKind = null);

public static class HomeworkCompletionService
{
    /// <summary>Longest range one sheet may span (both ends included).</summary>
    public const int MaxRangeDays = 120;

    /// <summary>Range when none is given: the last two weeks up to today.</summary>
    public const int DefaultRangeDays = 14;

    /// <summary>Most statuses one save may carry (a 40-pupil class × 30 lessons fits).</summary>
    public const int MaxMarks = 2000;

    public const string OwnerNotFoundMessage = "Sinf yoki guruh topilmadi.";
    public const string EmptyMarksMessage = "Saqlash uchun belgi yo'q.";
    public const string SubjectNotFoundMessage = "Fan topilmadi.";
    public const string FutureDateMessage =
        "Dars hali o'tilmagan — kelajakdagi sanaga uy vazifasi belgisini qo'yib bo'lmaydi.";

    /* ------------------------------------------------------------------ input */

    /// <summary>
    /// The owner the request names. <paramref name="ownerKind"/> may be omitted; when given it must be the
    /// owner's actual kind (a class id is never a group). <c>null</c> — no such owner.
    /// </summary>
    public static async Task<LessonOwner?> OwnerAsync(
        IAppDbContext db, string? ownerKind, string? ownerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) return null;
        var owner = await LessonRoster.OwnerAsync(db, ownerId, ct);
        if (owner is null) return null;
        return string.IsNullOrWhiteSpace(ownerKind) || owner.Kind == ownerKind ? owner : null;
    }

    /// <summary>
    /// Parses the range ("yyyy-MM-dd"). Missing <paramref name="to"/> = today; missing
    /// <paramref name="from"/> = <see cref="DefaultRangeDays"/> days ending at <c>to</c>.
    /// </summary>
    /// <returns><c>null</c> — valid; otherwise the message for a 400.</returns>
    public static string? ParseRange(string? from, string? to, out DateOnly start, out DateOnly end)
    {
        start = end = default;
        if (string.IsNullOrWhiteSpace(to)) end = AppClock.Today;
        else if (!TryDate(to, out end)) return "Sana noto'g'ri — yyyy-MM-dd ko'rinishida bo'lsin.";

        if (string.IsNullOrWhiteSpace(from)) start = end.AddDays(-(DefaultRangeDays - 1));
        else if (!TryDate(from, out start)) return "Sana noto'g'ri — yyyy-MM-dd ko'rinishida bo'lsin.";

        if (start > end) return "Boshlanish sanasi tugash sanasidan keyin bo'lmasin.";
        if (end.DayNumber - start.DayNumber + 1 > MaxRangeDays)
            return $"Oraliq {MaxRangeDays} kundan oshmasin.";
        return null;
    }

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /* ------------------------------------------------------------------ read */

    public static async Task<HomeworkSheetDto> BuildSheetAsync(
        IAppDbContext db, LessonOwner owner, string subjectId, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        var fromIso = Iso(from);
        var toIso = Iso(to);
        var subjectName = await db.Subjects.AsNoTracking()
            .Where(s => s.Id == subjectId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";

        var today = AppClock.Today;
        var last = to < today ? to : today;
        var lastIso = Iso(last);

        // The group cut-over switch off — no lessons at all (ComputeColumnsAsync's rule).
        var live = await LessonRoster.LessonsLiveAsync(db, owner, ct);
        var lessonKeys = live && from <= last
            ? await LessonColumnsAsync(db, owner, subjectId, fromIso, lastIso, ct)
            : [];

        var notes = lessonKeys.Count == 0
            ? []
            : await db.LessonNotes.AsNoTracking()
                .Where(n => n.ClassId == owner.Id && n.SubjectId == subjectId && n.OwnerKind == owner.Kind
                            && string.Compare(n.Date, fromIso) >= 0 && string.Compare(n.Date, lastIso) <= 0)
                .ToListAsync(ct);
        var entries = lessonKeys.Count == 0
            ? []
            : await db.JournalEntries.AsNoTracking()
                .Where(e => e.ClassId == owner.Id && e.SubjectId == subjectId && e.OwnerKind == owner.Kind
                            && string.Compare(e.Date, fromIso) >= 0 && string.Compare(e.Date, lastIso) <= 0)
                .Select(e => new { e.StudentId, e.Date, e.Period, e.Homework, e.ReasonId })
                .ToListAsync(ct);
        // No unique key on a journal cell (JournalServiceTests): the first row wins, as in the journal grid.
        var cellOf = entries
            .GroupBy(e => (e.StudentId, e.Date, e.Period))
            .ToDictionary(g => g.Key, g => (g.First().Homework, g.First().ReasonId));
        var lateReasonIds = (await db.AbsenceReasons.AsNoTracking()
                .Where(r => r.IsLate).Select(r => r.Id).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var students = live ? await LessonRoster.ForLessonAsync(db, owner, ct: ct) : [];

        var rows = new List<HomeworkRowDto>(students.Count);
        var columnStatuses = lessonKeys.Select(_ => new List<int>()).ToList();
        foreach (var s in students)
        {
            var studentSubGroup = owner.IsGroup ? 0 : s.SubGroup;
            var cells = new List<HomeworkCellDto>(lessonKeys.Count);
            for (var i = 0; i < lessonKeys.Count; i++)
            {
                var col = lessonKeys[i];
                var inLesson = col.SubGroup == 0 || col.SubGroup == studentSubGroup;
                if (!inLesson)
                {
                    cells.Add(new HomeworkCellDto(HomeworkStatus.None, false, false));
                    continue;
                }
                var status = HomeworkStatus.None;
                var derived = false;
                if (cellOf.TryGetValue((s.Id, col.Date, col.Period), out var cell))
                {
                    status = cell.Homework;
                    // Absent (any reason that is not a lateness) and nothing marked — shown as Kelmadi.
                    if (status == HomeworkStatus.None && !string.IsNullOrEmpty(cell.ReasonId)
                        && !lateReasonIds.Contains(cell.ReasonId))
                    {
                        status = HomeworkStatus.Absent;
                        derived = true;
                    }
                }
                cells.Add(new HomeworkCellDto(status, derived, true));
                columnStatuses[i].Add(status);
            }
            rows.Add(new HomeworkRowDto(s.Id, s.FullName, studentSubGroup, cells,
                Tally(cells.Where(c => c.InLesson).Select(c => c.Status))));
        }

        var lessons = lessonKeys.Select((c, i) =>
        {
            var note = NoteFor(notes, c);
            var text = note?.Homework;
            return new HomeworkLessonDto(
                c.Date, c.Period, c.SubGroup, c.Held, note?.Topic, text, !string.IsNullOrWhiteSpace(text),
                Tally(columnStatuses[i]));
        }).ToList();

        return new HomeworkSheetDto(
            owner.Id, owner.Kind, owner.Name, owner.IsTrack, subjectId, subjectName, fromIso, toIso,
            lessons, rows, Tally(columnStatuses.SelectMany(x => x)));
    }

    private sealed record LessonKey(string Date, int Period, int SubGroup, bool Held);

    /// <summary>The sheet's columns (rules in the file header), ordered by date, period, half.</summary>
    private static async Task<List<LessonKey>> LessonColumnsAsync(
        IAppDbContext db, LessonOwner owner, string subjectId, string fromIso, string lastIso, CancellationToken ct)
    {
        bool InRange(string d) => string.CompareOrdinal(d, fromIso) >= 0 && string.CompareOrdinal(d, lastIso) <= 0;

        // 1. The timetable — exactly the journal's columns, quarter by quarter.
        var quarters = await db.Quarters.AsNoTracking()
            .Where(q => string.Compare(q.StartDate, lastIso) <= 0 && string.Compare(q.EndDate, fromIso) >= 0)
            .Select(q => q.Quarter).ToListAsync(ct);
        var keys = new HashSet<(string Date, int Period, int SubGroup)>();
        foreach (var quarter in quarters)
            foreach (var c in await JournalService.ComputeColumnsAsync(db, owner.Id, subjectId, quarter))
                if (InRange(c.Date)) keys.Add((c.Date, c.Period, owner.IsGroup ? 0 : c.SubGroup));

        // 2. What happened: conducted notes and attendance marks (HeldLessons — the statistics' rule).
        var held = (await HeldLessons.ListAsync(db, [owner.Id], fromIso, lastIso, ct))
            .Where(h => h.SubjectId == subjectId && h.OwnerKind == owner.Kind)
            .Select(h => (h.Date, h.Period, SubGroup: owner.IsGroup ? 0 : h.SubGroup))
            .ToList();
        // 3. Lessons that set homework text, and cells that already carry a status.
        var noted = await db.LessonNotes.AsNoTracking()
            .Where(n => n.ClassId == owner.Id && n.SubjectId == subjectId && n.OwnerKind == owner.Kind
                        && n.Homework != null && n.Homework != ""
                        && string.Compare(n.Date, fromIso) >= 0 && string.Compare(n.Date, lastIso) <= 0)
            .Select(n => new { n.Date, n.Period, n.SubGroup }).ToListAsync(ct);
        var marked = await db.JournalEntries.AsNoTracking()
            .Where(e => e.ClassId == owner.Id && e.SubjectId == subjectId && e.OwnerKind == owner.Kind
                        && e.Homework != HomeworkStatus.None
                        && string.Compare(e.Date, fromIso) >= 0 && string.Compare(e.Date, lastIso) <= 0)
            .Select(e => new { e.Date, e.Period }).Distinct().ToListAsync(ct);

        // A timetable lesson owns its (date, period): an off-timetable source only adds a column where the
        // timetable has none. (A note's half is not trusted next to the timetable: the automatic
        // "o'tildi" note takes the pupil's half even in a whole-class lesson.)
        var scheduledSlots = keys.Select(k => (k.Date, k.Period)).ToHashSet();
        foreach (var k in held.Concat(noted.Select(n => (n.Date, n.Period, SubGroup: owner.IsGroup ? 0 : n.SubGroup))))
            if (InRange(k.Date) && !scheduledSlots.Contains((k.Date, k.Period))) keys.Add(k);
        var slots = keys.Select(k => (k.Date, k.Period)).ToHashSet();
        foreach (var m in marked)
            if (!slots.Contains((m.Date, m.Period))) keys.Add((m.Date, m.Period, 0));

        return keys
            .Select(k => new LessonKey(k.Date, k.Period, k.SubGroup,
                held.Any(h => h.Date == k.Date && h.Period == k.Period
                              && (h.SubGroup == k.SubGroup || h.SubGroup == 0 || k.SubGroup == 0))))
            .OrderBy(k => k.Date, StringComparer.Ordinal).ThenBy(k => k.Period).ThenBy(k => k.SubGroup)
            .ToList();
    }

    /// <summary>The lesson's note: the same half first, then the whole class, then any note of that slot.</summary>
    private static LessonNote? NoteFor(List<LessonNote> notes, LessonKey c)
    {
        var slot = notes.Where(n => n.Date == c.Date && n.Period == c.Period).ToList();
        return slot.FirstOrDefault(n => n.SubGroup == c.SubGroup)
               ?? slot.FirstOrDefault(n => n.SubGroup == 0)
               ?? slot.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n.Homework));
    }

    /// <summary>Counts and the completion rate (definition on <see cref="HomeworkTallyDto"/>).</summary>
    public static HomeworkTallyDto Tally(IEnumerable<int> statuses)
    {
        int done = 0, partial = 0, notDone = 0, late = 0, absent = 0, marked = 0;
        decimal credit = 0;
        foreach (var s in statuses)
        {
            switch (s)
            {
                case HomeworkStatus.Done: done++; break;
                case HomeworkStatus.Partial: partial++; break;
                case HomeworkStatus.NotDone: notDone++; break;
                case HomeworkStatus.Late: late++; break;
                case HomeworkStatus.Absent: absent++; break;
            }
            if (!HomeworkStatus.CountsTowardsRate(s)) continue;
            marked++;
            credit += HomeworkStatus.Credit(s);
        }
        decimal? percent = marked == 0
            ? null
            : Math.Round(credit * 100m / marked, 1, MidpointRounding.AwayFromZero);
        return new HomeworkTallyDto(done, partial, notDone, late, absent, marked, percent);
    }

    /* ------------------------------------------------------------------ write */

    /// <summary>
    /// Writes the statuses of one owner + subject. All-or-nothing: every mark is checked before anything
    /// is written. Caller has already checked access (and, for teachers, future dates).
    /// </summary>
    /// <returns><c>null</c> — saved; otherwise the message for a 400.</returns>
    public static async Task<string?> SaveAsync(
        IAppDbContext db, LessonOwner owner, string subjectId, IReadOnlyList<HomeworkMarkItem>? marks,
        CancellationToken ct = default)
    {
        if (marks is null || marks.Count == 0) return EmptyMarksMessage;
        if (marks.Count > MaxMarks) return $"Bir martada ko'pi bilan {MaxMarks} ta belgi saqlanadi.";
        foreach (var m in marks)
        {
            if (!HomeworkStatus.IsValid(m.Status)) return HomeworkStatus.InvalidMessage;
            if (string.IsNullOrWhiteSpace(m.StudentId)) return "O'quvchi ko'rsatilmagan.";
            if (!TryDate(m.Date, out _)) return "Sana noto'g'ri — yyyy-MM-dd ko'rinishida bo'lsin.";
            if (m.Period <= 0) return "Dars raqami noto'g'ri.";
            if (m.SubGroup is < 0 or > 2) return "subGroup 0, 1 yoki 2 bo'lishi kerak.";
            if (owner.IsGroup && m.SubGroup != 0) return JournalService.SubGroupOnGroupMessage;
        }
        if (string.IsNullOrWhiteSpace(subjectId) || !await db.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            return SubjectNotFoundMessage;
        // The group cut-over switch off — the group journal is closed (§4.3).
        if (!await LessonRoster.LessonsLiveAsync(db, owner, ct)) return JournalService.GroupLessonsOffMessage;

        var studentIds = marks.Select(m => m.StudentId).Distinct(StringComparer.Ordinal).ToList();
        // An archived pupil's journal is closed (client, 2026-10-08) — the same message as the cell.
        var archived = await ArchivedStudents.NamesAsync(db, studentIds, ct);
        if (archived.Count > 0) return ArchivedStudents.Message(archived);

        var roster = (await LessonRoster.ForLessonAsync(db, owner, ct: ct)).ToDictionary(s => s.Id, StringComparer.Ordinal);
        foreach (var m in marks)
        {
            if (!roster.TryGetValue(m.StudentId, out var s))
                return "O'quvchi bu sinf yoki guruh ro'yxatida yo'q.";
            if (m.SubGroup != 0 && s.SubGroup != m.SubGroup)
                return $"{s.FullName} {m.SubGroup}-guruh darsiga kirmaydi.";
        }

        // Quarter of each date — a new cell needs it (the journal is read per quarter).
        var quarters = await db.Quarters.AsNoTracking().ToListAsync(ct);
        var quarterOf = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var date in marks.Select(m => m.Date).Distinct(StringComparer.Ordinal))
        {
            var q = quarters.FirstOrDefault(x =>
                string.CompareOrdinal(date, x.StartDate) >= 0 && string.CompareOrdinal(date, x.EndDate) <= 0);
            if (q is null) return $"{date} hech bir chorakka tushmaydi — choraklar sozlamasini tekshiring.";
            quarterOf[date] = q.Quarter;
        }

        // The same cell twice in one request — the last one wins.
        var latest = marks.GroupBy(m => (m.StudentId, m.Date, m.Period)).Select(g => g.Last()).ToList();
        var dates = quarterOf.Keys.ToList();
        // Matched by (pupil, date, period) — not by quarter: a cell written before the quarters were
        // re-dated is still the same cell. Every duplicate row of a cell gets the status.
        var existing = (await db.JournalEntries
                .Where(e => e.ClassId == owner.Id && e.SubjectId == subjectId && e.OwnerKind == owner.Kind
                            && dates.Contains(e.Date) && studentIds.Contains(e.StudentId))
                .ToListAsync(ct))
            .GroupBy(e => (e.StudentId, e.Date, e.Period))
            .ToDictionary(g => g.Key, g => g.ToList());

        var touched = new HashSet<(int Quarter, string Date, int Period, int SubGroup)>();
        foreach (var m in latest)
        {
            if (existing.TryGetValue((m.StudentId, m.Date, m.Period), out var cells))
            {
                foreach (var e in cells)
                {
                    e.Homework = m.Status;
                    if (e.Grade is null && e.ReasonId is null && e.Homework == HomeworkStatus.None
                        && e.Behavior == 0 && e.Mastery is null)
                        db.JournalEntries.Remove(e);
                }
            }
            else if (m.Status != HomeworkStatus.None)
            {
                db.JournalEntries.Add(new JournalEntry
                {
                    ClassId = owner.Id,
                    SubjectId = subjectId,
                    Quarter = quarterOf[m.Date],
                    StudentId = m.StudentId,
                    Date = m.Date,
                    Period = m.Period,
                    // The journal's rule (SetEntryAsync): the pupil's half; a group has none.
                    SubGroup = owner.IsGroup ? 0 : roster[m.StudentId].SubGroup,
                    OwnerKind = owner.Kind,
                    Homework = m.Status,
                });
            }
            if (m.Status != HomeworkStatus.None) touched.Add((quarterOf[m.Date], m.Date, m.Period, m.SubGroup));
        }
        await db.SaveChangesAsync(ct);

        // A filled cell marks its lesson "o'tildi" — exactly as a cell saved in the journal does.
        var flags = await JournalSettingsGuard.FlagsAsync(db, ct);
        foreach (var slot in touched.OrderBy(t => t.Date, StringComparer.Ordinal).ThenBy(t => t.Period))
            await JournalService.AutoConductAsync(
                db, flags, owner.Id, subjectId, slot.Quarter, slot.Date, slot.Period, slot.SubGroup, owner.Kind);
        return null;
    }

    /* ------------------------------------------------------------------ export */

    /// <summary>The sheet as shown — statuses, per-pupil tallies and per-lesson rates — as .xlsx.</summary>
    public static byte[] ExportXlsx(HomeworkSheetDto sheet)
    {
        static string Day(string iso) =>
            DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : iso;
        static string Pct(decimal? p) => p is { } v ? v.ToString("0.#", CultureInfo.InvariantCulture) + "%" : "";
        string[] statusHeaders = [.. HomeworkStatus.Ordered.Select(s => $"{HomeworkStatus.Symbol(s)} {HomeworkStatus.Label(s)}")];
        static int Count(HomeworkTallyDto t, int status) => status switch
        {
            HomeworkStatus.Done => t.Done,
            HomeworkStatus.Partial => t.Partial,
            HomeworkStatus.NotDone => t.NotDone,
            HomeworkStatus.Late => t.Late,
            _ => t.Absent,
        };

        var ownerLabel = sheet.OwnerKind == LessonOwnerKind.Group
            ? $"{(sheet.IsTrack ? "Yo'nalish" : "Guruh")}: {sheet.OwnerName}"
            : $"{sheet.OwnerName}-sinf";
        var title = $"Uy vazifasi bajarilishi — {ownerLabel} · {sheet.SubjectName} · {Day(sheet.From)} – {Day(sheet.To)}";

        var headers = new List<string> { "№", "F.I.SH" };
        headers.AddRange(sheet.Lessons.Select(l =>
            $"{Day(l.Date)[..5]} · {l.Period}-dars" + (l.SubGroup > 0 ? $" · G{l.SubGroup}" : "")));
        headers.AddRange(statusHeaders);
        headers.Add("Bajarilish %");

        var rows = sheet.Rows.Select((r, i) =>
        {
            var cells = new List<ExcelExport.XlsxCell> { (i + 1).ToString(CultureInfo.InvariantCulture), r.FullName };
            cells.AddRange(r.Cells.Select(c => (ExcelExport.XlsxCell)(!c.InLesson
                ? "—"
                : c.Derived ? $"({HomeworkStatus.Symbol(c.Status)})" : HomeworkStatus.Symbol(c.Status))));
            cells.AddRange(HomeworkStatus.Ordered.Select(s => (ExcelExport.XlsxCell)Count(r.Summary, s).ToString(CultureInfo.InvariantCulture)));
            cells.Add(Pct(r.Summary.Percent));
            return (IReadOnlyList<ExcelExport.XlsxCell>)cells;
        }).ToList();

        var totals = new List<ExcelExport.XlsxCell> { "", "Bajarilish % (dars bo'yicha)" };
        totals.AddRange(sheet.Lessons.Select(l => (ExcelExport.XlsxCell)Pct(l.Summary.Percent)));
        totals.AddRange(HomeworkStatus.Ordered.Select(s => (ExcelExport.XlsxCell)Count(sheet.Total, s).ToString(CultureInfo.InvariantCulture)));
        totals.Add(Pct(sheet.Total.Percent));

        var widths = new List<double> { 5, 34 };
        widths.AddRange(sheet.Lessons.Select(_ => 14d));
        widths.AddRange(statusHeaders.Select(_ => 14d));
        widths.Add(13);

        var legend = new List<IReadOnlyList<ExcelExport.XlsxCell>>();
        foreach (var s in HomeworkStatus.Ordered)
            legend.Add(new ExcelExport.XlsxCell[] { HomeworkStatus.Symbol(s), HomeworkStatus.Label(s) });
        legend.Add(new ExcelExport.XlsxCell[]
        {
            $"({HomeworkStatus.Symbol(HomeworkStatus.Absent)})",
            "Kelmadi — davomatdan (darsda yo'q edi, uy vazifasi belgilanmagan)",
        });
        legend.Add(new ExcelExport.XlsxCell[] { "—", "O'quvchi bu guruh darsiga kirmaydi" });
        legend.Add(new ExcelExport.XlsxCell[]
        {
            "%", "Bajarilish = (bajardi + ½·chala + ½·kech qoldi) ÷ belgilanganlar. Kelmadi hisobga kirmaydi.",
        });

        return ExcelExport.BuildTables(
        [
            new ExcelExport.TableSpec("Uy vazifasi", headers, rows, totals, title, widths),
            new ExcelExport.TableSpec("Izoh", ["Belgi", "Ma'nosi"], legend, Widths: [10, 80]),
        ]);
    }
}
