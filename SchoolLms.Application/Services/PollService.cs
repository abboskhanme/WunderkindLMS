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
// ===========================================================================

public record PollQuestionDto(Guid Id, int Position, string Kind, string Text, List<string> Options, bool Required, string Target);

public record PollDto(
    Guid Id, string Title, string? Description, string Audience, bool Anonymous, string Status, string? ClosesOn,
    DateTimeOffset CreatedAt, DateTimeOffset? OpenedAt, DateTimeOffset? ClosedAt, DateTimeOffset? NotifiedAt,
    int Responses, List<PollQuestionDto> Questions);

public record PollQuestionInput(string Kind, string Text, List<string>? Options, bool Required, string? Target);

public record PollInput(string Title, string? Description, string Audience, bool Anonymous, string? ClosesOn, List<PollQuestionInput> Questions);

public record PollCountDto(string Label, int Count);

public record PollSubjectResultDto(string Id, string Name, int Answers, double? Average, int? Nps);

public record PollTextAnswerDto(string Text, string? SubjectName, string? ClassName, string? RespondentName, DateTimeOffset At);

public record PollQuestionResultDto(
    Guid QuestionId, string Kind, string Text, string Target, int Answers, double? Average, int? Nps,
    int Promoters, int Passives, int Detractors, List<PollCountDto> Distribution,
    List<PollSubjectResultDto> Subjects, List<PollTextAnswerDto> Texts);

public record PollResultsDto(
    Guid PollId, string Title, string Audience, bool Anonymous, int Responses, string? ClassName,
    List<string> Classes, List<PollQuestionResultDto> Questions);

/// <summary>A person a portal question rates: a teacher of the child, or a manager.</summary>
public record PollSubjectDto(string Id, string Name, string? Note);

public record PortalPollDto(
    Guid Id, string Title, string? Description, bool Anonymous, string? ClosesOn, bool Answered,
    List<PollQuestionDto> Questions, List<PollSubjectDto> Teachers, List<PollSubjectDto> Managers);

public record PollAnswerInput(Guid QuestionId, string? SubjectId, int? Score, string? Choice, string? Text);

public record PollSubmitRequest(List<PollAnswerInput> Answers);

/// <summary>Who answers: a child (parents answer per child) or a teacher.</summary>
public sealed record PollRespondent(string Audience, string RawKey, string? StudentId, string? TeacherId, string? ClassName);

public static class PollService
{
    public const string Permission = "polls";
    public const int MaxQuestions = 30;
    public const int MaxOptions = 20;
    public const int MaxText = 2000;

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static List<string> OptionsOf(PollQuestion q) =>
        string.IsNullOrWhiteSpace(q.Options) ? [] : [.. q.Options.Split('\n').Select(o => o.Trim()).Where(o => o.Length > 0)];

    public static PollQuestionDto ToDto(PollQuestion q) =>
        new(q.Id, q.Position, q.Kind, q.Text, OptionsOf(q), q.Required, q.Target);

    public static PollDto ToDto(Poll p, int responses) => new(
        p.Id, p.Title, p.Description, p.Audience, p.Anonymous, p.Status, p.ClosesOn is { } c ? Iso(c) : null,
        p.CreatedAt, p.OpenedAt, p.ClosedAt, p.NotifiedAt, responses,
        [.. p.Questions.OrderBy(q => q.Position).Select(ToDto)]);

