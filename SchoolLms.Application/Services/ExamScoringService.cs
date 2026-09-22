namespace SchoolLms.Application.Services;

// ===========================================================================
//  The ONE scoring engine of the exams module.
//  Spec: docs/modules/admission-and-testing.md §2.1, §5.7, §5.8, §8.3, §8.4.
//  Units: B2 (manual entry — this file's first caller), B3 (online grading).
// ===========================================================================
//
//  WHY A PURE FUNCTION
//  -------------------
//  §2.1 allows exactly one scoring path for both ways into an exam: the online
//  engine (answers + answer key → points) and manual entry (typed points →
//  summary). Both must end in the same `exam_section_scores` rows and the same
//  `exam_participants` summary, or the results register, the export and every
//  report become two implementations that drift.
//
//  So the arithmetic lives here and nothing else does: no database, no clock,
//  no HTTP. The caller loads the inputs, calls one of the two entry points and
//  writes the returned numbers (`ExamService.WriteResult` does that for both
//  units). A test can pin every rounding rule without a Postgres container.
//
//  NUMBERS
//  -------
//  Points are `numeric(8,2)` (per participant) and `numeric(6,2)` (per section
//  ceiling); percent is `numeric(5,2)`. Every value that leaves this class is
//  rounded half-up (`MidpointRounding.AwayFromZero`) to two decimals HERE, so
//  PostgreSQL's own rounding on insert never has to decide anything and the
//  value the API returns is the value the database holds.
//  `decimal`, never `double`: 0.1 + 0.2 must be 0.3.
// ===========================================================================

/// <summary>
/// Turns what a participant did into points — per section and in total.
/// Stateless and side-effect free; see the file header for why.
///
/// <para><b>Two entry points, one result shape:</b></para>
/// <list type="bullet">
///   <item><see cref="GradeOnline"/> — B3: the materialised paper
///   (<c>exam_answers</c>) against the answer key
///   (<c>question_options.is_correct</c>).</item>
///   <item><see cref="SummarizeManual"/> — B2: points typed into the entry grid
///   or imported from Excel.</item>
/// </list>
/// Both return an <see cref="ExamScore"/>; <c>ExamService.WriteResult</c> writes
/// it onto <c>exam_section_scores</c> and <c>exam_participants</c>.
/// </summary>
public static class ExamScoringService
{
    /// <summary>
    /// One section of the exam as the scorer needs it.
    /// </summary>
    /// <param name="SectionId"><c>exam_sections.id</c>.</param>
    /// <param name="MaxScore">
    /// <c>exam_sections.max_score</c> — for an online exam
    /// <c>question_count × points_per_correct</c>, written at publish (§8.1);
    /// for a manual exam the ceiling of the entry grid.
    /// </param>
    /// <param name="PointsPerCorrect">
    /// <c>exam_sections.points_per_correct</c>, copied from the bank at publish
    /// (§5.6). Required by <see cref="GradeOnline"/>, ignored by
    /// <see cref="SummarizeManual"/>.
    /// </param>
    public readonly record struct Section(string SectionId, decimal MaxScore, decimal? PointsPerCorrect = null);

    /// <summary>One row of the materialised paper — <c>exam_answers</c> (§5.11).</summary>
    /// <param name="SectionId">The section the question was drawn for.</param>
    /// <param name="QuestionId"><c>questions.id</c>.</param>
    /// <param name="SelectedOptionId"><c>null</c> = not answered, which scores as wrong.</param>
    public readonly record struct PaperAnswer(string SectionId, string QuestionId, string? SelectedOptionId);

    /// <summary>The score of one section — one <c>exam_section_scores</c> row.</summary>
    /// <param name="CorrectCount">Online only; <c>null</c> for manual entry.</param>
    /// <param name="QuestionCount">Online only; <c>null</c> for manual entry.</param>
    /// <param name="Points">0 ≤ points ≤ <paramref name="MaxPoints"/>, two decimals.</param>
    /// <param name="MaxPoints">Copied from <see cref="Section.MaxScore"/>.</param>
    public sealed record SectionScore(
        string SectionId, int? CorrectCount, int? QuestionCount, decimal Points, decimal MaxPoints);

