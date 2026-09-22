using SchoolLms.Application.Services;
using static SchoolLms.Application.Services.ExamScoringService;

namespace SchoolLms.Tests.Exams;

// ===========================================================================
//  ExamScoringService — the one scoring engine (admission-and-testing.md §2.1,
//  §5.7, §8.3, §8.4). Pure functions: no database, no fixture, milliseconds.
//  Unit B3 grades online papers through GradeOnline; B2's grid and import go
//  through SummarizeManual. Every rounding rule of both is pinned here.
// ===========================================================================

public class ScoringTests
{
    // =====================================================================
    //  Online (§8.3)
    // =====================================================================

    /// <summary>
    /// Per section: correct = selected option is THE correct one; unanswered
    /// counts as wrong but still counts as a question; points = correct ×
    /// points_per_correct; the maximum is the published ceiling.
    /// </summary>
    [Fact]
    public void Onlayn_har_bolim_togri_javoblar_boyicha_hisoblanadi()
    {
        Section[] sections =
        [
            new("math", MaxScore: 6m, PointsPerCorrect: 2m),     // 3 questions × 2
            new("english", MaxScore: 3.75m, PointsPerCorrect: 1.25m), // 3 questions × 1.25
        ];
        PaperAnswer[] paper =
        [
            new("math", "q1", "q1-right"),
            new("math", "q2", "q2-wrong"),
            new("math", "q3", null),          // not answered
            new("english", "q4", "q4-right"),
            new("english", "q5", "q5-right"),
            new("english", "q6", "q6-wrong"),
        ];
        var key = new Dictionary<string, string>
        {
            ["q1"] = "q1-right", ["q2"] = "q2-right", ["q3"] = "q3-right",
            ["q4"] = "q4-right", ["q5"] = "q5-right", ["q6"] = "q6-right",
        };

        var score = GradeOnline(sections, paper, key);

        var math = Assert.Single(score.Sections, s => s.SectionId == "math");
        Assert.Equal((1, 3, 2m, 6m), (math.CorrectCount, math.QuestionCount, math.Points, math.MaxPoints));
        var english = Assert.Single(score.Sections, s => s.SectionId == "english");
        Assert.Equal((2, 3, 2.5m, 3.75m), (english.CorrectCount, english.QuestionCount, english.Points, english.MaxPoints));

        Assert.Equal(3, score.CorrectCount);
        Assert.Equal(6, score.QuestionCount);
        Assert.Equal(4.5m, score.TotalPoints);
        Assert.Equal(9.75m, score.MaxPoints);
        Assert.Equal(46.15m, score.Percent); // 4.5 / 9.75 = 46.1538… → 46.15
    }

    /// <summary>A section with no paper rows still appears — the bars and the summary must add up.</summary>
    [Fact]
    public void Onlayn_savolsiz_bolim_ham_natijada_bor()
    {
        Section[] sections = [new("a", 4m, 2m), new("b", 2m, 1m)];
        var score = GradeOnline(sections, [new PaperAnswer("a", "q1", "o1")], new Dictionary<string, string> { ["q1"] = "o1" });

        Assert.Equal(2, score.Sections.Count);
        var b = Assert.Single(score.Sections, s => s.SectionId == "b");
        Assert.Equal((0, 0, 0m), (b.CorrectCount, b.QuestionCount, b.Points));
        Assert.Equal(2m, score.TotalPoints);
        Assert.Equal(6m, score.MaxPoints);
        Assert.Equal(33.33m, score.Percent);
    }

    /// <summary>
    /// Loading bugs throw instead of producing a plausible wrong score: a paper
    /// row for a foreign section, a question without a key, an unpublished section.
    /// </summary>
    [Fact]
    public void Onlayn_notogri_kirish_xato_beradi_jim_hisoblamaydi()
    {
        Section[] sections = [new("a", 2m, 1m)];
        var key = new Dictionary<string, string> { ["q1"] = "o1" };

        Assert.Throws<ArgumentException>(() => GradeOnline(sections, [new PaperAnswer("zzz", "q1", "o1")], key));
        Assert.Throws<ArgumentException>(() => GradeOnline(sections, [new PaperAnswer("a", "q-unknown", "o1")], key));
        Assert.Throws<ArgumentException>(() =>
            GradeOnline([new Section("a", 2m, null)], [new PaperAnswer("a", "q1", "o1")], key));
        // Three correct answers against a ceiling of two — the paper is bigger than what was published.
        Assert.Throws<InvalidOperationException>(() => GradeOnline(
            sections,
            [new PaperAnswer("a", "q1", "o1"), new PaperAnswer("a", "q2", "o2"), new PaperAnswer("a", "q3", "o3")],
            new Dictionary<string, string> { ["q1"] = "o1", ["q2"] = "o2", ["q3"] = "o3" }));
    }

