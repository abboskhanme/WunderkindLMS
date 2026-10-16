using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SchoolLms.Application.Dtos;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Vazifalar / Topshiriqlar — bulk import of test questions into the form.
//  docs/modules/test-import.md.
// ===========================================================================
//
//  A PARSER, NOT AN IMPORT
//  -----------------------
//  Nothing is written. The teacher's form receives the questions, shows them,
//  and the teacher saves the assignment exactly as before — so the server's
//  save rules (AssignmentService.CheckQuestions) stay the only gate, and a bad
//  file can never half-write an assignment.
//
//  FOUR INPUTS, ONE RULE SET
//  -------------------------
//  - .xlsx — the question-bank template ("Savollar" sheet), read by
//    QuestionImportService.Read with the image column ignored.
//  - text — what a teacher pastes, or what ChatGPT/Gemini answers to the
//    ready prompt (numbered question, lettered options, `*` on the right one
//    or a "Javob: B" line). Lenient on purpose: see ParseText.
//  - .docx — its paragraphs (with Word's automatic list numbers written out)
//    go through the text parser.
//  - .json — an array of { savol, variantlar, javob } (or text/options/correct).
//  A pasted text that is JSON is read as JSON.
//
//  PARTIAL RESULT IS THE CONTRACT
//  ------------------------------
//  As in the bank import, a question with any error is left out whole and the
//  rest come back. Errors are the reason chips of the bank import, plus
//  `manyCorrect` (two answers marked) which only free text can produce.
// ===========================================================================

/// <summary>
/// The closed list of reasons a question is left out, in chip order. The first
/// five and the last are the bank import's (<see cref="QuestionImportReason"/>);
/// <c>badImageUrl</c> cannot fire here (no images on assignment questions).
/// </summary>
public static class AssignmentQuestionReason
{
    public const string EmptyText = QuestionImportReason.EmptyText;
    public const string TooFewOptions = QuestionImportReason.TooFewOptions;
    public const string TooManyOptions = QuestionImportReason.TooManyOptions;
    public const string NoCorrect = QuestionImportReason.NoCorrect;
    public const string BadCorrect = QuestionImportReason.BadCorrect;

    /// <summary>"Bir nechta to'g'ri javob" — two options marked, or a mark and a "Javob:" line that disagree.</summary>
    public const string ManyCorrect = "manyCorrect";

    public const string DuplicateOption = QuestionImportReason.DuplicateOption;

    public static readonly IReadOnlyList<string> All =
        [EmptyText, TooFewOptions, TooManyOptions, NoCorrect, BadCorrect, ManyCorrect, DuplicateOption];
}

/// <summary>A parse that either produced a result or a single Uzbek sentence (a 400).</summary>
public sealed record AssignmentQuestionParseOutcome(string? FatalMessage, AssignmentQuestionParseResultDto? Result)
{
    public static AssignmentQuestionParseOutcome Fatal(string message) => new(message, null);
}

/// <summary>Template, file checks and the parsers of the assignment question import.</summary>
public static partial class AssignmentQuestionImport
{
    public const string UnitRow = "row";
    public const string UnitQuestion = "question";

    /// <summary>Same 10 MB as the bank and pupil imports.</summary>
    public const int MaxUploadBytes = QuestionImportService.MaxUploadBytes;

    /// <summary>
    /// The endpoint's request ceiling — a little above <see cref="MaxUploadBytes"/> so a
    /// slightly larger file gets the Uzbek sentence below instead of a bare 413.
    /// </summary>
    public const int MaxRequestBytes = MaxUploadBytes + 1024 * 1024;

    /// <summary>Pasted text ceiling — 200 long questions fit many times over.</summary>
    public const int MaxTextLength = 500_000;

    public const string TemplateFileName = "test_savollari_shablon.xlsx";

    public static readonly string[] Extensions = [".xlsx", ".docx", ".txt", ".json"];

    public const string NoInputMessage = "Matn yozilmagan yoki fayl tanlanmagan";
    public const string BadTypeMessage = "Faqat .xlsx, .docx, .txt yoki .json fayl qabul qilinadi";
    public const string TooBigMessage = "Fayl 10 MB dan katta";
    public const string BadUploadMessage = "Faylni yuklab bo'lmadi — qayta urinib ko'ring";
    public const string TextTooLongMessage = "Matn juda uzun — bir martada eng ko'pi 500 000 belgi";
    public const string BadDocxMessage = "Word faylni o'qib bo'lmadi — buzilmagan .docx ekanini tekshiring";
    public const string BadJsonMessage = "JSON faylni o'qib bo'lmadi — tuzilishini tekshiring";
    public const string BadJsonShapeMessage =
        "JSON savollar ro'yxati emas. Kutilgan ko'rinish: [{ \"savol\": \"…\", \"variantlar\": [\"…\", \"…\"], \"javob\": \"B\" }]";