    /// <summary>
    /// The whole sitting — the summary columns of <c>exam_participants</c> plus
    /// the per-section rows.
    /// </summary>
    /// <param name="CorrectCount">Online: total correct. Manual: <c>null</c> (§8.4 — there were no questions).</param>
    /// <param name="QuestionCount">Online: size of the paper. Manual: <c>null</c>.</param>
    /// <param name="TotalPoints">Sum of the section points.</param>
    /// <param name="MaxPoints">Sum of the ceilings of EVERY section of the exam — see <see cref="SummarizeManual"/>.</param>
    /// <param name="Percent"><see cref="Percent(decimal, decimal)"/>; <c>null</c> when <paramref name="MaxPoints"/> is 0.</param>
    /// <param name="Sections">
    /// Online: one entry per exam section, always. Manual: only the sections
    /// that have points — an empty cell is not a zero.
    /// </param>
    public sealed record ExamScore(
        int? CorrectCount, int? QuestionCount, decimal TotalPoints, decimal MaxPoints, decimal? Percent,
        IReadOnlyList<SectionScore> Sections);

    // =====================================================================
    //  Online (B3)
    // =====================================================================

    /// <summary>
    /// Grades an online paper (§8.3): per section, <c>correct</c> = answers whose
    /// selected option is the correct one, <c>points = correct × points_per_correct</c>.
    ///
    /// <para>
    /// An unanswered question (<see cref="PaperAnswer.SelectedOptionId"/> = null)
    /// is wrong, not skipped: it still counts in <c>QuestionCount</c>. Every
    /// section of the exam gets a <see cref="SectionScore"/>, even one with no
    /// paper rows, so the summary and the per-subject bars always add up.
    /// </para>
    /// </summary>
    /// <param name="sections">Every section of the exam, published (points copied).</param>
    /// <param name="paper">The attempt's <c>exam_answers</c> rows.</param>
    /// <param name="correctOptionByQuestion">
    /// <c>question_id → id of the one option with is_correct</c>, for at least
    /// every question on the paper. Load it with a hand-written projection;
    /// never hand this dictionary to a public DTO (§7.7).
    /// </param>
    /// <exception cref="ArgumentException">
    /// A paper row points at a section that is not in <paramref name="sections"/>,
    /// a question has no key, or a section has no <c>PointsPerCorrect</c>. Each
    /// of these is a bug in the caller's loading — grading on through it would
    /// write a wrong score that looks right.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A section's points exceed its ceiling — the paper holds more questions
    /// than were published for it. <c>ck_exam_section_scores_points</c> would
    /// refuse the row anyway; this says why.
    /// </exception>
    public static ExamScore GradeOnline(
        IReadOnlyList<Section> sections,
        IEnumerable<PaperAnswer> paper,
        IReadOnlyDictionary<string, string> correctOptionByQuestion)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(correctOptionByQuestion);

        var known = sections.ToDictionary(s => s.SectionId, StringComparer.Ordinal);
        var tally = sections.ToDictionary(s => s.SectionId, _ => (Correct: 0, Total: 0), StringComparer.Ordinal);

        foreach (var answer in paper)
        {
            if (!known.ContainsKey(answer.SectionId))
                throw new ArgumentException(
                    $"Paper row for question '{answer.QuestionId}' points at section '{answer.SectionId}', "
                    + "which is not a section of this exam.", nameof(paper));
            if (!correctOptionByQuestion.TryGetValue(answer.QuestionId, out var correctOptionId))
                throw new ArgumentException(
                    $"No answer key for question '{answer.QuestionId}'.", nameof(correctOptionByQuestion));

            var (correct, total) = tally[answer.SectionId];
            var isCorrect = answer.SelectedOptionId is not null
                && string.Equals(answer.SelectedOptionId, correctOptionId, StringComparison.Ordinal);
            tally[answer.SectionId] = (correct + (isCorrect ? 1 : 0), total + 1);
        }

        var scores = new List<SectionScore>(sections.Count);
        foreach (var section in sections)
        {
            var perCorrect = section.PointsPerCorrect
                ?? throw new ArgumentException(
                    $"Section '{section.SectionId}' has no points_per_correct — was the exam published?",
                    nameof(sections));

            var (correct, total) = tally[section.SectionId];
            var points = RoundPoints(correct * perCorrect);
            var max = RoundPoints(section.MaxScore);
            if (points > max)
                throw new InvalidOperationException(
                    $"Section '{section.SectionId}': {points} points exceed the ceiling {max} — "
                    + "the paper holds more questions than were published for this section.");

            scores.Add(new SectionScore(section.SectionId, correct, total, points, max));
        }