    // =====================================================================
    //  Manual (§8.4)
    // =====================================================================

    /// <summary>
    /// The maximum is the WHOLE exam's: a pupil with two of three subjects
    /// entered is 60 of 150, not 60 of 100. No question counts on paper.
    /// </summary>
    [Fact]
    public void Qolda_maksimum_butun_imtihonniki()
    {
        Section[] sections = [new("math", 50m), new("physics", 50m), new("english", 50m)];

        var score = SummarizeManual(sections, new Dictionary<string, decimal> { ["math"] = 40m, ["physics"] = 20m });

        Assert.Null(score.CorrectCount);
        Assert.Null(score.QuestionCount);
        Assert.Equal(60m, score.TotalPoints);
        Assert.Equal(150m, score.MaxPoints);
        Assert.Equal(40m, score.Percent);
        Assert.Equal(2, score.Sections.Count);
        Assert.All(score.Sections, s => Assert.Null(s.CorrectCount));
        Assert.Equal(50m, Assert.Single(score.Sections, s => s.SectionId == "math").MaxPoints);
    }

    /// <summary>Half-up to two decimals, applied to points before summing and to the percent.</summary>
    [Fact]
    public void Qolda_yaxlitlash_yarmidan_yuqoriga()
    {
        Section[] sections = [new("a", 3m), new("b", 3m)];

        var score = SummarizeManual(sections, new Dictionary<string, decimal> { ["a"] = 1.005m, ["b"] = 1m });

        Assert.Equal(1.01m, Assert.Single(score.Sections, s => s.SectionId == "a").Points);
        Assert.Equal(2.01m, score.TotalPoints);
        Assert.Equal(33.5m, score.Percent); // 2.01 / 6 = 33.5 %
        Assert.Equal(66.67m, Percent(2m, 3m));
        Assert.Equal(0.13m, RoundPoints(0.125m));
    }

    /// <summary>§5.7 `nullif(max_points, 0)`: no maximum, no percent — not a misleading 0 %.</summary>
    [Fact]
    public void Maksimum_nol_bolsa_foiz_yoq()
    {
        Assert.Null(Percent(0m, 0m));
        Assert.Null(SummarizeManual([], new Dictionary<string, decimal>()).Percent);
    }

    /// <summary>A value outside [0, max] never reaches the summary — and the rule names both numbers.</summary>
    [Fact]
    public void Qolda_oraliqdan_tashqari_ball_rad_etiladi()
    {
        Section[] sections = [new("a", 50m)];

        Assert.Throws<ArgumentException>(() => SummarizeManual(sections, new Dictionary<string, decimal> { ["a"] = 50.01m }));
        Assert.Throws<ArgumentException>(() => SummarizeManual(sections, new Dictionary<string, decimal> { ["a"] = -1m }));
        Assert.Throws<ArgumentException>(() => SummarizeManual(sections, new Dictionary<string, decimal> { ["zzz"] = 1m }));

        Assert.Null(PointsProblem(0m, 50m));
        Assert.Null(PointsProblem(50m, 50m));
        Assert.Equal("ball 0 dan 50 gacha bo'lishi kerak (55.5 kiritildi)", PointsProblem(55.5m, 50m));
        Assert.Equal("ball 0 dan 12.5 gacha bo'lishi kerak (-1 kiritildi)", PointsProblem(-1m, 12.5m));
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("42.5", "42.5")]
    [InlineData("42.50", "42.5")]
    [InlineData("42.125", "42.13")]
    [InlineData("0", "0")]
    public void Ball_ekrandagidek_yoziladi(string raw, string expected) =>
        Assert.Equal(expected, Format(decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture)));
}
