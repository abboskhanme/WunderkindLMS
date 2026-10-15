using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  So'rovnomalar va NPS (client, 2026-10-07; Polls.cs). The admin builds a poll for parents or
//  teachers, opens it, and may announce it once through the bot; parents answer per child in the
//  Mini App (and rate each of the child's teachers), teachers answer in theirs (and rate each
//  member of the management). Results: NPS, averages, distributions — by class and by person.
//
//  Google Forms parity (client, 2026-10-15; docs/modules/surveys-forms.md): checkboxes, dropdown,
//  linear scale, number, date, short text and sections; the public link `/sorovnoma/{token}` for
//  the `public` audience (and for any poll with "Havola orqali ham ochiq"); one row per response
//  in the "Javoblar" table and its Excel export.
// ===========================================================================

public record PollQuestionDto(
    Guid Id, int Position, string Kind, string Text, List<string> Options, bool Required, string Target,
    string? Description, bool AllowOther, decimal? MinValue, decimal? MaxValue, string? MinLabel, string? MaxLabel,
    int? MinChoices, int? MaxChoices);

/// <param name="PublicToken">Only while the public link is open (public audience or <c>PublicLink</c>).</param>
public record PollDto(
    Guid Id, string Title, string? Description, string Audience, bool Anonymous, string Status, string? ClosesOn,
    DateTimeOffset CreatedAt, DateTimeOffset? OpenedAt, DateTimeOffset? ClosedAt, DateTimeOffset? NotifiedAt,
    int Responses, List<PollQuestionDto> Questions, bool PublicLink, bool CollectContact, string? PublicToken);

/// <summary>A question as the editor sends it; settings that do not apply to the kind are dropped.</summary>
public record PollQuestionInput(
    string Kind, string Text, List<string>? Options, bool Required, string? Target,
    string? Description = null, bool AllowOther = false, decimal? MinValue = null, decimal? MaxValue = null,
    string? MinLabel = null, string? MaxLabel = null, int? MinChoices = null, int? MaxChoices = null);

public record PollInput(
    string Title, string? Description, string Audience, bool Anonymous, string? ClosesOn, List<PollQuestionInput> Questions,
    bool PublicLink = false, bool CollectContact = false);

public record PollCountDto(string Label, int Count);

public record PollSubjectResultDto(string Id, string Name, int Answers, double? Average, int? Nps);

public record PollTextAnswerDto(string Text, string? SubjectName, string? ClassName, string? RespondentName, DateTimeOffset At);

/// <param name="Min">Number questions: the smallest answer (with <c>Max</c> and <c>Average</c>).</param>
public record PollQuestionResultDto(
    Guid QuestionId, string Kind, string Text, string Target, int Answers, double? Average, int? Nps,
    int Promoters, int Passives, int Detractors, List<PollCountDto> Distribution,
    List<PollSubjectResultDto> Subjects, List<PollTextAnswerDto> Texts,
    string? Description, double? Min, double? Max);

public record PollResultsDto(
    Guid PollId, string Title, string Audience, bool Anonymous, int Responses, string? ClassName,
    List<string> Classes, List<PollQuestionResultDto> Questions);

/// <summary>A person a portal question rates: a teacher of the child, or a manager.</summary>
public record PollSubjectDto(string Id, string Name, string? Note);

public record PortalPollDto(
    Guid Id, string Title, string? Description, bool Anonymous, string? ClosesOn, bool Answered,
    List<PollQuestionDto> Questions, List<PollSubjectDto> Teachers, List<PollSubjectDto> Managers);

/// <summary>
/// One answer: <c>Score</c> (nps, stars, scale), <c>Choice</c> (choice, dropdown), <c>Choices</c> (multi),
/// <c>Text</c> (text, short — and the "Boshqa" text of choice/multi), <c>Number</c>, <c>Date</c> (yyyy-MM-dd).
/// </summary>
public record PollAnswerInput(
    Guid QuestionId, string? SubjectId, int? Score, string? Choice, string? Text,
    List<string>? Choices = null, decimal? Number = null, string? Date = null);

public record PollSubmitRequest(List<PollAnswerInput> Answers);

/// <summary>Who answers: a child (parents answer per child) or a teacher.</summary>
public sealed record PollRespondent(string Audience, string RawKey, string? StudentId, string? TeacherId, string? ClassName);

/// <summary>
/// What the public page renders. A closed or not yet opened poll comes with <c>Accepting = false</c>
/// and no questions. Per-person questions (each teacher / manager) are Mini App only and left out.
/// </summary>
public record PublicPollDto(
    string Title, string? Description, bool Accepting, bool Anonymous, bool CollectContact, string? ClosesOn,
    List<PollQuestionDto> Questions);

/// <param name="RespondentToken">A random id the browser keeps in localStorage — one answer per browser.</param>
public record PublicPollSubmitRequest(
    List<PollAnswerInput>? Answers, string? RespondentToken, string? ContactName, string? ContactPhone);

/// <summary>A column of the "Javoblar" table — every question except sections.</summary>
public record PollResponseColumnDto(Guid QuestionId, string Kind, string Text);

/// <param name="Respondent">Null on anonymous polls.</param>
/// <param name="Cells">One per column, formatted as in the Excel export.</param>
public record PollResponseRowDto(
    Guid Id, DateTimeOffset SubmittedAt, string? Respondent, string? Phone, string? ClassName, bool ViaLink, List<string> Cells);

/// <param name="ShowRespondent">Not anonymous: the "Kim" column.</param>
/// <param name="ShowPhone">Some response carries a phone typed on the public page.</param>
/// <param name="ShowSource">A parents/teachers poll that has link answers too: the "Manba" column.</param>
public record PollResponsesDto(
    Guid PollId, string Title, bool ShowRespondent, bool ShowPhone, bool ShowSource, int Total, int Page, int PageSize,
    List<string> Classes, List<PollResponseColumnDto> Columns, List<PollResponseRowDto> Rows);

public static class PollService
{
    public const string Permission = "polls";
    /// <summary>Sections count — they are blocks of the same form.</summary>
    public const int MaxQuestions = 50;
    public const int MaxOptions = 20;
    /// <summary>A dropdown is for long lists (classes, districts).</summary>
    public const int MaxDropdownOptions = 50;
    public const int MaxText = 2000;
    public const int MaxShortText = 500;
    /// <summary>The "Boshqa: ___" text of a choice / multi answer.</summary>
    public const int MaxOtherText = 500;
    /// <summary>numeric(18,4) holds 14 integer digits; a survey never needs more than this.</summary>
    public const decimal MaxNumber = 1_000_000_000_000m;
    public const int MaxPageSize = 100;
    public const string OtherLabel = "Boshqa";
    public const string NotFoundMessage = "So'rovnoma topilmadi";
    public const string ClosedMessage = "So'rovnoma yopilgan";
    public const string AnsweredMessage = "Siz bu so'rovnomaga javob bergansiz";