        return Summarize(sections, scores, online: true);
    }

    // =====================================================================
    //  Manual (B2)
    // =====================================================================

    /// <summary>
    /// Summarises typed points (§8.4). <c>CorrectCount</c> and <c>QuestionCount</c>
    /// stay <c>null</c> — there were no questions.
    ///
    /// <para>
    /// <b>The maximum is the whole exam's</b>, not the sum of the sections that
    /// happen to have points: a pupil scored in two of three subjects so far is
    /// 60 out of 150, not 60 out of 100. Otherwise the percent of a
    /// half-entered row would be higher than it will be when the entry is done.
    /// </para>
    /// </summary>
    /// <param name="sections">Every section of the exam.</param>
    /// <param name="pointsBySection">
    /// <c>section_id → points</c> for the sections that have a value. Each value
    /// must already be valid (<see cref="PointsProblem"/> returned <c>null</c>).
    /// </param>
    /// <exception cref="ArgumentException">
    /// A key is not a section of the exam, or a value is outside its range — the
    /// caller skipped validation.
    /// </exception>
    public static ExamScore SummarizeManual(
        IReadOnlyList<Section> sections, IReadOnlyDictionary<string, decimal> pointsBySection)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(pointsBySection);

        var known = sections.ToDictionary(s => s.SectionId, StringComparer.Ordinal);
        foreach (var key in pointsBySection.Keys)
        {
            if (!known.ContainsKey(key))
                throw new ArgumentException($"'{key}' is not a section of this exam.", nameof(pointsBySection));
        }

        var scores = new List<SectionScore>(pointsBySection.Count);
        foreach (var section in sections)
        {
            if (!pointsBySection.TryGetValue(section.SectionId, out var raw)) continue;

            var max = RoundPoints(section.MaxScore);
            var points = RoundPoints(raw);
            if (PointsProblem(points, max) is { } problem)
                throw new ArgumentException(
                    $"Section '{section.SectionId}': {problem}", nameof(pointsBySection));

            scores.Add(new SectionScore(section.SectionId, null, null, points, max));
        }

        return Summarize(sections, scores, online: false);
    }

    // =====================================================================
    //  Rules shared with validation
    // =====================================================================

    /// <summary>
    /// <c>round(total / max × 100, 2)</c>, half-up — §5.7's formula, with its
    /// <c>nullif(max, 0)</c>: <c>null</c> when the maximum is zero. A percent of
    /// an exam worth nothing is undefined, and 0 % would read as "failed".
    /// </summary>
    public static decimal? Percent(decimal total, decimal max) =>
        max == 0m ? null : Math.Round(total / max * 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Points to two decimals, half-up — the precision of every points column.
    /// A typed <c>42.125</c> is stored and shown as <c>42.13</c>.
    /// </summary>
    public static decimal RoundPoints(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Is <paramref name="points"/> a valid score for a section whose ceiling is
    /// <paramref name="max"/>? <c>null</c> when it is; otherwise the Uzbek reason
    /// (without the row and subject — the caller prefixes those, §8.4).
    /// Round first (<see cref="RoundPoints"/>): the rule is about the stored value.
    /// </summary>
    public static string? PointsProblem(decimal points, decimal max) =>
        points < 0m || points > max
            ? $"ball 0 dan {Format(max)} gacha bo'lishi kerak ({Format(points)} kiritildi)"
            : null;

    /// <summary>Points the way the screens print them: <c>42</c>, <c>42.5</c>, <c>42.25</c>.</summary>
    public static string Format(decimal value) =>
        RoundPoints(value).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    // =====================================================================

    private static ExamScore Summarize(IReadOnlyList<Section> sections, List<SectionScore> scores, bool online)
    {
        var total = RoundPoints(scores.Sum(s => s.Points));
        var max = RoundPoints(sections.Sum(s => s.MaxScore));
        return new ExamScore(
            online ? scores.Sum(s => s.CorrectCount ?? 0) : null,
            online ? scores.Sum(s => s.QuestionCount ?? 0) : null,
            total,
            max,
            Percent(total, max),
            scores);
    }
}