    /// <summary>Answers are taken: opened and not past its last day.</summary>
    public static bool IsAccepting(Poll p, DateOnly today) =>
        p.Status == PollStatus.Open && (p.ClosesOn is null || today <= p.ClosesOn);

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
    public static string KeyOf(Poll p, string rawKey)
    {
        if (!p.Anonymous) return rawKey;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{p.Id:N}:{rawKey}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

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
        if (!string.IsNullOrWhiteSpace(input.ClosesOn))
        {
            if (!DateOnly.TryParseExact(input.ClosesOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return "Tugash sanasi noto'g'ri";
            closesOn = d;
        }
        if (input.Questions is not { Count: > 0 }) return "Kamida bitta savol qo'shing";
        if (input.Questions.Count > MaxQuestions) return $"Savollar {MaxQuestions} tadan oshmasin";

        var position = 0;
        foreach (var q in input.Questions)
        {
            position++;
            var text = (q.Text ?? "").Trim();
            if (text.Length == 0) return $"{position}-savol matni bo'sh";
            if (text.Length > 500) return $"{position}-savol 500 belgidan oshmasin";
            if (!PollQuestionKind.IsValid(q.Kind)) return $"{position}-savol turi noto'g'ri";
            var target = string.IsNullOrWhiteSpace(q.Target) ? PollTarget.School : q.Target;
            if (!PollTarget.IsValid(target)) return $"{position}-savol kimga tegishli ekani noto'g'ri";
            if (target == PollTarget.Teachers && input.Audience != PollAudience.Parents)
                return $"{position}-savol: ustozlarni faqat ota-onalar baholaydi";
            if (target == PollTarget.Management && input.Audience != PollAudience.Teachers)
                return $"{position}-savol: rahbariyatni faqat ustozlar baholaydi";
            var options = (q.Options ?? []).Select(o => (o ?? "").Trim()).Where(o => o.Length > 0).Distinct().ToList();
            if (q.Kind == PollQuestionKind.Choice)
            {
                if (options.Count < 2) return $"{position}-savolga kamida ikkita variant kiriting";
                if (options.Count > MaxOptions) return $"{position}-savol variantlari {MaxOptions} tadan oshmasin";
                if (options.Any(o => o.Length > 200)) return $"{position}-savol varianti 200 belgidan oshmasin";
            }
            questions.Add(new PollQuestion
            {
                Position = position, Kind = q.Kind, Text = text, Required = q.Required, Target = target,
                Options = q.Kind == PollQuestionKind.Choice ? string.Join('\n', options) : null,
            });
        }
        return null;
    }

    public static async Task<(string? Error, Poll? Poll)> CreateAsync(IAppDbContext db, PollInput input, string actorId, CancellationToken ct = default)
    {
        if (Validate(input, out var questions, out var closesOn) is { } error) return (error, null);
        var poll = new Poll
        {
            Title = input.Title.Trim(), Description = Clean(input.Description), Audience = input.Audience,
            Anonymous = input.Anonymous, ClosesOn = closesOn, CreatedBy = actorId, CreatedAt = AppClock.NowInstant,
        };
        foreach (var q in questions) q.PollId = poll.Id;
        poll.Questions = questions;
        db.Polls.Add(poll);
        await db.SaveChangesAsync(ct);
        return (null, poll);
    }

    /// <summary>
    /// Before the first answer everything may change; after it only the title, the note and the
    /// last day — the answers must keep meaning what they were given against.
    /// </summary>
    public static async Task<(int Status, string? Error)> UpdateAsync(IAppDbContext db, Guid id, PollInput input, CancellationToken ct = default)
    {
        var poll = await db.Polls.Include(p => p.Questions).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (poll is null) return (404, "So'rovnoma topilmadi");
        if (Validate(input, out var questions, out var closesOn) is { } error) return (400, error);
        var answered = await db.PollResponses.AnyAsync(r => r.PollId == id, ct);
        if (answered && (input.Audience != poll.Audience || input.Anonymous != poll.Anonymous || !SameQuestions(poll.Questions, questions)))
            return (409, "Javoblar kelgan — endi faqat sarlavha, izoh va tugash sanasini o'zgartirish mumkin");

        poll.Title = input.Title.Trim();
        poll.Description = Clean(input.Description);
        poll.ClosesOn = closesOn;
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

    private static bool SameQuestions(IEnumerable<PollQuestion> current, List<PollQuestion> next)
    {
        var a = current.OrderBy(q => q.Position).Select(q => (q.Kind, q.Text, q.Options ?? "", q.Required, q.Target)).ToList();
        var b = next.Select(q => (q.Kind, q.Text, q.Options ?? "", q.Required, q.Target)).ToList();
        return a.SequenceEqual(b);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static async Task<(int Status, string? Error, Poll? Poll)> SetStatusAsync(IAppDbContext db, Guid id, string status, CancellationToken ct = default)
    {
        var poll = await db.Polls.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (poll is null) return (404, "So'rovnoma topilmadi", null);
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
        if (poll is null) return (404, "So'rovnoma topilmadi");
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
        if (poll is null || poll.Audience != who.Audience) return (404, "So'rovnoma topilmadi");
        if (!IsAccepting(poll, AppClock.Today)) return (400, "So'rovnoma yopilgan");
        var key = KeyOf(poll, who.RawKey);
        if (await db.PollResponses.AnyAsync(r => r.PollId == pollId && r.RespondentKey == key, ct))
            return (409, "Siz bu so'rovnomaga javob bergansiz");

        var input = req.Answers ?? [];
        var response = new PollResponse
        {
            PollId = poll.Id, RespondentKey = key, SubmittedAt = AppClock.NowInstant, ClassName = who.ClassName,
            StudentId = poll.Anonymous ? null : who.StudentId, TeacherId = poll.Anonymous ? null : who.TeacherId,
        };
        foreach (var q in poll.Questions.OrderBy(q => q.Position))
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
        if (input.Any(a => poll.Questions.All(q => q.Id != a.QuestionId))) return (400, "Noma'lum savol");

        db.PollResponses.Add(response);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return (409, "Siz bu so'rovnomaga javob bergansiz");
        }
        return (200, null);
    }

    /// <summary>One answer checked against its question; a blank answer is (null, null).</summary>
    private static (string? Error, PollAnswer? Answer) Read(PollQuestion q, PollAnswerInput a)
    {
        switch (q.Kind)
        {
            case PollQuestionKind.Nps:
            case PollQuestionKind.Stars:
                if (a.Score is null) return (null, null);
                var (min, max) = q.Kind == PollQuestionKind.Nps ? (0, 10) : (1, 5);
                if (a.Score < min || a.Score > max) return ($"«{q.Text}» — baho {min}..{max} oralig'ida", null);
                return (null, new PollAnswer { QuestionId = q.Id, SubjectId = a.SubjectId, Score = a.Score });
            case PollQuestionKind.Choice:
                if (string.IsNullOrWhiteSpace(a.Choice)) return (null, null);
                if (!OptionsOf(q).Contains(a.Choice.Trim())) return ($"«{q.Text}» — bunday variant yo'q", null);
                return (null, new PollAnswer { QuestionId = q.Id, SubjectId = a.SubjectId, Choice = a.Choice.Trim() });
            default:
                var text = (a.Text ?? "").Trim();
                if (text.Length == 0) return (null, null);
                if (text.Length > MaxText) return ($"«{q.Text}» — javob {MaxText} belgidan oshmasin", null);
                return (null, new PollAnswer { QuestionId = q.Id, SubjectId = a.SubjectId, Text = text });
        }
    }

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

        var subjectIds = responses.SelectMany(r => r.Answers).Select(a => a.SubjectId).Where(s => s is not null).Distinct().ToList();
        var names = await db.Teachers.AsNoTracking().Where(t => subjectIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        foreach (var e in await db.HrEmployees.AsNoTracking().Where(e => subjectIds.Contains(e.Id)).ToListAsync(ct))
            names.TryAdd(e.Id, e.FullName);

        // Respondent names only when the poll is not anonymous.
        var respondentNames = new Dictionary<Guid, string>();
        if (!poll.Anonymous)
        {
            var studentIds = responses.Select(r => r.StudentId).Where(s => s is not null).Distinct().ToList();
            var teacherIds = responses.Select(r => r.TeacherId).Where(s => s is not null).Distinct().ToList();
            var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
            var teachers = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
            foreach (var r in responses)
                respondentNames[r.Id] = r.StudentId is { } s ? $"{students.GetValueOrDefault(s, "")} (ota-onasi)"
                    : r.TeacherId is { } t ? teachers.GetValueOrDefault(t, "") : "";
        }

        var questions = new List<PollQuestionResultDto>();
        foreach (var q in poll.Questions.OrderBy(q => q.Position))
        {
            var answers = responses.SelectMany(r => r.Answers.Where(a => a.QuestionId == q.Id).Select(a => (r, a))).ToList();
            var scores = answers.Where(x => x.a.Score is not null).Select(x => x.a.Score!.Value).ToList();
            var (nps, promoters, passives, detractors) = q.Kind == PollQuestionKind.Nps ? NpsOf(scores) : (null, 0, 0, 0);
            double? average = scores.Count == 0 ? null : Math.Round(scores.Average(), 2);

            var distribution = q.Kind switch
            {
                PollQuestionKind.Nps => Enumerable.Range(0, 11).Select(s => new PollCountDto(s.ToString(CultureInfo.InvariantCulture), scores.Count(x => x == s))).ToList(),
                PollQuestionKind.Stars => Enumerable.Range(1, 5).Select(s => new PollCountDto(s.ToString(CultureInfo.InvariantCulture), scores.Count(x => x == s))).ToList(),
                PollQuestionKind.Choice => OptionsOf(q).Select(o => new PollCountDto(o, answers.Count(x => x.a.Choice == o))).ToList(),
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

            var texts = q.Kind != PollQuestionKind.Text ? [] : answers.Where(x => x.a.Text is not null)
                .OrderByDescending(x => x.r.SubmittedAt).Take(500)
                .Select(x => new PollTextAnswerDto(x.a.Text!, x.a.SubjectId is { } sid ? names.GetValueOrDefault(sid) : null,
                    x.r.ClassName, respondentNames.GetValueOrDefault(x.r.Id), x.r.SubmittedAt))
                .ToList();

            questions.Add(new PollQuestionResultDto(q.Id, q.Kind, q.Text, q.Target, answers.Count, average, nps,
                promoters, passives, detractors, distribution, subjects, texts));
        }
        return new PollResultsDto(poll.Id, poll.Title, poll.Audience, poll.Anonymous, responses.Count,
            string.IsNullOrWhiteSpace(className) ? null : className, classes, questions);
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

    public static async Task<IReadOnlyCollection<long>> RecipientsAsync(IAppDbContext db, string audience, CancellationToken ct = default)
    {
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
