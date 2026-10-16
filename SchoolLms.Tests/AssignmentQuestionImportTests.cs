using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Exams;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Vazifalar / Topshiriqlar — bulk import of test questions (docs/modules/test-import.md).
// ===========================================================================
//
//  1. THE PARSER — straight on AssignmentQuestionImport, no HTTP: the text
//     format and its leniencies, JSON shapes, Word (typed and auto-numbered),
//     the Excel template, the reason chips and their numbers.
//  2. THE SAVE RULE — AssignmentService.CheckQuestions, and the same rule on
//     the admin and teacher create/update endpoints (400 naming the question,
//     nothing written).
//  3. THE ENDPOINTS — parse and template on both sides: gates (teacher
//     assignments key, admin `app`, anonymous 401), bad uploads, no DB writes.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class AssignmentQuestionImportTests(ApiFixture fixture)
{
    private const string TeacherParse = "/api/teacher/assignments/questions/parse";
    private const string TeacherTemplate = "/api/teacher/assignments/questions/template";
    private const string AdminParse = "/api/admin/assignments/questions/parse";
    private const string AdminTemplate = "/api/admin/assignments/questions/template";
    private const string Admin = "/api/admin/assignments";
    private const string Teacher = "/api/teacher/assignments";

    // =====================================================================
    //  1. Parser — text
    // =====================================================================

    [Fact]
    public void Text_format_with_star_marks_becomes_questions_in_order()
    {
        var r = ParseText("""
            1. O'zbekiston poytaxti qaysi shahar?
            A) Samarqand
            *B) Toshkent
            C) Buxoro
            D) Xiva

            2. 2 + 2 = ?
            A) 3
            B) 5
            *C) 4
            """);

        Assert.Equal(AssignmentQuestionImport.UnitQuestion, r.Unit);
        Assert.Equal(2, r.TotalRows);
        Assert.Equal(2, r.ValidCount);
        Assert.Equal(0, r.ErrorCount);
        Assert.Empty(r.Errors);

        Assert.Equal("O'zbekiston poytaxti qaysi shahar?", r.Questions[0].Text);
        Assert.Equal(new[] { "Samarqand", "Toshkent", "Buxoro", "Xiva" }, r.Questions[0].Options);
        Assert.Equal(1, r.Questions[0].CorrectIndex);
        Assert.Equal(new[] { "3", "5", "4" }, r.Questions[1].Options);
        Assert.Equal(2, r.Questions[1].CorrectIndex);
    }

    [Theory]
    [InlineData("Javob: B", 1)]
    [InlineData("javob - c", 2)]
    [InlineData("To'g'ri javob: B) Toshkent", 1)]
    [InlineData("To‘g‘ri javob: A", 0)]
    [InlineData("Javobi: Buxoro", 2)]
    [InlineData("Javob: 2", 1)]
    [InlineData("Answer: c", 2)]
    [InlineData("Javob: В", 1)] // Cyrillic В typed for Latin B
    public void A_javob_line_names_the_answer_by_letter_text_or_number(string answerLine, int expected)
    {
        var r = ParseText($"""
            1. Poytaxt?
            A) Samarqand
            B) Toshkent
            C) Buxoro
            {answerLine}
            """);

        var q = Assert.Single(r.Questions);
        Assert.Equal(expected, q.CorrectIndex);
    }

    /// <summary>Uzbek/Russian Cyrillic tests letter options А Б В Г — there В is the THIRD option.</summary>
    [Fact]
    public void Cyrillic_option_letters_resolve_the_answer_by_their_own_alphabet()
    {
        var r = ParseText("""
            1. Пойтахт?
            А) Самарқанд
            Б) Бухоро
            В) Тошкент
            Г) Хива
            Жавоб: В
            """);

        var q = Assert.Single(r.Questions);
        Assert.Equal(new[] { "Самарқанд", "Бухоро", "Тошкент", "Хива" }, q.Options);
        Assert.Equal(2, q.CorrectIndex);
    }

    [Theory]
    [InlineData("a) bir\n+b) ikki\nc) uch", 1)]
    [InlineData("A. bir\nB. ikki\n* C. uch", 2)]
    [InlineData("A) bir\nB) ikki ✅\nC) uch", 1)]
    [InlineData("A) bir\nB) *ikki\nC) uch", 1)]
    [InlineData("A) bir\nB) ikki\nC) uch *", 2)]
    [InlineData("A) bir\n**B) ikki**\nC) uch", 1)]
    public void Option_letters_and_marks_are_read_leniently(string options, int expected)
    {
        var r = ParseText("1) Qaysi?\n" + options);

        var q = Assert.Single(r.Questions);
        Assert.Equal(new[] { "bir", "ikki", "uch" }, q.Options);
        Assert.Equal(expected, q.CorrectIndex);
    }

    [Fact]
    public void Every_reason_is_reported_with_the_question_number()
    {
        var r = ParseText("""
            1. Yaxshi savol
            A) bir
            *B) ikki

            2. Javobsiz
            A) bir
            B) ikki

            3. Bitta variant
            *A) yolg'iz

            4. Yettita variant
            A) 1
            B) 2
            C) 3
            D) 4
            E) 5
            F) 6
            *G) 7

            5. Ikki belgi
            *A) bir
            *B) ikki

            6. Belgi va javob boshqa-boshqa
            *A) bir
            B) ikki
            Javob: B

            7. Yo'q harf
            A) bir
            B) ikki
            Javob: D

            8. Takror
            A) bir
            *B) bir

            9.
            A) matnsiz
            *B) savol
            """);

        Assert.Equal(9, r.TotalRows);
        Assert.Equal(1, r.ValidCount);
        Assert.Equal(8, r.ErrorCount);
        Assert.Equal("Yaxshi savol", Assert.Single(r.Questions).Text);
        Assert.Equal(
            new[]
            {
                (AssignmentQuestionReason.EmptyText, "9"),
                (AssignmentQuestionReason.TooFewOptions, "3"),
                (AssignmentQuestionReason.TooManyOptions, "4"),
                (AssignmentQuestionReason.NoCorrect, "2"),
                (AssignmentQuestionReason.BadCorrect, "7"),
                (AssignmentQuestionReason.ManyCorrect, "5,6"),
                (AssignmentQuestionReason.DuplicateOption, "8"),
            },
            r.Errors.Select(e => (e.Reason, string.Join(",", e.Rows))));
    }

    /// <summary>
    /// What a chat model really answers: a preamble, markdown, a fence, unnumbered questions,
    /// a blank line between a question and its options, a two-line question, a key after a
    /// blank line, and a sign-off. Only the questions survive.
    /// </summary>
    [Fact]
    public void Blank_lines_preamble_markdown_and_sign_off_are_handled()
    {
        var r = ParseText("""
            Albatta! Mana 5-sinf uchun 3 ta test savoli:

            ```
            **1. Quyosh tizimidagi eng katta sayyora?**

            A) Mars
            **B)** Yupiter *
            C) Venera

            Quyidagi she'r muallifi kim?
            "Yoshlikda yig'ilgan bilim"
            - A) Navoiy
            - B) Bobur
            - C) Cho'lpon

            Javob: A

            ### 3. Suv formulasi?
            A) CO2
            B) H2O
            Javob: B
            ```

            Omad! Yana savollar kerak bo'lsa, yozing.
            """);

        Assert.Equal(3, r.TotalRows);
        Assert.Equal(0, r.ErrorCount);
        Assert.Equal("Quyosh tizimidagi eng katta sayyora?", r.Questions[0].Text);
        Assert.Equal(new[] { "Mars", "Yupiter", "Venera" }, r.Questions[0].Options);
        Assert.Equal(1, r.Questions[0].CorrectIndex);
        Assert.Equal("Quyidagi she'r muallifi kim? \"Yoshlikda yig'ilgan bilim\"", r.Questions[1].Text);
        Assert.Equal(0, r.Questions[1].CorrectIndex);
        Assert.Equal(new[] { "CO2", "H2O" }, r.Questions[2].Options);
        Assert.Equal(1, r.Questions[2].CorrectIndex);
    }

    /// <summary>No blank line and no numbers: a new "A)" starts the next question, taking the line before it.</summary>
    [Fact]
    public void Unseparated_unnumbered_questions_split_at_the_next_A()
    {
        var r = ParseText("""
            Birinchi savol?
            A) bir
            *B) ikki
            Ikkinchi savol?
            A) uch
            *B) to'rt
            Uchinchi savol?
            A) besh
            B) olti
            Javob: A
            Izoh: bu tushuntirish tashlab yuboriladi.
            To'rtinchi savol?
            *A) yetti
            B) sakkiz
            """);

        Assert.Equal(4, r.ValidCount);
        Assert.Equal(new[] { "Birinchi savol?", "Ikkinchi savol?", "Uchinchi savol?", "To'rtinchi savol?" },
            r.Questions.Select(q => q.Text));
        Assert.Equal(new[] { "bir", "ikki" }, r.Questions[0].Options);
        Assert.Equal(new[] { "besh", "olti" }, r.Questions[2].Options);
        Assert.Equal(new[] { 1, 1, 0, 0 }, r.Questions.Select(q => q.CorrectIndex));
    }

    [Fact]
    public void A_question_without_options_between_two_questions_is_an_error_not_dropped()
    {
        var r = ParseText("""
            Birinchi?
            *A) bir
            B) ikki

            Variantlari unutilgan savol

            Uchinchi?
            A) uch
            *B) to'rt
            """);

        Assert.Equal(3, r.TotalRows);
        Assert.Equal(2, r.ValidCount);
        Assert.Contains(r.Errors, e => e.Reason == AssignmentQuestionReason.TooFewOptions && e.Rows.SequenceEqual(new[] { 2 }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData(null)]
    public void Empty_text_is_fatal(string? text)
    {
        var outcome = AssignmentQuestionImport.FromText(text);
        Assert.Equal(AssignmentQuestionImport.NoInputMessage, outcome.FatalMessage);
    }

    [Fact]
    public void Text_without_any_question_is_zero_rows_not_an_error()
    {
        var r = ParseText("Salom, bu shunchaki matn.");

        Assert.Equal(0, r.TotalRows);
        Assert.Empty(r.Questions);
    }

    [Fact]
    public void More_than_the_question_cap_is_fatal()
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= AssignmentService.MaxQuestions + 1; i++)
            sb.Append($"{i}. Savol {i}\nA) bir\n*B) ikki\n\n");

        var outcome = AssignmentQuestionImport.FromText(sb.ToString());

        Assert.Equal(AssignmentQuestionImport.TooManyQuestionsMessage(AssignmentService.MaxQuestions + 1), outcome.FatalMessage);
    }

    // =====================================================================
    //  1. Parser — JSON
    // =====================================================================

    [Fact]
    public void Json_in_uzbek_keys_with_letter_answers()
    {
        var r = Json("""
            [
              { "savol": "Poytaxt?", "variantlar": ["Samarqand", "Toshkent", "Buxoro"], "javob": "B" },
              { "savol": "2+2?", "variantlar": [3, 4, 5], "javob": "а" },
            ]
            """);

        Assert.Equal(2, r.ValidCount);
        Assert.Equal(1, r.Questions[0].CorrectIndex);
        Assert.Equal(new[] { "3", "4", "5" }, r.Questions[1].Options);
        Assert.Equal(0, r.Questions[1].CorrectIndex); // Cyrillic а as Latin a
    }

    [Fact]
    public void Json_with_english_keys_and_a_zero_based_index()
    {
        var r = Json("""{ "questions": [ { "text": "Q?", "options": ["x", "y", "z"], "correctIndex": 2 }, { "question": "R?", "choices": ["p", "q"], "correct": 0 } ] }""");

        Assert.Equal(2, r.ValidCount);
        Assert.Equal(2, r.Questions[0].CorrectIndex);
        Assert.Equal(0, r.Questions[1].CorrectIndex);
    }

    [Fact]
    public void Json_options_as_a_letter_map_or_as_objects_with_isCorrect()
    {
        var r = Json("""
            [
              { "savol": "Map?", "variantlar": { "A": "bir", "B": "ikki", "C": "uch" }, "javob": "C" },
              { "savol": "Obj?", "options": [ { "text": "bir" }, { "text": "ikki", "isCorrect": true } ] },
              { "savol": "Text answer?", "variantlar": ["olma", "nok"], "javob": "nok" }
            ]
            """);

        Assert.Equal(3, r.ValidCount);
        Assert.Equal(new[] { 2, 1, 1 }, r.Questions.Select(q => q.CorrectIndex));
    }

    [Fact]
    public void Json_errors_are_numbered_by_array_position()
    {
        var r = Json("""
            [
              { "savol": "Yaxshi", "variantlar": ["a", "b"], "javob": 1 },
              { "savol": "Indeks tashqarida", "variantlar": ["a", "b"], "javob": 5 },
              { "savol": "", "variantlar": ["a", "b"], "javob": "A" },
              "bu obyekt emas",
              { "savol": "Javobsiz", "variantlar": ["a", "b"] }
            ]
            """);

        Assert.Equal(5, r.TotalRows);
        Assert.Equal(1, r.ValidCount);
        Assert.Contains(r.Errors, e => e.Reason == AssignmentQuestionReason.BadCorrect && e.Rows.SequenceEqual(new[] { 2 }));
        Assert.Contains(r.Errors, e => e.Reason == AssignmentQuestionReason.EmptyText && e.Rows.SequenceEqual(new[] { 3, 4 }));
        Assert.Contains(r.Errors, e => e.Reason == AssignmentQuestionReason.NoCorrect && e.Rows.SequenceEqual(new[] { 4, 5 }));
    }

    [Fact]
    public void Pasted_json_in_a_code_fence_is_read_as_json()
    {
        var r = ParseText("```json\n[{ \"savol\": \"Q?\", \"variantlar\": [\"a\", \"b\"], \"javob\": \"A\" }]\n```");

        Assert.Equal("Q?", Assert.Single(r.Questions).Text);
    }

    [Fact]
    public void A_broken_json_file_is_fatal_but_broken_json_text_falls_back_to_the_text_format()
    {
        var file = AssignmentQuestionImport.FromFile("savollar.json", Stream(Encoding.UTF8.GetBytes("[{ \"savol\": ")));
        Assert.Equal(AssignmentQuestionImport.BadJsonMessage, file.FatalMessage);

        var shape = AssignmentQuestionImport.FromFile("savollar.json", Stream(Encoding.UTF8.GetBytes("{ \"x\": 1 }")));
        Assert.Equal(AssignmentQuestionImport.BadJsonShapeMessage, shape.FatalMessage);

        var text = ParseText("[5-sinf] Matematika\n\n1. 2+2?\nA) 3\n*B) 4");
        Assert.Equal(1, Assert.Single(text.Questions).CorrectIndex);
    }

    // =====================================================================
    //  1. Parser — Word, .txt, Excel
    // =====================================================================

    [Fact]
    public void Docx_paragraphs_go_through_the_text_format()
    {
        var bytes = Docx(
        [
            P("1. Poytaxt qayer?"), P("A) Samarqand"), P("*B) Toshkent"), P(""),
            P("2. 2+2=?"), P("A) 3"), P("B) 4"), P("Javob: B"),
        ]);

        var r = ParseFile("test.docx", bytes);

        Assert.Equal(2, r.ValidCount);
        Assert.Equal(new[] { 1, 1 }, r.Questions.Select(q => q.CorrectIndex));
        Assert.Equal(AssignmentQuestionImport.UnitQuestion, r.Unit);
    }

    /// <summary>
    /// Word's automatic lists — numbers and letters are list formatting, not text. A decimal
    /// list for the questions and a fresh letter list per question must read the same.
    /// </summary>
    [Fact]
    public void Docx_auto_numbered_and_auto_lettered_lists_are_written_out()
    {
        var numbering = new Numbering(
            AbstractList(1, NumberFormatValues.Decimal, "%1."),
            AbstractList(2, NumberFormatValues.UpperLetter, "%1)"),
            new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 },
            new NumberingInstance(new AbstractNumId { Val = 2 }) { NumberID = 2 },
            new NumberingInstance(new AbstractNumId { Val = 2 }) { NumberID = 3 });

        var bytes = Docx(
        [
            Listed("Poytaxt qayer?", 1), Listed("Samarqand", 2), Listed("*Toshkent", 2), Listed("Buxoro", 2),
            Listed("Eng katta sayyora?", 1), Listed("Mars", 3), Listed("Yupiter", 3), P("Javob: B"),
        ], numbering);

        var r = ParseFile("test.docx", bytes);

        Assert.Equal(2, r.ValidCount);
        Assert.Equal(new[] { "Poytaxt qayer?", "Eng katta sayyora?" }, r.Questions.Select(q => q.Text));
        Assert.Equal(new[] { "Samarqand", "Toshkent", "Buxoro" }, r.Questions[0].Options);
        Assert.Equal(new[] { 1, 1 }, r.Questions.Select(q => q.CorrectIndex));
    }

    [Fact]
    public void A_broken_docx_is_one_sentence()
    {
        var outcome = AssignmentQuestionImport.FromFile("test.docx", Stream("PK\u0003\u0004 not a document"u8.ToArray()));
        Assert.Equal(AssignmentQuestionImport.BadDocxMessage, outcome.FatalMessage);
    }

    [Fact]
    public void Txt_file_with_a_bom_reads_as_text()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("1. Savol?\r\nA) bir\r\n*B) ikki\r\n")).ToArray();

        var r = ParseFile("savollar.txt", bytes);

        Assert.Equal("Savol?", Assert.Single(r.Questions).Text);
    }

    /// <summary>
    /// The bank's "Savollar" template: sheet row numbers, unit "row", and the image column
    /// ignored — an outside URL that the bank refuses imports here.
    /// </summary>
    [Fact]
    public void Xlsx_uses_the_bank_template_with_sheet_rows_and_no_image_check()
    {
        var sheet = QuestionBankKit.Sheet(
        [
            QuestionBankKit.Row("Poytaxt?", ["Samarqand", "Toshkent"], "B", "https://x.uz/a.png"),
            QuestionBankKit.Row("Javobsiz", ["a", "b"], ""),
            QuestionBankKit.Row("Kirill harf", ["a", "b", "c"], "С"),
        ]);

        var r = ParseFile("savollar.xlsx", sheet);

        Assert.Equal(AssignmentQuestionImport.UnitRow, r.Unit);
        Assert.Equal(3, r.TotalRows);
        Assert.Equal(2, r.ValidCount);
        Assert.Equal(new[] { 1, 2 }, r.Questions.Select(q => q.CorrectIndex));
        var error = Assert.Single(r.Errors);
        Assert.Equal(AssignmentQuestionReason.NoCorrect, error.Reason);
        Assert.Equal(new[] { 3 }, error.Rows);
    }

    [Fact]
    public void Xlsx_with_more_rows_than_the_question_cap_is_fatal()
    {
        var rows = Enumerable.Range(0, AssignmentService.MaxQuestions + 1)
            .Select(i => QuestionBankKit.Row($"Savol {i}", ["a", "b"], "A"))
            .ToArray();

        var outcome = AssignmentQuestionImport.FromFile("savollar.xlsx", Stream(QuestionBankKit.Sheet(rows)));

        Assert.Equal(AssignmentQuestionImport.TooManyQuestionsMessage(AssignmentService.MaxQuestions + 1), outcome.FatalMessage);
    }

    [Theory]
    [InlineData("s.xlsx", 100L, null)]
    [InlineData("S.DOCX", 100L, null)]
    [InlineData("s.txt", 100L, null)]
    [InlineData("s.json", 100L, null)]
    [InlineData("s.doc", 100L, AssignmentQuestionImport.BadTypeMessage)]
    [InlineData("s.pdf", 100L, AssignmentQuestionImport.BadTypeMessage)]
    [InlineData("s.txt.exe", 100L, AssignmentQuestionImport.BadTypeMessage)]
    [InlineData("s.txt", 0L, AssignmentQuestionImport.NoInputMessage)]
    [InlineData("", 100L, AssignmentQuestionImport.NoInputMessage)]
    [InlineData("s.xlsx", AssignmentQuestionImport.MaxUploadBytes + 1L, AssignmentQuestionImport.TooBigMessage)]
    public void File_acceptance(string fileName, long length, string? expected)
    {
        Assert.Equal(expected, AssignmentQuestionImport.RejectFile(fileName, length));
    }

    // =====================================================================
    //  2. Save rule
    // =====================================================================

    [Fact]
    public void CheckQuestions_drops_blank_options_and_moves_the_key_with_its_text()
    {
        var (questions, problem) = AssignmentService.CheckQuestions("test",
        [
            new QuestionInput("  Savol  ", ["", " bir ", "", "ikki"], 3),
            new QuestionInput("", ["", ""], 0), // an untouched card — skipped, as before
        ]);

        Assert.Null(problem);
        var q = Assert.Single(questions!);
        Assert.Equal("Savol", q.Text);
        Assert.Equal(new[] { "bir", "ikki" }, q.Options);
        Assert.Equal(1, q.CorrectIndex);
    }

    [Theory]
    [InlineData("", new[] { "a", "b" }, 0, "2-savol: savol matnini yozing")]
    [InlineData("Q", new[] { "a", "" }, 0, "2-savol: kamida 2 ta variant bo'lsin")]
    [InlineData("Q", new[] { "1", "2", "3", "4", "5", "6", "7" }, 0, "2-savol: ko'pi bilan 6 ta variant bo'lishi mumkin")]
    [InlineData("Q", new[] { "a", "b" }, 2, "2-savol: to'g'ri javob belgilanmagan yoki bo'sh variantda")]
    [InlineData("Q", new[] { "a", "b" }, -1, "2-savol: to'g'ri javob belgilanmagan yoki bo'sh variantda")]
    [InlineData("Q", new[] { "a", "b", "" }, 2, "2-savol: to'g'ri javob belgilanmagan yoki bo'sh variantda")]
    public void CheckQuestions_names_the_question_that_breaks_a_rule(string text, string[] options, int correct, string message)
    {
        var (questions, problem) = AssignmentService.CheckQuestions("test",
            [new QuestionInput("Yaxshi", ["a", "b"], 0), new QuestionInput(text, [.. options], correct)]);

        Assert.Null(questions);
        Assert.Equal(message, problem);
    }

    [Fact]
    public void CheckQuestions_caps_the_count_and_ignores_questions_of_other_formats()
    {
        var many = Enumerable.Range(0, AssignmentService.MaxQuestions + 1)
            .Select(i => new QuestionInput($"Q{i}", ["a", "b"], 0)).ToList();

        Assert.NotNull(AssignmentService.CheckQuestions("test", many).Problem);
        Assert.Null(AssignmentService.CheckQuestions("test", many.Take(AssignmentService.MaxQuestions).ToList()).Problem);

        var (written, problem) = AssignmentService.CheckQuestions("written", [new QuestionInput("", ["x"], 9)]);
        Assert.Null(problem);
        Assert.Empty(written!);
    }

    [Fact]
    public async Task Admin_create_with_a_broken_question_is_400_and_writes_nothing()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var title = $"Buzuq test {Tag()}";

        var response = await admin.PostAsJsonAsync(Admin, Body(title,
            new { text = "Yaxshi", options = new[] { "a", "b" }, correctIndex = 0 },
            new { text = "Yomon", options = new[] { "a", "b" }, correctIndex = 5 }));

        await AssertMessageAsync(response, "2-savol: to'g'ri javob belgilanmagan yoki bo'sh variantda");
        await fixture.Api.WithDbAsync(async db =>
            Assert.False(await db.Assignments.AnyAsync(a => a.Title == title)));
    }

    [Fact]
    public async Task Admin_create_stores_trimmed_options_without_blanks()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Admin, Body($"Toza test {Tag()}",
            new { text = " Poytaxt? ", options = new[] { "Samarqand", "", " Toshkent " }, correctIndex = 2 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var q = Assert.Single(doc.RootElement.GetProperty("questions").EnumerateArray());
        Assert.Equal("Poytaxt?", q.GetProperty("text").GetString());
        Assert.Equal(new[] { "Samarqand", "Toshkent" }, q.GetProperty("options").EnumerateArray().Select(o => o.GetString()));
        Assert.Equal(1, q.GetProperty("correctIndex").GetInt32());
    }

    [Fact]
    public async Task Admin_update_with_a_broken_question_is_400_and_keeps_the_old_questions()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var title = $"Tahrir test {Tag()}";
        var created = await admin.PostAsJsonAsync(Admin, Body(title,
            new { text = "Eski savol", options = new[] { "a", "b" }, correctIndex = 1 }));
        created.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("id").GetString();

        var response = await admin.PutAsJsonAsync($"{Admin}/{id}", Body(title,
            new { text = "Bitta variant", options = new[] { "a" }, correctIndex = 0 }));

        await AssertMessageAsync(response, "1-savol: kamida 2 ta variant bo'lsin");
        await fixture.Api.WithDbAsync(async db =>
        {
            var q = await db.TestQuestions.SingleAsync(x => x.AssignmentId == id);
            Assert.Equal("Eski savol", q.Text);
        });
    }

    [Fact]
    public async Task Teacher_create_and_update_apply_the_same_rule()
    {
        var (_, teacher) = await TeacherClientAsync(TeacherPermissions.Assignments);

        var bad = await teacher.PostAsJsonAsync(Teacher, Body($"O'qituvchi test {Tag()}",
            new { text = "", options = new[] { "a", "b" }, correctIndex = 0 }));
        await AssertMessageAsync(bad, "1-savol: savol matnini yozing");

        var good = await teacher.PostAsJsonAsync(Teacher, Body($"O'qituvchi test {Tag()}",
            new { text = "Q", options = new[] { "a", "b" }, correctIndex = 0 }));
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
        using var doc = JsonDocument.Parse(await good.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("id").GetString();

        var badUpdate = await teacher.PutAsJsonAsync($"{Teacher}/{id}", Body("Yangi nom",
            new { text = "Q", options = new[] { "1", "2", "3", "4", "5", "6", "7" }, correctIndex = 0 }));
        await AssertMessageAsync(badUpdate, "1-savol: ko'pi bilan 6 ta variant bo'lishi mumkin");
    }

    // =====================================================================
    //  3. Endpoints
    // =====================================================================

    [Fact]
    public async Task Teacher_parses_pasted_text_and_nothing_is_written()
    {
        var (_, teacher) = await TeacherClientAsync(TeacherPermissions.Assignments);
        var before = await CountQuestionsAsync();

        var response = await teacher.PostAsJsonAsync(TeacherParse, new { text = "1. Savol?\nA) bir\n*B) ikki\n\n2. Xato\nA) yolg'iz" });

        using var doc = await OkJsonAsync(response);
        var r = doc.RootElement;
        Assert.Equal("question", r.GetProperty("unit").GetString());
        Assert.Equal(2, r.GetProperty("totalRows").GetInt32());
        Assert.Equal(1, r.GetProperty("validCount").GetInt32());
        Assert.Equal(1, r.GetProperty("errorCount").GetInt32());
        var q = Assert.Single(r.GetProperty("questions").EnumerateArray());
        Assert.Equal("Savol?", q.GetProperty("text").GetString());
        Assert.Equal(1, q.GetProperty("correctIndex").GetInt32());
        var reasons = r.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("reason").GetString()).ToList();
        Assert.Contains(AssignmentQuestionReason.TooFewOptions, reasons);

        Assert.Equal(before, await CountQuestionsAsync());
    }

    [Fact]
    public async Task Teacher_parses_an_uploaded_file()
    {
        var (_, teacher) = await TeacherClientAsync(TeacherPermissions.Assignments);

        var response = await teacher.PostAsync(TeacherParse,
            Form(Encoding.UTF8.GetBytes("[{\"savol\":\"Q?\",\"variantlar\":[\"a\",\"b\"],\"javob\":\"B\"}]"), "savollar.json"));

        using var doc = await OkJsonAsync(response);
        Assert.Equal(1, doc.RootElement.GetProperty("validCount").GetInt32());
    }

    [Fact]
    public async Task Admin_parses_an_excel_file()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var sheet = QuestionBankKit.Sheet([QuestionBankKit.Row("Q?", ["a", "b", "c"], "C")]);

        var response = await admin.PostAsync(AdminParse, Form(sheet, "savollar.xlsx"));

        using var doc = await OkJsonAsync(response);
        Assert.Equal("row", doc.RootElement.GetProperty("unit").GetString());
        Assert.Equal(2, Assert.Single(doc.RootElement.GetProperty("questions").EnumerateArray()).GetProperty("correctIndex").GetInt32());
    }

    [Theory]
    [InlineData("savollar.pdf", AssignmentQuestionImport.BadTypeMessage)]
    [InlineData("savollar.docx", AssignmentQuestionImport.BadDocxMessage)]
    [InlineData("savollar.xlsx", QuestionImportService.BadFileMessage)]
    public async Task Unusable_file_is_400_with_one_sentence(string fileName, string message)
    {
        var (_, teacher) = await TeacherClientAsync(TeacherPermissions.Assignments);

        var response = await teacher.PostAsync(TeacherParse, Form("not a real file"u8.ToArray(), fileName));

        await AssertMessageAsync(response, message);
    }

    [Fact]
    public async Task A_file_over_10_mb_is_400()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsync(AdminParse, Form(new byte[AssignmentQuestionImport.MaxUploadBytes + 1], "katta.txt"));

        await AssertMessageAsync(response, AssignmentQuestionImport.TooBigMessage);
    }

    /// <summary>
    /// A truncated multipart body: MVC's form value provider reads the form before the action
    /// and answers with its own validation 400 — pinned so a change never turns it into a 500.
    /// </summary>
    [Fact]
    public async Task A_broken_multipart_body_is_400_not_500()
    {
        var (_, teacher) = await TeacherClientAsync(TeacherPermissions.Assignments);
        var content = new StringContent("--xyz\r\nContent-Disposition: form-data; name=\"file\"; filename=\"a.txt\"\r\n\r\nkesilgan", Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=xyz");

        var response = await teacher.PostAsync(TeacherParse, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_empty_body_is_400()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await AssertMessageAsync(await admin.PostAsJsonAsync(AdminParse, new { text = "  " }), AssignmentQuestionImport.NoInputMessage);
        await AssertMessageAsync(await admin.PostAsync(AdminParse, new StringContent("salom", Encoding.UTF8, "text/plain")),
            AssignmentQuestionImport.NoInputMessage);
    }

    [Fact]
    public async Task Template_reads_back_as_an_empty_import_with_the_columns_and_no_image()
    {
        var (_, teacher) = await TeacherClientAsync(TeacherPermissions.Assignments);

        var response = await teacher.GetAsync(TeacherTemplate);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(QuestionImportService.XlsxMime, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(AssignmentQuestionImport.TemplateFileName, response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var header = Assert.Single(StudentListTests.XlsxRows(bytes, 9));
        Assert.Equal(QuestionImportService.Headers.Take(8).Append(""), header);

        var r = ParseFile("shablon.xlsx", bytes);
        Assert.Equal(0, r.TotalRows);
    }

    [Fact]
    public async Task Teacher_without_the_assignments_key_is_refused_everywhere()
    {
        var (_, teacher) = await TeacherClientAsync(TeacherPermissions.Journal);

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync(TeacherParse, new { text = "1. Q\nA) a\n*B) b" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(TeacherTemplate)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsync($"{TeacherTemplate}/telegram", null)).StatusCode);
    }

    [Fact]
    public async Task Template_to_telegram_without_a_linked_chat_is_409()
    {
        var (_, teacher) = await TeacherClientAsync(TeacherPermissions.Assignments);

        var response = await teacher.PostAsync($"{TeacherTemplate}/telegram", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("telegram_not_linked", doc.RootElement.GetProperty("code").GetString());
    }

    /// <summary>The admin twin keeps its controller's gate: `app` to post; staff reads stay open.</summary>
    [Fact]
    public async Task Admin_endpoints_keep_the_app_gate()
    {
        const string text = "1. Q\nA) a\n*B) b";

        using var staffWithApp = await fixture.Api.ClientAsAsync(Roles.Staff, "app");
        Assert.Equal(HttpStatusCode.OK, (await staffWithApp.PostAsJsonAsync(AdminParse, new { text })).StatusCode);

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(AdminParse, new { text })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(AdminTemplate)).StatusCode);

        var (_, teacher) = await TeacherClientAsync(TeacherPermissions.Assignments);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync(AdminParse, new { text })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(AdminTemplate)).StatusCode);

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(TeacherParse, new { text })).StatusCode);
    }

    [Theory]
    [InlineData(TeacherParse)]
    [InlineData(AdminParse)]
    public async Task Anonymous_is_401(string url)
    {
        using var client = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(url, new { text = "x" })).StatusCode);
    }

    [Theory]
    [InlineData(TeacherTemplate)]
    [InlineData(AdminTemplate)]
    public async Task Anonymous_template_is_401(string url)
    {
        using var client = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static AssignmentQuestionParseResultDto ParseText(string text) => Ok(AssignmentQuestionImport.FromText(text));

    private static AssignmentQuestionParseResultDto Json(string json) =>
        Ok(AssignmentQuestionImport.FromFile("savollar.json", Stream(Encoding.UTF8.GetBytes(json))));

    private static AssignmentQuestionParseResultDto ParseFile(string fileName, byte[] bytes) =>
        Ok(AssignmentQuestionImport.FromFile(fileName, Stream(bytes)));

    private static AssignmentQuestionParseResultDto Ok(AssignmentQuestionParseOutcome outcome)
    {
        Assert.Null(outcome.FatalMessage);
        return outcome.Result!;
    }

    private static MemoryStream Stream(byte[] bytes) => new(bytes);

    private static Paragraph P(string text) =>
        new(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static Paragraph Listed(string text, int numId) => new(
        new ParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = numId })),
        new Run(new Text(text)));

    private static AbstractNum AbstractList(int id, NumberFormatValues format, string levelText) => new(
        new Level(new StartNumberingValue { Val = 1 }, new NumberingFormat { Val = format }, new LevelText { Val = levelText })
        {
            LevelIndex = 0,
        })
    {
        AbstractNumberId = id,
    };

    private static byte[] Docx(IEnumerable<Paragraph> paragraphs, Numbering? numbering = null)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(paragraphs));
            if (numbering is not null) main.AddNewPart<NumberingDefinitionsPart>().Numbering = numbering;
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static MultipartFormDataContent Form(byte[] bytes, string fileName)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static object Body(string title, params object[] questions) => new
    {
        subjectId = Guid.NewGuid().ToString(),
        title,
        format = "test",
        classIds = new[] { Guid.NewGuid().ToString() },
        lateAccept = false,
        latePenaltyPct = 0,
        maxScore = 10,
        autoGrade = true,
        questions,
    };

    private static async Task AssertMessageAsync(HttpResponseMessage response, string message)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        Assert.True(doc.RootElement.TryGetProperty("message", out var actual), $"no message: {text}");
        Assert.Equal(message, actual.GetString());
    }

    private static async Task<JsonDocument> OkJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text);
    }

    private async Task<int> CountQuestionsAsync()
    {
        var count = 0;
        await fixture.Api.WithDbAsync(async db => count = await db.TestQuestions.CountAsync());
        return count;
    }

    private async Task<(string TeacherId, HttpClient Client)> TeacherClientAsync(params string[] permissions)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var teacherId = "";
        await fixture.Api.WithDbAsync(async db =>
        {
            var t = await db.Teachers.SingleAsync(x => x.UserId == user.Id);
            t.Permissions = [.. permissions];
            await db.SaveChangesAsync();
            teacherId = t.Id;
        });
        return (teacherId, fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email)));
    }

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];
}