    /// <summary>Error codes of the public endpoint — the page branches on them, not on the text.</summary>
    public const string CodeNotFound = "poll_not_found";
    public const string CodeClosed = "poll_closed";
    public const string CodeAnswered = "already_answered";
    public const string CodeValidation = "validation";

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static List<string> OptionsOf(PollQuestion q) =>
        string.IsNullOrWhiteSpace(q.Options) ? [] : [.. q.Options.Split('\n').Select(o => o.Trim()).Where(o => o.Length > 0)];

    public static PollQuestionDto ToDto(PollQuestion q) =>
        new(q.Id, q.Position, q.Kind, q.Text, OptionsOf(q), q.Required, q.Target,
            q.Description, q.AllowOther, q.MinValue, q.MaxValue, q.MinLabel, q.MaxLabel, q.MinChoices, q.MaxChoices);

    public static PollDto ToDto(Poll p, int responses) => new(
        p.Id, p.Title, p.Description, p.Audience, p.Anonymous, p.Status, p.ClosesOn is { } c ? Iso(c) : null,
        p.CreatedAt, p.OpenedAt, p.ClosedAt, p.NotifiedAt, responses,
        [.. p.Questions.OrderBy(q => q.Position).Select(ToDto)],
        p.PublicLink, p.CollectContact, LinkOpen(p) ? p.PublicToken : null);

    /// <summary>Answers are taken: opened and not past its last day.</summary>
    public static bool IsAccepting(Poll p, DateOnly today) =>
        p.Status == PollStatus.Open && (p.ClosesOn is null || today <= p.ClosesOn);

    /// <summary>The public page answers: the public audience always, a parents/teachers poll when switched on.</summary>
    public static bool LinkOpen(Poll p) => p.PublicToken is not null && (p.Audience == PollAudience.Public || p.PublicLink);

    /// <summary>128 random bits, URL-safe — the link cannot be guessed or enumerated.</summary>
    public static string NewToken() => System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

    /// <summary>NPS = % promoters (9–10) − % detractors (0–6), rounded; null without answers.</summary>
    public static (int? Nps, int Promoters, int Passives, int Detractors) NpsOf(IReadOnlyCollection<int> scores)
    {
        if (scores.Count == 0) return (null, 0, 0, 0);
        var promoters = scores.Count(s => s >= 9);
        var detractors = scores.Count(s => s <= 6);
        var nps = (int)Math.Round(100.0 * (promoters - detractors) / scores.Count, MidpointRounding.AwayFromZero);
        return (nps, promoters, scores.Count - promoters - detractors, detractors);
    }

    /// <summary>The stored respondent key — hashed with the poll id on anonymous polls.</summary>
    public static string KeyOf(Poll p, string rawKey) => p.Anonymous ? Hash(p, rawKey) : rawKey;

    /// <summary>A public-page respondent: the browser's own random id, always hashed — it means nothing raw.</summary>
    public static string LinkKey(Poll p, string browserToken) => Hash(p, $"link:{browserToken}");