    public static string TooManyQuestionsMessage(int count) =>
        $"Faylda {count} ta savol — bitta testda eng ko'pi {AssignmentService.MaxQuestions} ta";

    // =====================================================================
    //  Entry points
    // =====================================================================

    /// <summary>Is the upload acceptable at all. <c>null</c> = yes. The parser judges the content.</summary>
    public static string? RejectFile(string? fileName, long length)
    {
        if (string.IsNullOrWhiteSpace(fileName) || length <= 0) return NoInputMessage;
        if (length > MaxUploadBytes) return TooBigMessage;
        return Extensions.Any(e => fileName.EndsWith(e, StringComparison.OrdinalIgnoreCase)) ? null : BadTypeMessage;
    }

    /// <summary>A file by its extension (checked by <see cref="RejectFile"/> first). Never throws on a bad file.</summary>
    public static AssignmentQuestionParseOutcome FromFile(string fileName, Stream stream)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        switch (ext)
        {
            case ".xlsx":
                return FromXlsx(stream);
            case ".docx":
                string? text;
                try
                {
                    text = DocxText(stream);
                }
                catch (Exception)
                {
                    // OpenXML throws a handful of unrelated types for a broken file.
                    return AssignmentQuestionParseOutcome.Fatal(BadDocxMessage);
                }
                return ParseText(text);
            case ".json":
                return ParseJson(ReadAllText(stream)) ?? AssignmentQuestionParseOutcome.Fatal(BadJsonMessage);
            case ".txt":
                return FromText(ReadAllText(stream));
            default:
                return AssignmentQuestionParseOutcome.Fatal(BadTypeMessage);
        }
    }

    /// <summary>
    /// Pasted text. JSON (an array or an object, optionally inside a ``` fence) is read as
    /// JSON; anything else — or JSON that does not parse — goes to the text parser.
    /// </summary>
    public static AssignmentQuestionParseOutcome FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return AssignmentQuestionParseOutcome.Fatal(NoInputMessage);
        if (text.Length > MaxTextLength) return AssignmentQuestionParseOutcome.Fatal(TextTooLongMessage);

        var json = Unfence(text.Trim());
        if (json.StartsWith('[') || json.StartsWith('{'))
        {
            if (ParseJson(json) is { } parsed) return parsed;
        }
        return ParseText(text);
    }

    /// <summary>
    /// <c>test_savollari_shablon.xlsx</c>: sheet "Savollar" with the bank columns minus the
    /// image (headers only — a sample row would be imported by whoever forgets to delete
    /// it), and a rules sheet written for the assignment form.
    /// </summary>
    public static byte[] Template()
    {
        var headers = QuestionImportService.Headers.Take(8).ToArray();
        var guide = new List<IReadOnlyList<string>>
        {
            new[] { headers[0], "Majburiy. Bir qator — bitta savol." },
            new[] { "A … F", "Javob variantlari: kamida 2 ta, ko'pi bilan 6 ta. Bo'sh katak — variant yo'q." },
            new[] { headers[7], "Majburiy. To'g'ri variantning harfi, masalan: B." },
            new[] { "", "" },
            new[] { "Bir xil variant", "Bitta savolda ikki variant bir xil bo'lmasin." },
            new[] { "Oradagi bo'sh katak", "Variantlar ketma-ket qayta harflanadi, to'g'ri javob o'zgarmaydi." },
            new[] { "Xato qator", "O'tkazib yuboriladi; qolgan to'g'ri savollar formaga qo'shiladi." },
            new[] { "Saqlash", "Fayl faqat o'qiladi — savollar formaga tushadi, topshiriq «Saqlash» bosilganda saqlanadi." },
            new[] { "Ko'pi bilan", $"Bitta testda {AssignmentService.MaxQuestions} ta savol." },
            new[] { "", "" },
            new[] { "Matn ko'rinishi", "Word (.docx), .txt yoki «Ommaviy yuklash» oynasiga matn ham bo'ladi:" },
            new[] { "", "1. Savol matni" },
            new[] { "", "A) variant" },
            new[] { "", "*B) to'g'ri variant (oldiga * qo'ying yoki pastiga «Javob: B» deb yozing)" },
            new[] { "", "C) variant" },
            new[] { "", "D) variant" },
            new[] { "", "Savollar orasida bo'sh qator qoldiring yoki raqamlang." },
        };

        return ExcelExport.Build(
        [
            new ExcelExport.SheetSpec(QuestionImportService.SheetName, headers, []),
            new ExcelExport.SheetSpec(QuestionImportService.GuideSheetName, ["Maydon", "Izoh"], guide),
        ]);
    }

    // =====================================================================
    //  Excel
    // =====================================================================

    private static AssignmentQuestionParseOutcome FromXlsx(Stream stream)
    {
        var plan = QuestionImportService.Read(stream, ignoreImage: true);
        if (plan.FatalMessage is { } fatal) return AssignmentQuestionParseOutcome.Fatal(fatal);
        if (plan.TotalRows > AssignmentService.MaxQuestions)
            return AssignmentQuestionParseOutcome.Fatal(TooManyQuestionsMessage(plan.TotalRows));

        var questions = plan.Valid
            .Select(v => new QuestionInput(
                v.Question.Text,
                v.Question.Options.Select(o => o.Text).ToList(),
                v.Question.Options.ToList().FindIndex(o => o.IsCorrect)))
            .ToList();
        var bankErrors = plan.ToResult("", imported: 0).Errors;
        var errors = AssignmentQuestionReason.All
            .Select(reason => bankErrors.FirstOrDefault(e => e.Reason == reason))
            .OfType<QuestionImportErrorDto>()
            .ToList();

        return new AssignmentQuestionParseOutcome(null, new AssignmentQuestionParseResultDto(
            UnitRow, plan.TotalRows, questions.Count, plan.ErrorCount, questions, errors));
    }

    // =====================================================================
    //  Text
    // =====================================================================

    /// <summary>One option as read: its letter (null when JSON gave none), text and mark.</summary>
    private sealed record RawOption(char? Label, string Text, bool Marked);

    /// <summary>A question block of the text parser, before the rules.</summary>
    private sealed class Block
    {
        public bool Numbered;
        public readonly List<string> TextLines = [];
        public readonly List<(char Label, bool Marked, List<string> Lines)> Options = [];
        public readonly List<string> Answers = [];

        /// <summary>Lines after the "Javob:" line — an explanation, or the next question's text.</summary>
        public readonly List<string> AfterAnswer = [];

        /// <summary>A blank line was seen while the block had only text.</summary>
        public bool Gap;

        public bool HasContent => Numbered || TextLines.Count > 0 || Options.Count > 0 || Answers.Count > 0;
        public bool IsQuestionLike => Numbered || Options.Count > 0 || Answers.Count > 0;
    }

    /// <summary>
    /// The lenient text format:
    /// <code>
    /// 1. Savol matni
    /// A) variant
    /// *B) to'g'ri variant      (or a line "Javob: B")
    /// C) variant
    /// </code>
    /// <list type="bullet">
    ///   <item>Questions: "1." "1)" "1-savol" "Savol 1:" start one; so does the first plain line after a blank line.
    ///     Unnumbered text before the first and after the last question (an AI's "Mana savollar:" / "Omad!") is dropped.</item>
    ///   <item>Options: "A)" "A." "a)", Latin A–H and Cyrillic А–Ж; their position is their order, the letter only
    ///     resolves "Javob:". A new "A)" after options starts the next question even without a blank line.</item>
    ///   <item>Correct: <c>*</c> or <c>+</c> before the letter, <c>*</c> right after it or at the end, ✓/✅, a wholly
    ///     bold option, or a "Javob: B" / "To'g'ri javob: B" / "Ответ: Б" line (a letter, the option's text, or its
    ///     1-based number).</item>
    ///   <item>A multi-line question is joined with spaces (the form's inputs are single-line); an option's
    ///     continuation lines join the option; lines after "Javob:" are an explanation and are dropped.</item>
    ///   <item>Markdown the AI may add anyway — ``` fences, # headings, **bold**, "- " bullets — is stripped.</item>
    /// </list>
    /// </summary>
    private static AssignmentQuestionParseOutcome ParseText(string text)
    {
        var blocks = new List<Block>();
        Block? current = null;

        void Close()
        {
            if (current is { HasContent: true }) blocks.Add(current);
            current = null;
        }

        foreach (var rawLine in Normalize(text).Split('\n'))
        {
            var (line, wholeBold) = CleanLine(rawLine);

            if (line.Length == 0)
            {
                if (current is null) continue;
                if (current.Options.Count > 0 || current.Answers.Count > 0) Close();
                else if (current.TextLines.Count > 0) current.Gap = true;
                continue;
            }

            if (NumberLine().Match(line) is { Success: true } number)
            {
                Close();
                current = new Block { Numbered = true };
                var rest = number.Groups["rest"].Value.Trim();
                if (rest.Length > 0) current.TextLines.Add(rest);
                continue;
            }

            if (AnswerLine().Match(line) is { Success: true } answer)
            {
                var value = answer.Groups["value"].Value.Trim();
                // A key on its own after a blank line belongs to the question just closed.
                var target = current ?? (blocks.Count > 0 && blocks[^1].Options.Count > 0 ? blocks[^1] : null);
                if (target is null)
                {
                    current = new Block();
                    target = current;
                }
                target.Answers.Add(value);
                continue;
            }

            if (OptionLine().Match(line) is { Success: true } option
                && current is not null
                && (current.TextLines.Count > 0 || current.Numbered || current.Options.Count > 0))
            {
                var label = option.Groups["label"].Value[0];
                var (optionText, marked) = OptionText(option.Groups["text"].Value, option.Groups["mark"].Success || wholeBold);

                if (IsFirstLetter(label) && current.Options.Count > 0)
                {
                    // "A)" again — the next question started without a blank line. Its text is
                    // what was read after the answer line or, with no answer line, the lines
                    // that were taken for the last option's continuation.
                    List<string> carried;
                    if (current.Answers.Count > 0)
                        carried = current.AfterAnswer.Where(l => !ExplanationLine().IsMatch(l)).ToList();
                    else
                    {
                        var lines = current.Options[^1].Lines;
                        carried = lines.Skip(1).ToList();
                        if (carried.Count > 0) lines.RemoveRange(1, carried.Count);
                    }
                    Close();
                    current = new Block();
                    current.TextLines.AddRange(carried);
                }

                current.Options.Add((label, marked, [optionText]));
                current.Gap = false;
                continue;
            }

            // A plain line.
            if (current is null)
            {
                current = new Block();
                current.TextLines.Add(line);
            }
            else if (current.Answers.Count > 0)
            {
                current.AfterAnswer.Add(line);
            }
            else if (current.Options.Count > 0)
            {
                current.Options[^1].Lines.Add(line);
            }
            else if (current.Gap)
            {
                // Text, a blank line, more text — the first part was not a question.
                Close();
                current = new Block();
                current.TextLines.Add(line);
            }
            else
            {
                current.TextLines.Add(line);
            }
        }
        Close();

        // Unnumbered text-only blocks at either end are an AI's preamble or sign-off.
        var first = blocks.FindIndex(b => b.IsQuestionLike);
        var last = blocks.FindLastIndex(b => b.IsQuestionLike);
        var questions = first < 0 ? [] : blocks.GetRange(first, last - first + 1);
        if (questions.Count > AssignmentService.MaxQuestions)
            return AssignmentQuestionParseOutcome.Fatal(TooManyQuestionsMessage(questions.Count));

        var collector = new Collector();
        for (var i = 0; i < questions.Count; i++)
        {
            var b = questions[i];
            var options = b.Options
                .Select(o => new RawOption(o.Label, string.Join(" ", o.Lines).Trim(), o.Marked))
                .ToList();
            var answers = b.Answers.Select(a => ResolveTextAnswer(a, options)).ToList();
            collector.Add(i + 1, Check(string.Join(" ", b.TextLines), options, answers));
        }
        return collector.ToOutcome(UnitQuestion);
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Replace("﻿", "").Replace("​", "").Replace(' ', ' ');

    /// <summary>
    /// Trims and strips what an AI adds despite being asked not to. <c>WholeBold</c> — the
    /// whole line was <c>**…**</c>: on an option line that is how a chat model marks the answer.
    /// </summary>
    private static (string Line, bool WholeBold) CleanLine(string raw)
    {
        var line = raw.Trim();
        if (line.StartsWith("```", StringComparison.Ordinal)) return ("", false);
        line = line.TrimStart('#').Trim();
        if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("• ", StringComparison.Ordinal))
            line = line[2..].Trim();

        var wholeBold = false;
        if (line.Length > 4 && line.StartsWith("**", StringComparison.Ordinal) && line.EndsWith("**", StringComparison.Ordinal)
            && line.IndexOf("**", 2, StringComparison.Ordinal) == line.Length - 2)
        {
            wholeBold = true;
            line = line[2..^2].Trim();
        }
        else if (line.StartsWith("**", StringComparison.Ordinal))
        {
            // "**B)** Toshkent" / "**Javob:** B" — the first bold pair only.
            line = line[2..];
            var close = line.IndexOf("**", StringComparison.Ordinal);
            if (close >= 0) line = line.Remove(close, 2);
            line = line.Trim();
        }
        return (line, wholeBold);
    }

    /// <summary>The option text and whether it carries a correct mark (after the letter or at the end).</summary>
    private static (string Text, bool Marked) OptionText(string raw, bool markedBefore)
    {
        var text = raw.Trim();
        var marked = markedBefore;
        if (text.Length > 4 && text.StartsWith("**", StringComparison.Ordinal) && text.EndsWith("**", StringComparison.Ordinal))
        {
            // "B) **Toshkent**" — the bold option among plain ones.
            marked = true;
            text = text[2..^2].Trim();
        }
        if (text.StartsWith('*'))
        {
            marked = true;
            text = text.TrimStart('*').Trim();
        }
        foreach (var tick in Ticks)
        {
            if (text.StartsWith(tick, StringComparison.Ordinal)) { marked = true; text = text[tick.Length..].Trim(); }
            if (text.EndsWith(tick, StringComparison.Ordinal)) { marked = true; text = text[..^tick.Length].Trim(); }
        }
        if (text.EndsWith("(*)", StringComparison.Ordinal)) { marked = true; text = text[..^3].Trim(); }
        else if (text.Length > 1 && text.EndsWith('*') && !text.EndsWith("**", StringComparison.Ordinal))
        {
            marked = true;
            text = text[..^1].Trim();
        }
        return (text, marked);
    }

    private static readonly string[] Ticks = ["✅", "✔️", "✔", "✓"];

    private static bool IsFirstLetter(char label) => label is 'A' or 'a' or 'А' or 'а';

    /// <summary>A "Javob:" value as an option index; -1 when it names no option.</summary>
    private static int ResolveTextAnswer(string value, IReadOnlyList<RawOption> options)
    {
        var v = value.Trim().TrimEnd('.', ',', ';', '!').Trim();
        if (v.Length == 0) return -1;

        // "B", "b)", "B) Toshkent", "B. Toshkent"
        if (char.IsLetter(v[0]) && (v.Length == 1 || v[1] is ')' or '.' or ' ' or ':' or ','))
        {
            var byLetter = IndexOfLabel(v[0], options);
            if (byLetter >= 0) return byLetter;
        }
        // "Toshkent" — the option's own text.
        var byText = IndexOfText(v, options);
        if (byText >= 0) return byText;
        // "2" — the second option, as a person counts.
        if (int.TryParse(v, out var n) && n >= 1 && n <= options.Count) return n - 1;
        return -1;
    }

    // =====================================================================
    //  Word
    // =====================================================================

    /// <summary>
    /// The document's paragraphs as lines (tables included, in reading order). Word's
    /// automatic list numbers are not text, so they are written out first: a decimal list
    /// gives "1. ", a letter list "A) " — a test typed as two Word lists reads like the text format.
    /// </summary>
    private static string DocxText(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var main = doc.MainDocumentPart;
        var body = main?.Document?.Body;
        if (body is null) return "";

        var numbering = new WordNumbering(main!.NumberingDefinitionsPart?.Numbering);
        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            sb.Append(numbering.LabelFor(paragraph));
            foreach (var element in paragraph.Descendants())
            {
                // A text box inside a run holds paragraphs of its own — they come in their own turn.
                if (element.Ancestors<Paragraph>().FirstOrDefault() != paragraph) continue;
                switch (element)
                {
                    case Text t: sb.Append(t.Text); break;
                    case TabChar: sb.Append(' '); break;
                    case Break or CarriageReturn: sb.Append('\n'); break;
                }
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Counts Word list items per (list, level) and renders their visible label.</summary>
    private sealed class WordNumbering(Numbering? numbering)
    {
        private readonly Dictionary<(int Num, int Level), int> _counters = [];

        public string LabelFor(Paragraph paragraph)
        {
            try
            {
                var numPr = paragraph.ParagraphProperties?.NumberingProperties;
                var numId = numPr?.NumberingId?.Val?.Value;
                if (numbering is null || numId is null or 0) return "";
                var level = numPr!.NumberingLevelReference?.Val?.Value ?? 0;

                var instance = numbering.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
                var abstractId = instance?.AbstractNumId?.Val?.Value;
                var definition = numbering.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId);
                var lvl = definition?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == level);
                if (lvl is null) return "";

                // A deeper level restarts whenever a shallower one moves on.
                foreach (var key in _counters.Keys.Where(k => k.Num == numId && k.Level > level).ToList())
                    _counters.Remove(key);
                var count = _counters.GetValueOrDefault((numId.Value, level)) + 1;
                _counters[(numId.Value, level)] = count;
                var n = (lvl.StartNumberingValue?.Val?.Value ?? 1) + count - 1;

                var format = lvl.NumberingFormat?.Val?.Value;
                if (format == NumberFormatValues.Decimal) return $"{n}. ";
                if (n < 1 || n > 26) return "";
                if (format == NumberFormatValues.UpperLetter) return $"{(char)('A' + n - 1)}) ";
                if (format == NumberFormatValues.LowerLetter) return $"{(char)('a' + n - 1)}) ";
                if (n <= CyrillicLetters.Length)
                {
                    if (format == NumberFormatValues.RussianUpper) return $"{CyrillicLetters[n - 1]}) ";
                    if (format == NumberFormatValues.RussianLower) return $"{char.ToLowerInvariant(CyrillicLetters[n - 1])}) ";
                }
                return "";
            }
            catch (Exception)
            {
                // An odd numbering part costs the labels, never the document.
                return "";
            }
        }
    }

    private const string CyrillicLetters = "АБВГДЕЖ";

    // =====================================================================
    //  JSON
    // =====================================================================

    private static readonly HashSet<string> TextKeys = ["savol", "text", "question", "matn", "savolmatni", "questiontext"];
    private static readonly HashSet<string> OptionKeys =
        ["variantlar", "options", "choices", "variants", "javobvariantlari", "answers", "javoblar"];
    private static readonly HashSet<string> CorrectKeys =
        ["javob", "togrijavob", "correct", "correctanswer", "answer", "correctindex", "togri", "rightanswer"];
    private static readonly HashSet<string> OptionCorrectKeys = ["iscorrect", "correct", "togri", "right"];
    private static readonly HashSet<string> ListKeys = ["questions", "savollar", "testlar", "tests", "items", "data"];

    /// <summary>
    /// <c>[{ "savol": "…", "variantlar": ["…","…"], "javob": "B" | 1 }]</c> — also
    /// <c>text/question</c>, <c>options/choices</c>, <c>correct/answer/correctIndex</c>; the array may sit
    /// under <c>questions</c>/<c>savollar</c>; options may be <c>{ "A": "…" }</c> or
    /// <c>[{ "text": "…", "isCorrect": true }]</c>. A number answer is the 0-based index
    /// (as <c>correctIndex</c> everywhere in this API); a string is a letter or the option's text.
    /// <c>null</c> = not JSON at all (the caller decides whether that is fatal).
    /// </summary>
    private static AssignmentQuestionParseOutcome? ParseJson(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 16,
            });
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            List<JsonElement> items;
            if (root.ValueKind == JsonValueKind.Array) items = [.. root.EnumerateArray()];
            else if (root.ValueKind == JsonValueKind.Object && Property(root, ListKeys, JsonValueKind.Array) is { } list)
                items = [.. list.EnumerateArray()];
            else if (root.ValueKind == JsonValueKind.Object && Property(root, TextKeys, null) is not null)
                items = [root];
            else return AssignmentQuestionParseOutcome.Fatal(BadJsonShapeMessage);

            if (items.Count > AssignmentService.MaxQuestions)
                return AssignmentQuestionParseOutcome.Fatal(TooManyQuestionsMessage(items.Count));

            var collector = new Collector();
            for (var i = 0; i < items.Count; i++)
                collector.Add(i + 1, JsonQuestion(items[i]));
            return collector.ToOutcome(UnitQuestion);
        }
    }

    private static (QuestionInput? Question, List<string> Reasons) JsonQuestion(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return Check("", [], []);

        var text = Property(item, TextKeys, null) is { } t ? Scalar(t) : "";

        var options = new List<RawOption>();
        if (Property(item, OptionKeys, JsonValueKind.Array) is { } array)
        {
            foreach (var o in array.EnumerateArray())
            {
                if (o.ValueKind == JsonValueKind.Object)
                {
                    var optionText = Property(o, TextKeys, null) is { } ot ? Scalar(ot) : "";
                    var marked = Property(o, OptionCorrectKeys, null) is { ValueKind: JsonValueKind.True };
                    options.Add(new RawOption(null, optionText, marked));
                }
                else options.Add(new RawOption(null, Scalar(o), false));
            }
        }
        else if (Property(item, OptionKeys, JsonValueKind.Object) is { } map)
        {
            foreach (var p in map.EnumerateObject())
            {
                var key = p.Name.Trim().TrimEnd(')', '.');
                options.Add(new RawOption(key.Length == 1 ? key[0] : null, Scalar(p.Value), false));
            }
        }

        var answers = new List<int>();
        var answer = Property(item, CorrectKeys, null);
        if (answer is { ValueKind: JsonValueKind.Number })
            answers.Add(answer.Value.TryGetInt32(out var index) && index >= 0 && index < options.Count ? index : -1);
        else if (answer is { ValueKind: JsonValueKind.String })
        {
            var v = answer.Value.GetString()!.Trim().TrimEnd(')', '.').Trim();
            if (v.Length > 0)
            {
                var resolved = v.Length == 1 && char.IsLetter(v[0]) ? IndexOfLabel(v[0], options) : -1;
                if (resolved < 0) resolved = IndexOfText(v, options);
                if (resolved < 0 && int.TryParse(v, out var n)) resolved = n >= 0 && n < options.Count ? n : -1;
                answers.Add(resolved);
            }
        }

        return Check(text, options, answers);
    }

    /// <summary>The first property whose name (letters and digits, lower case) is in <paramref name="keys"/>.</summary>
    private static JsonElement? Property(JsonElement obj, HashSet<string> keys, JsonValueKind? kind)
    {
        foreach (var p in obj.EnumerateObject())
        {
            var name = new string(p.Name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (keys.Contains(name) && (kind is null || p.Value.ValueKind == kind)) return p.Value;
        }
        return null;
    }

    /// <summary>A string as is; a number or boolean as written ("2", "3.5"); anything else blank.</summary>
    private static string Scalar(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => e.GetRawText(),
        _ => "",
    };

    // =====================================================================
    //  The rules — one place for text, Word and JSON
    // =====================================================================

    /// <summary>
    /// Every rule for one question. <paramref name="answers"/> are the resolved "Javob"
    /// values (-1 = names no option); marks come with the options. Blank options are
    /// dropped (a mark on one is lost with it), as the save rule does.
    /// </summary>
    private static (QuestionInput? Question, List<string> Reasons) Check(
        string text, IReadOnlyList<RawOption> rawOptions, IReadOnlyList<int> answers)
    {
        var reasons = new List<string>();
        var cleanText = text.Trim();
        if (cleanText.Length == 0) reasons.Add(AssignmentQuestionReason.EmptyText);

        // Indexes move when blanks go: map the original position to the kept one.
        var kept = new List<RawOption>();
        var keptIndex = new int[rawOptions.Count];
        for (var i = 0; i < rawOptions.Count; i++)
        {
            var o = rawOptions[i] with { Text = rawOptions[i].Text.Trim() };
            keptIndex[i] = o.Text.Length == 0 ? -1 : kept.Count;
            if (o.Text.Length > 0) kept.Add(o);
        }

        if (kept.Count < AssignmentService.MinOptions) reasons.Add(AssignmentQuestionReason.TooFewOptions);
        if (kept.Count > AssignmentService.MaxOptions) reasons.Add(AssignmentQuestionReason.TooManyOptions);

        var correct = new HashSet<int>();
        foreach (var (o, i) in kept.Select((o, i) => (o, i)))
            if (o.Marked) correct.Add(i);
        var badAnswer = false;
        foreach (var a in answers)
        {
            var k = a >= 0 && a < keptIndex.Length ? keptIndex[a] : -1;
            if (k < 0) badAnswer = true;
            else correct.Add(k);
        }

        if (correct.Count == 0 && !badAnswer) reasons.Add(AssignmentQuestionReason.NoCorrect);
        else if (badAnswer) reasons.Add(AssignmentQuestionReason.BadCorrect);
        else if (correct.Count > 1) reasons.Add(AssignmentQuestionReason.ManyCorrect);

        if (kept.Select(o => o.Text).Distinct(StringComparer.Ordinal).Count() != kept.Count)
            reasons.Add(AssignmentQuestionReason.DuplicateOption);

        if (reasons.Count > 0) return (null, reasons);
        return (new QuestionInput(cleanText, kept.Select(o => o.Text).ToList(), correct.Single()), reasons);
    }

    /// <summary>
    /// A letter to an option: the option written with that letter (case-insensitive) first; then
    /// through the Latin/Cyrillic look-alikes (А В С Е typed on a Russian layout); options without
    /// letters (JSON arrays) count A–F from the top. -1 = no such option.
    /// </summary>
    private static int IndexOfLabel(char letter, IReadOnlyList<RawOption> options)
    {
        var upper = char.ToUpperInvariant(letter);
        for (var i = 0; i < options.Count; i++)
            if (options[i].Label is { } l && char.ToUpperInvariant(l) == upper) return i;

        var latin = LatinTwin(upper);
        for (var i = 0; i < options.Count; i++)
            if (options[i].Label is { } l && LatinTwin(char.ToUpperInvariant(l)) == latin) return i;

        if (options.Count > 0 && options.All(o => o.Label is null))
        {
            var index = QuestionBankService.OptionLetters.IndexOf(latin);
            return index >= 0 && index < options.Count ? index : -1;
        }
        return -1;
    }

    private static char LatinTwin(char upper) => upper switch
    {
        'А' => 'A', // Cyrillic А
        'В' => 'B', // Cyrillic В
        'С' => 'C', // Cyrillic С
        'Е' => 'E', // Cyrillic Е
        var other => other,
    };

    private static int IndexOfText(string value, IReadOnlyList<RawOption> options)
    {
        for (var i = 0; i < options.Count; i++)
            if (string.Equals(options[i].Text.Trim(), value, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    /// <summary>Questions and reason chips as they are checked; numbers are 1-based.</summary>
    private sealed class Collector
    {
        private readonly Dictionary<string, List<int>> _rowsByReason = new(StringComparer.Ordinal);
        private readonly List<QuestionInput> _valid = [];
        private int _total;
        private int _errors;

        public void Add(int number, (QuestionInput? Question, List<string> Reasons) checkedQuestion)
        {
            _total++;
            if (checkedQuestion.Question is { } q)
            {
                _valid.Add(q);
                return;
            }
            _errors++;
            foreach (var reason in checkedQuestion.Reasons)
            {
                if (!_rowsByReason.TryGetValue(reason, out var rows)) _rowsByReason[reason] = rows = [];
                rows.Add(number);
            }
        }

        public AssignmentQuestionParseOutcome ToOutcome(string unit) => new(null, new AssignmentQuestionParseResultDto(
            unit, _total, _valid.Count, _errors, _valid,
            AssignmentQuestionReason.All
                .Where(_rowsByReason.ContainsKey)
                .Select(reason => new QuestionImportErrorDto(reason, _rowsByReason[reason]))
                .ToList()));
    }

    private static string ReadAllText(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>"```json\n[…]\n```" → "[…]"; anything else unchanged.</summary>
    private static string Unfence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal)) return text;
        var start = text.IndexOf('\n');
        var end = text.LastIndexOf("```", StringComparison.Ordinal);
        return start < 0 || end <= start ? text : text[(start + 1)..end].Trim();
    }

    /// <summary>"1." "1)" "1-savol" "1-savol." "Savol 1:" — a question number; "1.5" is not one.</summary>
    [GeneratedRegex(@"^(?:(?<n>\d{1,3})\s*(?:[.)]|-\s*savol\s*[.:)]?)(?!\d)|savol\s*№?\s*(?<n>\d{1,3})\s*[.:)\-])\s*(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumberLine();

    /// <summary>"A) …" "b. …" "*C) …" "+ D) …" "Б) …" — Latin A–H, Cyrillic А–Ж (G/H/Ж exist only to be counted as too many).</summary>
    [GeneratedRegex(@"^(?<mark>[*+]\s*)?(?<label>[A-Ha-hА-Жа-ж])\s*[).](?<text>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex OptionLine();

    /// <summary>"Javob: B", "To'g'ri javob - B", "Javobi: Toshkent", "Answer: b", "Ответ: Б", "Жавоб: В".</summary>
    [GeneratedRegex(
        @"^(?:to['‘’ʻʼ`]?g['‘’ʻʼ`]?ri\s+|тўғри\s+|правильный\s+|correct\s+)?(?:javobi?|answer|ответ|жавоби?)\s*[:\-–—=]\s*(?<value>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnswerLine();

    /// <summary>"Izoh: …", "Tushuntirish - …", "Explanation: …" — after an answer line, never the next question.</summary>
    [GeneratedRegex(@"^(?:izohi?|tushuntirish|sababi?|explanation|изоҳи?|пояснение|объяснение)\s*[:\-–—]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplanationLine();
}