    private static string Hash(Poll p, string rawKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{p.Id:N}:{rawKey}"))).ToLowerInvariant();

    // =====================================================================
    //  Admin
    // =====================================================================

    public static async Task<List<PollDto>> ListAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var polls = await db.Polls.AsNoTracking().Include(p => p.Questions).OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
        var counts = await db.PollResponses.AsNoTracking().GroupBy(r => r.PollId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return [.. polls.Select(p => ToDto(p, counts.GetValueOrDefault(p.Id)))];
    }

    public static async Task<PollDto?> GetAsync(IAppDbContext db, Guid id, CancellationToken ct = default)
    {
        var p = await db.Polls.AsNoTracking().Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? null : ToDto(p, await db.PollResponses.CountAsync(r => r.PollId == id, ct));
    }

    /// <summary>Error text, or null with the questions built from the input.</summary>
    public static string? Validate(PollInput input, out List<PollQuestion> questions, out DateOnly? closesOn)
    {
        questions = [];
        closesOn = null;
        if (string.IsNullOrWhiteSpace(input.Title)) return "Sarlavha kiritilmagan";
        if (input.Title.Trim().Length > 200) return "Sarlavha 200 belgidan oshmasin";
        if ((input.Description?.Length ?? 0) > MaxText) return $"Izoh {MaxText} belgidan oshmasin";
        if (!PollAudience.IsValid(input.Audience)) return "Kimlar uchun ekani noto'g'ri";
        if (input.CollectContact && input.Anonymous) return "Anonim so'rovnoma ism va telefonni so'ramaydi";
        if (!string.IsNullOrWhiteSpace(input.ClosesOn))
        {
            if (!DateOnly.TryParseExact(input.ClosesOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return "Tugash sanasi noto'g'ri";
            closesOn = d;
        }
        if (input.Questions is not { Count: > 0 } || input.Questions.All(q => q.Kind == PollQuestionKind.Section))
            return "Kamida bitta savol qo'shing";
        if (input.Questions.Count > MaxQuestions) return $"Savollar (bo'limlar bilan) {MaxQuestions} tadan oshmasin";

        var position = 0;
        foreach (var q in input.Questions)
        {
            position++;
            if (!PollQuestionKind.IsValid(q.Kind)) return $"{position}-savol turi noto'g'ri";
            var section = q.Kind == PollQuestionKind.Section;
            var text = (q.Text ?? "").Trim();
            if (text.Length == 0) return section ? $"{position}-bo'lim sarlavhasi bo'sh" : $"{position}-savol matni bo'sh";
            if (text.Length > 500) return $"{position}-savol 500 belgidan oshmasin";
            var description = Clean(q.Description);
            if (description?.Length > 1000) return $"{position}-savol izohi 1000 belgidan oshmasin";
            // A section is a header, not a question: never required, never about a person.
            var target = section || string.IsNullOrWhiteSpace(q.Target) ? PollTarget.School : q.Target;
            if (!PollTarget.IsValid(target)) return $"{position}-savol kimga tegishli ekani noto'g'ri";
            if (target == PollTarget.Teachers && input.Audience != PollAudience.Parents)
                return $"{position}-savol: ustozlarni faqat ota-onalar baholaydi";
            if (target == PollTarget.Management && input.Audience != PollAudience.Teachers)
                return $"{position}-savol: rahbariyatni faqat ustozlar baholaydi";
            var question = new PollQuestion
            {
                Position = position, Kind = q.Kind, Text = text, Description = description,
                Required = !section && q.Required, Target = target,
            };
            if (ApplySettings(q, question, position) is { } error) return error;
            questions.Add(question);
        }
        return null;
    }

    /// <summary>Copies the settings the kind uses (options, ranges, labels) and checks them.</summary>
    private static string? ApplySettings(PollQuestionInput q, PollQuestion into, int position)
    {
        switch (q.Kind)
        {
            case PollQuestionKind.Choice:
            case PollQuestionKind.Multi:
            case PollQuestionKind.Dropdown:
            {
                var options = (q.Options ?? []).Select(o => (o ?? "").Trim()).Where(o => o.Length > 0).Distinct().ToList();
                var max = q.Kind == PollQuestionKind.Dropdown ? MaxDropdownOptions : MaxOptions;
                if (options.Count < 2) return $"{position}-savolga kamida ikkita variant kiriting";
                if (options.Count > max) return $"{position}-savol variantlari {max} tadan oshmasin";
                if (options.Any(o => o.Length > 200)) return $"{position}-savol varianti 200 belgidan oshmasin";
                var other = q.AllowOther && q.Kind != PollQuestionKind.Dropdown;
                if (other && options.Any(o => string.Equals(o, OtherLabel, StringComparison.OrdinalIgnoreCase)))
                    return $"{position}-savol: «{OtherLabel}» variantini yozmang — uni «Boshqa» belgisi qo'shadi";
                into.Options = string.Join('\n', options);
                into.AllowOther = other;
                if (q.Kind == PollQuestionKind.Multi)
                {
                    var available = options.Count + (other ? 1 : 0);
                    if (q.MinChoices is { } min && (min < 1 || min > available))
                        return $"{position}-savol: eng kam tanlov 1..{available} oralig'ida bo'lsin";
                    if (q.MaxChoices is { } most && (most < 1 || most > available))
                        return $"{position}-savol: eng ko'p tanlov 1..{available} oralig'ida bo'lsin";
                    if (q.MinChoices > q.MaxChoices) return $"{position}-savol: eng kam tanlov eng ko'pidan oshmasin";
                    into.MinChoices = q.MinChoices;
                    into.MaxChoices = q.MaxChoices;
                }
                return null;
            }
            case PollQuestionKind.Scale:
            {
                var low = q.MinValue ?? 1;
                var high = q.MaxValue ?? 5;
                if (low != 0 && low != 1) return $"{position}-savol: shkala 0 yoki 1 dan boshlanadi";
                if (high < 2 || high > 10 || high != decimal.Truncate(high)) return $"{position}-savol: shkala oxiri 2..10 oralig'ida bo'lsin";
                var minLabel = Clean(q.MinLabel);
                var maxLabel = Clean(q.MaxLabel);
                if (minLabel?.Length > 100 || maxLabel?.Length > 100) return $"{position}-savol: shkala yozuvi 100 belgidan oshmasin";
                (into.MinValue, into.MaxValue, into.MinLabel, into.MaxLabel) = (low, high, minLabel, maxLabel);
                return null;
            }
            case PollQuestionKind.Number:
            {
                if ((q.MinValue is { } a && Math.Abs(a) >= MaxNumber) || (q.MaxValue is { } b && Math.Abs(b) >= MaxNumber))
                    return $"{position}-savol: chegara juda katta";
                if (q.MinValue > q.MaxValue) return $"{position}-savol: eng kichik qiymat eng kattasidan oshmasin";
                into.MinValue = q.MinValue is { } lo ? Math.Round(lo, 4) : null;
                into.MaxValue = q.MaxValue is { } hi ? Math.Round(hi, 4) : null;
                return null;
            }
            default:
                return null;
        }
    }

    public static async Task<(string? Error, Poll? Poll)> CreateAsync(IAppDbContext db, PollInput input, string actorId, CancellationToken ct = default)
    {
        if (Validate(input, out var questions, out var closesOn) is { } error) return (error, null);
        var poll = new Poll
        {
            Title = input.Title.Trim(), Description = Clean(input.Description), Audience = input.Audience,
            Anonymous = input.Anonymous, ClosesOn = closesOn, CreatedBy = actorId, CreatedAt = AppClock.NowInstant,
            PublicToken = NewToken(),
        };
        SetLink(poll, input);
        foreach (var q in questions) q.PollId = poll.Id;
        poll.Questions = questions;
        db.Polls.Add(poll);
        await db.SaveChangesAsync(ct);
        return (null, poll);
    }

    /// <summary>
    /// Before the first answer everything may change; after it only the title, the note, the last
    /// day and whether the link is open — the answers must keep meaning what they were given against
    /// (questions, audience, anonymity and the name/phone request stay).
    /// </summary>
    public static async Task<(int Status, string? Error)> UpdateAsync(IAppDbContext db, Guid id, PollInput input, CancellationToken ct = default)
    {
        var poll = await db.Polls.Include(p => p.Questions).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (poll is null) return (404, NotFoundMessage);
        if (Validate(input, out var questions, out var closesOn) is { } error) return (400, error);
        var answered = await db.PollResponses.AnyAsync(r => r.PollId == id, ct);
        if (answered && (input.Audience != poll.Audience || input.Anonymous != poll.Anonymous
                         || input.CollectContact != poll.CollectContact || !SameQuestions(poll.Questions, questions)))
            return (409, "Javoblar kelgan — endi faqat sarlavha, izoh, tugash sanasi va havolani o'zgartirish mumkin");

        poll.Title = input.Title.Trim();
        poll.Description = Clean(input.Description);
        poll.ClosesOn = closesOn;
        poll.PublicToken ??= NewToken();
        SetLink(poll, input);
        if (!answered)
        {
            poll.Audience = input.Audience;
            poll.Anonymous = input.Anonymous;
            db.PollQuestions.RemoveRange(poll.Questions);
            foreach (var q in questions)
            {
                q.PollId = poll.Id;
                db.PollQuestions.Add(q);
            }
        }
        await db.SaveChangesAsync(ct);
        return (200, null);
    }

    /// <summary>
    /// The link switches. A public poll is always on the link. The name/phone request is kept as
    /// given — it only takes effect while the link is open, and switching the link off and on again
    /// must not quietly change what link respondents are asked.
    /// </summary>
    private static void SetLink(Poll poll, PollInput input)
    {
        poll.PublicLink = input.Audience == PollAudience.Public || input.PublicLink;
        poll.CollectContact = input.CollectContact;
    }

    private static bool SameQuestions(IEnumerable<PollQuestion> current, List<PollQuestion> next)
    {
        var a = current.OrderBy(q => q.Position).Select(Shape).ToList();
        var b = next.Select(Shape).ToList();
        return a.SequenceEqual(b);
    }

    /// <summary>Everything an answer is given against.</summary>
    private static object Shape(PollQuestion q) => (
        q.Kind, q.Text, q.Description ?? "", q.Options ?? "", q.Required, q.Target,
        (q.AllowOther, q.MinValue, q.MaxValue, q.MinLabel ?? "", q.MaxLabel ?? "", q.MinChoices, q.MaxChoices));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>"Nusxa olish": the same form as a new draft — no answers, no last day, a new link.</summary>
    public static async Task<Poll?> DuplicateAsync(IAppDbContext db, Guid id, string actorId, CancellationToken ct = default)
    {
        var source = await db.Polls.AsNoTracking().Include(p => p.Questions).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (source is null) return null;
        const string suffix = " (nusxa)";
        var copy = new Poll
        {
            Title = (source.Title.Length + suffix.Length > 200 ? source.Title[..(200 - suffix.Length)] : source.Title) + suffix,
            Description = source.Description, Audience = source.Audience, Anonymous = source.Anonymous,
            PublicLink = source.PublicLink, CollectContact = source.CollectContact,
            CreatedBy = actorId, CreatedAt = AppClock.NowInstant, PublicToken = NewToken(),
        };
        copy.Questions = [.. source.Questions.OrderBy(q => q.Position).Select(q => new PollQuestion
        {
            PollId = copy.Id, Position = q.Position, Kind = q.Kind, Text = q.Text, Description = q.Description,
            Options = q.Options, AllowOther = q.AllowOther, MinValue = q.MinValue, MaxValue = q.MaxValue,
            MinLabel = q.MinLabel, MaxLabel = q.MaxLabel, MinChoices = q.MinChoices, MaxChoices = q.MaxChoices,
            Required = q.Required, Target = q.Target,
        })];
        db.Polls.Add(copy);
        await db.SaveChangesAsync(ct);
        return copy;
    }

    public static async Task<(int Status, string? Error, Poll? Poll)> SetStatusAsync(IAppDbContext db, Guid id, string status, CancellationToken ct = default)
    {
        var poll = await db.Polls.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (poll is null) return (404, NotFoundMessage, null);
        var now = AppClock.NowInstant;
        if (status == PollStatus.Open)
        {
            if (poll.ClosesOn is { } c && c < AppClock.Today) return (400, "Tugash sanasi o'tib ketgan — avval uni o'zgartiring", null);
            poll.Status = PollStatus.Open;
            poll.OpenedAt ??= now;
            poll.ClosedAt = null;
        }
        else
        {
            poll.Status = PollStatus.Closed;
            poll.ClosedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return (200, null, poll);
    }

    /// <summary>Only a poll nobody answered can be deleted.</summary>
    public static async Task<(int Status, string? Error)> DeleteAsync(IAppDbContext db, Guid id, CancellationToken ct = default)
    {
        var poll = await db.Polls.Include(p => p.Questions).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (poll is null) return (404, NotFoundMessage);
        if (await db.PollResponses.AnyAsync(r => r.PollId == id, ct))
            return (409, "Javoblar kelgan so'rovnomani o'chirib bo'lmaydi — yoping");
        db.Polls.Remove(poll);
        await db.SaveChangesAsync(ct);
        return (200, null);
    }

    // =====================================================================
    //  Portal (Mini App)
    // =====================================================================

    /// <summary>The child's teachers this week, with what they teach.</summary>
    public static async Task<List<PollSubjectDto>> TeachersOfAsync(IAppDbContext db, Student student, CancellationToken ct = default)
    {
        var (quarter, week) = await PortalSchedule.CurrentQuarterWeekAsync(db);
        var lessons = await PupilTimetable.ForWeekAsync(db, student, quarter, week, ct);
        if (lessons.Count == 0) return [];
        var subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var ids = lessons.Select(l => l.TeacherId).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking().Where(t => ids.Contains(t.Id) && !t.IsArchived)
            .ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        return [.. ids.Where(teachers.ContainsKey)
            .Select(id => new PollSubjectDto(id, teachers[id], string.Join(", ",
                lessons.Where(l => l.TeacherId == id).Select(l => subjects.GetValueOrDefault(l.SubjectId, "")).Where(n => n.Length > 0).Distinct())))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The management a teacher rates: active HR directors and managers, not themselves.</summary>
    public static async Task<List<PollSubjectDto>> ManagersAsync(IAppDbContext db, string? exceptUserId, CancellationToken ct = default)
    {
        var rows = await (from e in db.HrEmployees.AsNoTracking()
                          where e.IsActive && (e.HrRole == HrRoles.Director || e.HrRole == HrRoles.Manager) && e.UserId != exceptUserId
                          join p in db.HrPositions.AsNoTracking() on e.PositionId equals p.Id into pos
                          from p in pos.DefaultIfEmpty()
                          orderby e.FullName
                          select new PollSubjectDto(e.Id, e.FullName, p != null ? p.Name : null)).ToListAsync(ct);
        return rows;
    }

    public static async Task<List<PortalPollDto>> OpenForAsync(
        IAppDbContext db, PollRespondent who, List<PollSubjectDto> teachers, List<PollSubjectDto> managers, CancellationToken ct = default)
    {
        var today = AppClock.Today;
        var polls = (await db.Polls.AsNoTracking().Include(p => p.Questions)
                .Where(p => p.Status == PollStatus.Open && p.Audience == who.Audience)
                .OrderByDescending(p => p.OpenedAt).ToListAsync(ct))
            .Where(p => IsAccepting(p, today)).ToList();
        if (polls.Count == 0) return [];
        var keys = polls.Select(p => KeyOf(p, who.RawKey)).ToList();
        var answered = (await db.PollResponses.AsNoTracking().Where(r => keys.Contains(r.RespondentKey))
            .Select(r => new { r.PollId, r.RespondentKey }).ToListAsync(ct)).Select(r => (r.PollId, r.RespondentKey)).ToHashSet();
        return [.. polls.Select(p => new PortalPollDto(
            p.Id, p.Title, p.Description, p.Anonymous, p.ClosesOn is { } c ? Iso(c) : null,
            answered.Contains((p.Id, KeyOf(p, who.RawKey))),
            [.. p.Questions.OrderBy(q => q.Position).Select(ToDto)],
            p.Questions.Any(q => q.Target == PollTarget.Teachers) ? teachers : [],
            p.Questions.Any(q => q.Target == PollTarget.Management) ? managers : []))];
    }

    /// <summary>Error text (and HTTP status), or null when saved.</summary>
    public static async Task<(int Status, string? Error)> SubmitAsync(
        IAppDbContext db, Guid pollId, PollRespondent who, PollSubmitRequest req,
        IReadOnlyCollection<string> teacherIds, IReadOnlyCollection<string> managerIds, CancellationToken ct = default)
    {
        var poll = await db.Polls.AsNoTracking().Include(p => p.Questions).FirstOrDefaultAsync(p => p.Id == pollId, ct);
        if (poll is null || poll.Audience != who.Audience) return (404, NotFoundMessage);
        if (!IsAccepting(poll, AppClock.Today)) return (400, ClosedMessage);
        var key = KeyOf(poll, who.RawKey);
        if (await db.PollResponses.AnyAsync(r => r.PollId == pollId && r.RespondentKey == key, ct))
            return (409, AnsweredMessage);

        var response = new PollResponse
        {
            PollId = poll.Id, RespondentKey = key, SubmittedAt = AppClock.NowInstant, ClassName = who.ClassName,
            StudentId = poll.Anonymous ? null : who.StudentId, TeacherId = poll.Anonymous ? null : who.TeacherId,
        };
        return await SaveAsync(db, poll, response, req.Answers ?? [], teacherIds, managerIds, ct);
    }

    /// <summary>Checks every answer against its question and stores the sheet; (200, null) when saved.</summary>
    private static async Task<(int Status, string? Error)> SaveAsync(
        IAppDbContext db, Poll poll, PollResponse response, List<PollAnswerInput> input,
        IReadOnlyCollection<string> teacherIds, IReadOnlyCollection<string> managerIds, CancellationToken ct)
    {
        var answerable = poll.Questions.Where(q => q.Kind != PollQuestionKind.Section).OrderBy(q => q.Position).ToList();
        foreach (var q in answerable)
        {
            var allowed = q.Target switch
            {
                PollTarget.Teachers => teacherIds,
                PollTarget.Management => managerIds,
                _ => null,
            };
            var mine = input.Where(a => a.QuestionId == q.Id).ToList();
            if (allowed is null)
            {
                if (mine.Count > 1) return (400, $"«{q.Text}» — bitta javob bo'ladi");
                mine = [.. mine.Select(a => a with { SubjectId = null })];
            }
            else
            {
                if (mine.Any(a => a.SubjectId is null || !allowed.Contains(a.SubjectId)))
                    return (400, $"«{q.Text}» — baholanadigan odam ro'yxatda yo'q");
                if (mine.GroupBy(a => a.SubjectId).Any(g => g.Count() > 1)) return (400, $"«{q.Text}» — har kishiga bitta javob");
            }
            var given = new List<PollAnswer>();
            foreach (var a in mine)
            {
                var (error, answer) = Read(q, a);
                if (error is not null) return (400, error);
                if (answer is not null) given.Add(answer);
            }
            // A per-person question is required "at least once" — nobody has to rate every teacher.
            if (q.Required && given.Count == 0 && (allowed is null || allowed.Count > 0))
                return (400, $"«{q.Text}» savoliga javob bering");
            foreach (var answer in given)
            {
                answer.ResponseId = response.Id;
                response.Answers.Add(answer);
            }
        }
        // A section is not a question: an answer to it is as unknown as one to another poll's question.
        if (input.Any(a => answerable.All(q => q.Id != a.QuestionId))) return (400, "Noma'lum savol");

        db.PollResponses.Add(response);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return (409, AnsweredMessage);
        }
        return (200, null);
    }

    /// <summary>One answer checked against its question; a blank answer is (null, null).</summary>
    private static (string? Error, PollAnswer? Answer) Read(PollQuestion q, PollAnswerInput a)
    {
        PollAnswer New() => new() { QuestionId = q.Id, SubjectId = a.SubjectId };
        switch (q.Kind)
        {
            case PollQuestionKind.Nps:
            case PollQuestionKind.Stars:
            case PollQuestionKind.Scale:
            {
                if (a.Score is null) return (null, null);
                var (min, max) = ScoreRange(q);
                if (a.Score < min || a.Score > max) return ($"«{q.Text}» — baho {min}..{max} oralig'ida", null);
                var answer = New();
                answer.Score = a.Score;
                return (null, answer);
            }
            case PollQuestionKind.Choice:
            case PollQuestionKind.Dropdown:
            {
                var choice = (a.Choice ?? "").Trim();
                var other = q.AllowOther ? (a.Text ?? "").Trim() : "";
                if (choice.Length == 0 && other.Length == 0) return (null, null);
                if (choice.Length > 0 && other.Length > 0) return ($"«{q.Text}» — bitta variant tanlang", null);
                if (other.Length > MaxOtherText) return ($"«{q.Text}» — «{OtherLabel}» javobi {MaxOtherText} belgidan oshmasin", null);
                if (choice.Length > 0 && !OptionsOf(q).Contains(choice)) return ($"«{q.Text}» — bunday variant yo'q", null);
                var answer = New();
                if (choice.Length > 0) answer.Choice = choice;
                else answer.Text = other;
                return (null, answer);
            }
            case PollQuestionKind.Multi:
            {
                var options = OptionsOf(q);
                var picked = (a.Choices ?? []).Select(c => (c ?? "").Trim()).Where(c => c.Length > 0).Distinct().ToList();
                if (picked.Any(c => !options.Contains(c))) return ($"«{q.Text}» — bunday variant yo'q", null);
                var other = q.AllowOther ? (a.Text ?? "").Trim() : "";
                if (other.Length > MaxOtherText) return ($"«{q.Text}» — «{OtherLabel}» javobi {MaxOtherText} belgidan oshmasin", null);
                var count = picked.Count + (other.Length > 0 ? 1 : 0);
                if (count == 0) return (null, null);
                if (q.MinChoices is { } least && count < least) return ($"«{q.Text}» — kamida {least} ta variant tanlang", null);
                if (q.MaxChoices is { } most && count > most) return ($"«{q.Text}» — ko'pi bilan {most} ta variant tanlang", null);
                var answer = New();
                answer.Choices = [.. options.Where(picked.Contains)];
                answer.Text = other.Length > 0 ? other : null;
                return (null, answer);
            }
            case PollQuestionKind.Number:
            {
                if (a.Number is not { } n) return (null, null);
                if (Math.Abs(n) >= MaxNumber) return ($"«{q.Text}» — son juda katta", null);
                if (q.MinValue is { } low && n < low) return ($"«{q.Text}» — kamida {Number(low)}", null);
                if (q.MaxValue is { } high && n > high) return ($"«{q.Text}» — ko'pi bilan {Number(high)}", null);
                var answer = New();
                answer.Number = Math.Round(n, 4);
                return (null, answer);
            }
            case PollQuestionKind.Date:
            {
                if (string.IsNullOrWhiteSpace(a.Date)) return (null, null);
                if (!DateOnly.TryParseExact(a.Date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    return ($"«{q.Text}» — sana noto'g'ri", null);
                var answer = New();
                answer.Date = d;
                return (null, answer);
            }
            case PollQuestionKind.Short:
            {
                // One line: whatever line breaks a pasted text brings become spaces.
                var text = string.Join(' ', (a.Text ?? "").Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
                if (text.Length == 0) return (null, null);
                if (text.Length > MaxShortText) return ($"«{q.Text}» — javob {MaxShortText} belgidan oshmasin", null);
                var answer = New();
                answer.Text = text;
                return (null, answer);
            }
            default:
            {
                var text = (a.Text ?? "").Trim();
                if (text.Length == 0) return (null, null);
                if (text.Length > MaxText) return ($"«{q.Text}» — javob {MaxText} belgidan oshmasin", null);
                var answer = New();
                answer.Text = text;
                return (null, answer);
            }
        }
    }

    /// <summary>NPS 0–10, stars 1–5, scale as configured (old rows without a range read 1–5).</summary>
    private static (int Min, int Max) ScoreRange(PollQuestion q) => q.Kind switch
    {
        PollQuestionKind.Nps => (0, 10),
        PollQuestionKind.Scale => ((int)(q.MinValue ?? 1), (int)(q.MaxValue ?? 5)),
        _ => (1, 5),
    };

    private static string Number(decimal n) => n.ToString("0.####", CultureInfo.InvariantCulture);

    // =====================================================================
    //  Public link (/sorovnoma/{token})
    // =====================================================================

    /// <summary>The poll behind a public token — null when unknown or its link is switched off.</summary>
    public static async Task<Poll?> FindByTokenAsync(IAppDbContext db, string? token, CancellationToken ct = default)
    {
        // Malformed tokens never reach the database.
        if (token is not { Length: >= 16 and <= 32 } || !token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return null;
        var poll = await db.Polls.AsNoTracking().Include(p => p.Questions).FirstOrDefaultAsync(p => p.PublicToken == token, ct);
        return poll is not null && LinkOpen(poll) ? poll : null;
    }

    public static PublicPollDto ToPublicDto(Poll p)
    {
        var accepting = IsAccepting(p, AppClock.Today);
        return new PublicPollDto(
            p.Title, accepting ? p.Description : null, accepting, p.Anonymous, p.CollectContact,
            p.ClosesOn is { } c ? Iso(c) : null,
            accepting ? [.. p.Questions.Where(q => q.Target == PollTarget.School).OrderBy(q => q.Position).Select(ToDto)] : []);
    }

    /// <summary>
    /// A public-page submission: (status, error code, message); (200, null, null) when saved. One per
    /// browser (its token, hashed) and — when the poll asks for contacts — one per phone number.
    /// </summary>
    public static async Task<(int Status, string? Code, string? Error)> PublicSubmitAsync(
        IAppDbContext db, Poll poll, PublicPollSubmitRequest req, CancellationToken ct = default)
    {
        if (!IsAccepting(poll, AppClock.Today)) return (400, CodeClosed, ClosedMessage);
        var browser = (req.RespondentToken ?? "").Trim();
        if (browser is not { Length: >= 16 and <= 64 } || !browser.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return (400, CodeValidation, "Sahifani yangilab, qaytadan urinib ko'ring");

        string? name = null, phone = null;
        if (poll.CollectContact)
        {
            name = Clean(req.ContactName);
            if (name is not { Length: >= 2 }) return (400, CodeValidation, "Ismingizni kiriting");
            if (name.Length > 100) return (400, CodeValidation, "Ism 100 belgidan oshmasin");
            phone = PhoneDigits(req.ContactPhone);
            if (phone is null) return (400, CodeValidation, "Telefon raqamini to'liq kiriting");
        }

        var key = LinkKey(poll, browser);
        if (await db.PollResponses.AnyAsync(r => r.PollId == poll.Id && (r.RespondentKey == key || (phone != null && r.ContactPhone == phone)), ct))
            return (409, CodeAnswered, AnsweredMessage);

        var response = new PollResponse
        {
            PollId = poll.Id, RespondentKey = key, SubmittedAt = AppClock.NowInstant, ViaLink = true,
            ContactName = name, ContactPhone = phone,
        };
        // Per-person questions are not on the public page: nobody to rate, nothing required there.
        var (status, error) = await SaveAsync(db, poll, response, req.Answers ?? [], [], [], ct);
        return (status, status switch { 200 => null, 409 => CodeAnswered, _ => CodeValidation }, error);
    }

    /// <summary>"+998 90 123 45 67" / "901234567" → "998901234567"; null unless an Uzbek mobile number.</summary>
    public static string? PhoneDigits(string? raw)
    {
        var digits = new string((raw ?? "").Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 9) digits = "998" + digits;
        return digits.Length == 12 && digits.StartsWith("998", StringComparison.Ordinal) ? digits : null;
    }

    /// <summary>"998901234567" → "+998 90 123 45 67".</summary>
    public static string FormatPhone(string digits) =>
        digits.Length == 12 ? $"+{digits[..3]} {digits[3..5]} {digits[5..8]} {digits[8..10]} {digits[10..]}" : digits;

    // =====================================================================
    //  Results
    // =====================================================================

    public static async Task<PollResultsDto?> ResultsAsync(IAppDbContext db, Guid id, string? className, CancellationToken ct = default)
    {
        var poll = await db.Polls.AsNoTracking().Include(p => p.Questions).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (poll is null) return null;
        var all = await db.PollResponses.AsNoTracking().Include(r => r.Answers).Where(r => r.PollId == id).ToListAsync(ct);
        var classes = all.Select(r => r.ClassName).Where(c => !string.IsNullOrEmpty(c)).Distinct()
            .Order(StringComparer.OrdinalIgnoreCase).Select(c => c!).ToList();
        var responses = string.IsNullOrWhiteSpace(className) ? all : [.. all.Where(r => r.ClassName == className)];

        var names = await SubjectNamesAsync(db, responses, ct);
        var respondentNames = await RespondentNamesAsync(db, poll, responses, ct);

        var questions = new List<PollQuestionResultDto>();
        foreach (var q in poll.Questions.OrderBy(q => q.Position))
        {
            // A section is a heading on the results page too.
            if (q.Kind == PollQuestionKind.Section)
            {
                questions.Add(new PollQuestionResultDto(q.Id, q.Kind, q.Text, q.Target, 0, null, null, 0, 0, 0, [], [], [], q.Description, null, null));
                continue;
            }
            var answers = responses.SelectMany(r => r.Answers.Where(a => a.QuestionId == q.Id).Select(a => (r, a))).ToList();
            var scores = answers.Where(x => x.a.Score is not null).Select(x => x.a.Score!.Value).ToList();
            var numbers = answers.Where(x => x.a.Number is not null).Select(x => (double)x.a.Number!.Value).ToList();
            var (nps, promoters, passives, detractors) = q.Kind == PollQuestionKind.Nps ? NpsOf(scores) : (null, 0, 0, 0);
            double? average = q.Kind == PollQuestionKind.Number
                ? numbers.Count == 0 ? null : Math.Round(numbers.Average(), 2)
                : scores.Count == 0 ? null : Math.Round(scores.Average(), 2);

            var distribution = q.Kind switch
            {
                PollQuestionKind.Nps or PollQuestionKind.Stars or PollQuestionKind.Scale => ScaleCounts(q, scores),
                PollQuestionKind.Choice or PollQuestionKind.Dropdown =>
                    [.. OptionsOf(q).Select(o => new PollCountDto(o, answers.Count(x => x.a.Choice == o))),
                     .. q.AllowOther ? [new PollCountDto(OtherLabel, answers.Count(x => x.a.Choice is null && x.a.Text is not null))] : Array.Empty<PollCountDto>()],
                PollQuestionKind.Multi =>
                    [.. OptionsOf(q).Select(o => new PollCountDto(o, answers.Count(x => x.a.Choices?.Contains(o) == true))),
                     .. q.AllowOther ? [new PollCountDto(OtherLabel, answers.Count(x => x.a.Text is not null))] : Array.Empty<PollCountDto>()],
                PollQuestionKind.Date => answers.Where(x => x.a.Date is not null).GroupBy(x => x.a.Date!.Value).OrderBy(g => g.Key)
                    .Select(g => new PollCountDto(Iso(g.Key), g.Count())).ToList(),
                _ => [],
            };

            var subjects = q.Target == PollTarget.School ? [] : answers.Where(x => x.a.SubjectId is not null)
                .GroupBy(x => x.a.SubjectId!)
                .Select(g =>
                {
                    var s = g.Where(x => x.a.Score is not null).Select(x => x.a.Score!.Value).ToList();
                    return new PollSubjectResultDto(g.Key, names.GetValueOrDefault(g.Key, ""), g.Count(),
                        s.Count == 0 ? null : Math.Round(s.Average(), 2),
                        q.Kind == PollQuestionKind.Nps ? NpsOf(s).Nps : null);
                })
                .OrderByDescending(s => s.Nps ?? int.MinValue).ThenByDescending(s => s.Average ?? double.MinValue).ThenBy(s => s.Name)
                .ToList();

            // Free text — and the "Boshqa" texts, so the admin can read what "other" was.
            var hasTexts = q.Kind is PollQuestionKind.Text or PollQuestionKind.Short || (PollQuestionKind.HasOptions(q.Kind) && q.AllowOther);
            var texts = !hasTexts ? [] : answers.Where(x => x.a.Text is not null)
                .OrderByDescending(x => x.r.SubmittedAt).Take(500)
                .Select(x => new PollTextAnswerDto(x.a.Text!, x.a.SubjectId is { } sid ? names.GetValueOrDefault(sid) : null,
                    x.r.ClassName, respondentNames.GetValueOrDefault(x.r.Id), x.r.SubmittedAt))
                .ToList();

            questions.Add(new PollQuestionResultDto(q.Id, q.Kind, q.Text, q.Target, answers.Count, average, nps,
                promoters, passives, detractors, distribution, subjects, texts, q.Description,
                numbers.Count == 0 ? null : numbers.Min(), numbers.Count == 0 ? null : numbers.Max()));
        }
        return new PollResultsDto(poll.Id, poll.Title, poll.Audience, poll.Anonymous, responses.Count,
            string.IsNullOrWhiteSpace(className) ? null : className, classes, questions);
    }

    private static List<PollCountDto> ScaleCounts(PollQuestion q, List<int> scores)
    {
        var (min, max) = ScoreRange(q);
        return [.. Enumerable.Range(min, max - min + 1).Select(s => new PollCountDto(s.ToString(CultureInfo.InvariantCulture), scores.Count(x => x == s)))];
    }

    /// <summary>Names of the people rated (teachers, then HR employees), by id.</summary>
    private static async Task<Dictionary<string, string>> SubjectNamesAsync(IAppDbContext db, IEnumerable<PollResponse> responses, CancellationToken ct)
    {
        var subjectIds = responses.SelectMany(r => r.Answers).Select(a => a.SubjectId).Where(s => s is not null).Distinct().ToList();
        if (subjectIds.Count == 0) return new Dictionary<string, string>();
        var names = await db.Teachers.AsNoTracking().Where(t => subjectIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        foreach (var e in await db.HrEmployees.AsNoTracking().Where(e => subjectIds.Contains(e.Id)).ToListAsync(ct))
            names.TryAdd(e.Id, e.FullName);
        return names;
    }

    /// <summary>Who answered, by response id — only when the poll is not anonymous.</summary>
    private static async Task<Dictionary<Guid, string>> RespondentNamesAsync(
        IAppDbContext db, Poll poll, IReadOnlyCollection<PollResponse> responses, CancellationToken ct)
    {
        var respondentNames = new Dictionary<Guid, string>();
        if (poll.Anonymous) return respondentNames;
        var studentIds = responses.Select(r => r.StudentId).Where(s => s is not null).Distinct().ToList();
        var teacherIds = responses.Select(r => r.TeacherId).Where(s => s is not null).Distinct().ToList();
        var students = studentIds.Count == 0 ? new Dictionary<string, string>()
            : await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var teachers = teacherIds.Count == 0 ? new Dictionary<string, string>()
            : await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        foreach (var r in responses)
            respondentNames[r.Id] = r.StudentId is { } s ? $"{students.GetValueOrDefault(s, "")} (ota-onasi)"
                : r.TeacherId is { } t ? teachers.GetValueOrDefault(t, "")
                : r.ContactName ?? (r.ViaLink ? "Havola orqali" : "");
        return respondentNames;
    }

    // =====================================================================
    //  Javoblar — one row per response, and its Excel export
    // =====================================================================

    /// <summary>A page of the responses table, newest first; null when the poll does not exist.</summary>
    public static async Task<PollResponsesDto?> ResponsesAsync(
        IAppDbContext db, Guid id, string? className, int page, int pageSize, CancellationToken ct = default)
    {
        var poll = await db.Polls.AsNoTracking().Include(p => p.Questions).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (poll is null) return null;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (sheet, filtered) = await SheetQueryAsync(db, poll, className, ct);
        var total = await filtered.CountAsync(ct);
        var rows = await filtered.Include(r => r.Answers)
            .OrderByDescending(r => r.SubmittedAt).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PollResponsesDto(poll.Id, poll.Title, sheet.ShowRespondent, sheet.ShowPhone, sheet.ShowSource,
            total, page, pageSize, sheet.Classes, sheet.Columns, await RowsAsync(db, poll, rows, ct));
    }

    /// <summary>Every response (class filter as on screen) as an .xlsx — the same cells as the table.</summary>
    public static async Task<byte[]?> ExportAsync(IAppDbContext db, Guid id, string? className, CancellationToken ct = default)
    {
        var poll = await db.Polls.AsNoTracking().Include(p => p.Questions).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (poll is null) return null;
        var (sheet, filtered) = await SheetQueryAsync(db, poll, className, ct);
        var responses = await filtered.Include(r => r.Answers).OrderByDescending(r => r.SubmittedAt).ThenBy(r => r.Id).ToListAsync(ct);
        var rows = await RowsAsync(db, poll, responses, ct);

        var showClass = sheet.Classes.Count > 0;
        List<string> headers = ["№", "Vaqt"];
        if (sheet.ShowRespondent) headers.Add("Kim");
        if (sheet.ShowPhone) headers.Add("Telefon");
        if (showClass) headers.Add("Sinf");
        if (sheet.ShowSource) headers.Add("Manba");
        headers.AddRange(sheet.Columns.Select(c => c.Text));

        var lines = rows.Select((r, i) =>
        {
            List<ExcelExport.XlsxCell> cells = [(i + 1).ToString(CultureInfo.InvariantCulture), AppClock.ToLocal(r.SubmittedAt).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)];
            if (sheet.ShowRespondent) cells.Add(r.Respondent);
            if (sheet.ShowPhone) cells.Add(r.Phone);
            if (showClass) cells.Add(r.ClassName);
            if (sheet.ShowSource) cells.Add(r.ViaLink ? "Havola" : "Ilova");
            cells.AddRange(r.Cells.Select(c => (ExcelExport.XlsxCell)c));
            return (IReadOnlyList<ExcelExport.XlsxCell>)cells;
        }).ToList();

        var title = string.IsNullOrWhiteSpace(className) ? poll.Title : $"{poll.Title} — {className}";
        var widths = headers.Select((h, i) => i == 0 ? 6d : i == 1 ? 17d : Math.Clamp(h.Length, 14, 40)).ToList();
        return ExcelExport.BuildTables([new ExcelExport.TableSpec("Javoblar", headers, lines, Title: title, Widths: widths)]);
    }

    private sealed record SheetShape(
        bool ShowRespondent, bool ShowPhone, bool ShowSource, List<string> Classes, List<PollResponseColumnDto> Columns);

    /// <summary>The columns (fixed for the whole poll, not per page) and the filtered responses query.</summary>
    private static async Task<(SheetShape Shape, IQueryable<PollResponse> Filtered)> SheetQueryAsync(
        IAppDbContext db, Poll poll, string? className, CancellationToken ct)
    {
        var all = db.PollResponses.AsNoTracking().Where(r => r.PollId == poll.Id);
        var classes = (await all.Where(r => r.ClassName != null && r.ClassName != "").Select(r => r.ClassName!).Distinct().ToListAsync(ct))
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
        var showPhone = await all.AnyAsync(r => r.ContactPhone != null, ct);
        var showSource = poll.Audience != PollAudience.Public && await all.AnyAsync(r => r.ViaLink, ct);
        var columns = poll.Questions.Where(q => q.Kind != PollQuestionKind.Section).OrderBy(q => q.Position)
            .Select(q => new PollResponseColumnDto(q.Id, q.Kind, q.Text)).ToList();
        var filtered = string.IsNullOrWhiteSpace(className) ? all : all.Where(r => r.ClassName == className);
        return (new SheetShape(!poll.Anonymous, showPhone, showSource, classes, columns), filtered);
    }

    private static async Task<List<PollResponseRowDto>> RowsAsync(IAppDbContext db, Poll poll, List<PollResponse> responses, CancellationToken ct)
    {
        var names = await SubjectNamesAsync(db, responses, ct);
        var respondents = await RespondentNamesAsync(db, poll, responses, ct);
        var questions = poll.Questions.Where(q => q.Kind != PollQuestionKind.Section).OrderBy(q => q.Position).ToList();
        return [.. responses.Select(r =>
        {
            var byQuestion = r.Answers.ToLookup(a => a.QuestionId);
            return new PollResponseRowDto(r.Id, r.SubmittedAt,
                poll.Anonymous ? null : respondents.GetValueOrDefault(r.Id),
                r.ContactPhone is { } p ? FormatPhone(p) : null, r.ClassName, r.ViaLink,
                [.. questions.Select(q => Cell(q, [.. byQuestion[q.Id]], names))]);
        })];
    }

    /// <summary>A question's cell: the value, or "Teacher: value; …" for a per-person question.</summary>
    private static string Cell(PollQuestion q, List<PollAnswer> answers, IReadOnlyDictionary<string, string> names) =>
        q.Target == PollTarget.School
            ? answers.Count == 0 ? "" : ValueOf(q, answers[0])
            : string.Join("; ", answers.Select(a => $"{names.GetValueOrDefault(a.SubjectId ?? "", "?")}: {ValueOf(q, a)}"));

    /// <summary>One answer as text — the table, the Excel file and nothing else read it.</summary>
    public static string ValueOf(PollQuestion q, PollAnswer a) => q.Kind switch
    {
        PollQuestionKind.Nps or PollQuestionKind.Stars or PollQuestionKind.Scale => a.Score?.ToString(CultureInfo.InvariantCulture) ?? "",
        PollQuestionKind.Choice or PollQuestionKind.Dropdown => a.Choice ?? (a.Text is { } t ? $"{OtherLabel}: {t}" : ""),
        PollQuestionKind.Multi => string.Join(", ", MultiValues(a)),
        PollQuestionKind.Number => a.Number is { } n ? Number(n) : "",
        PollQuestionKind.Date => a.Date is { } d ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "",
        _ => a.Text ?? "",
    };

    /// <summary>The ticked options, then the "Boshqa" text.</summary>
    private static List<string> MultiValues(PollAnswer a)
    {
        var values = new List<string>(a.Choices ?? []);
        if (a.Text is { } other) values.Add($"{OtherLabel}: {other}");
        return values;
    }

    /// <summary>NPS answers about people (teachers / managers) submitted in a range — the KPI's input.</summary>
    public static async Task<Dictionary<string, List<int>>> PersonScoresAsync(
        IAppDbContext db, string target, DateTimeOffset since, DateTimeOffset until, CancellationToken ct = default)
    {
        var rows = await (from a in db.PollAnswers.AsNoTracking()
                          join q in db.PollQuestions.AsNoTracking() on a.QuestionId equals q.Id
                          join r in db.PollResponses.AsNoTracking() on a.ResponseId equals r.Id
                          where q.Target == target && q.Kind == PollQuestionKind.Nps && a.SubjectId != null && a.Score != null
                                && r.SubmittedAt >= since && r.SubmittedAt < until
                          select new { a.SubjectId, a.Score }).ToListAsync(ct);
        return rows.GroupBy(x => x.SubjectId!).ToDictionary(g => g.Key, g => g.Select(x => x.Score!.Value).ToList());
    }
}

/// <summary>The one bot announcement of a poll — to every parent or every teacher linked to the bot.</summary>
public sealed class PollNotifier(IAppDbContext db, TelegramService telegram, ILogger<PollNotifier> logger)
{
    private static readonly string[] ParentRoles = ["parent", Roles.Student];

    /// <summary>A public (link-only) poll has no bot audience — its link is shared by hand.</summary>
    public static async Task<IReadOnlyCollection<long>> RecipientsAsync(IAppDbContext db, string audience, CancellationToken ct = default)
    {
        if (audience is not (PollAudience.Parents or PollAudience.Teachers)) return [];
        var parents = audience == PollAudience.Parents;
        var ids = new HashSet<long>(await db.TelegramRegistrations.AsNoTracking()
            .Where(r => parents ? r.TeacherId == null : r.TeacherId != null)
            .Select(r => r.ChatId).ToListAsync(ct));
        string[] roles = parents ? ParentRoles : [Roles.Teacher];
        ids.UnionWith(await (from a in db.TelegramAccounts.AsNoTracking()
                             join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                             where roles.Contains(u.Role)
                             select a.TelegramUserId).ToListAsync(ct));
        ids.Remove(0);
        return ids;
    }

    public static string Text(Poll poll)
    {
        var lines = new List<string> { $"📋 Yangi so'rovnoma: {poll.Title}" };
        if (!string.IsNullOrWhiteSpace(poll.Description)) lines.AddRange(["", poll.Description.Trim()]);
        if (poll.ClosesOn is { } c) lines.AddRange(["", $"Javob berish muddati: {c:dd.MM.yyyy} gacha."]);
        if (poll.Anonymous) lines.Add("So'rovnoma anonim — ismingiz ko'rinmaydi.");
        lines.AddRange(["", $"Javob berish: «{TelegramPanelKeys.ButtonText}»."]);
        return string.Join('\n', lines);
    }

    /// <summary>(recipients, sent). Never throws — the poll is already open.</summary>
    public async Task<(int Total, int Sent)> AnnounceAsync(Poll poll, CancellationToken ct = default)
    {
        if (!telegram.IsConfigured) return (0, 0);
        try
        {
            var chats = await RecipientsAsync(db, poll.Audience, ct);
            var text = Text(poll);
            var sent = 0;
            foreach (var chat in chats)
                if (await telegram.SendMessageAsync(chat, text, ct: ct)) sent++;
            logger.LogInformation("So'rovnoma e'loni: poll={PollId}, chats={Total}, sent={Sent}", poll.Id, chats.Count, sent);
            return (chats.Count, sent);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "So'rovnoma e'loni yuborilmadi: poll={PollId}", poll.Id);
            return (0, 0);
        }
    }
}
