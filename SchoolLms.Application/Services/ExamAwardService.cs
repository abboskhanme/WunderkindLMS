using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  TEST NATIJASI BO'YICHA XABAR — eng yaxshilarga tashakkurnoma, eng pastlarga ogohlantirish
//  (mijoz, 2026-10-15: "Oylik testni eng top va bottom o'quvchilarining accountiga xabar jo'natish
//  (tashakkurnoma diplom / ogohlantirish) — nechta o'quvchiga jo'natish sonini qo'lda kiritishli qilish").
//  docs/modules/exam-awards.md
//
//  KIMGA — imtihonda bahosi bor (foiz bor, kelmagan/bekor emas) o'quvchilar foiz bo'yicha, teng foizda
//  ball bo'yicha tartiblanadi. Eng yaxshi N ta va eng past M ta — N va M ni xodim qo'lda kiritadi, butun
//  imtihon yoki har sinf alohida. Teng natijalar bitta o'rinni bo'lishadi va chegarada hammasi kiradi
//  (3 ta so'ralgan, 3-o'rinda ikkitasi — 4 ta). Bitta o'quvchi ikkala ro'yxatga tushmaydi.
//
//  QANDAY — o'quvchining Telegram chatlariga (telegram_registrations: ota-ona va o'quvchining o'z
//  akkaunti), matn shablonidan; eng yaxshilarga ixtiyoriy "Tashakkurnoma" PDF. Har xabar — Broadcast
//  (o'quvchining "Maktab e'lonlari" lentasida va Xabarlar registrida kimga yetgani bilan). Bir imtihon,
//  bir o'quvchi, bir tur — bir marta (exam_award_notices).
// ===========================================================================

public sealed partial class ExamAwardService(IAppDbContext db, TelegramService telegram, ILogger<ExamAwardService> logger)
{
    public const int MaxCount = 50;
    public const int MaxTextLength = 2000;

    public static readonly IReadOnlyList<string> Tokens =
        ["{fish}", "{ism}", "{sinf}", "{imtihon}", "{sana}", "{ball}", "{foiz}", "{orin}"];

    public const string DefaultTopText =
        "🏆 **Tashakkurnoma**\n\n" +
        "Hurmatli ota-ona! Farzandingiz {fish} ({sinf}) {sana} kuni o'tkazilgan «{imtihon}» da {ball} ball ({foiz}) " +
        "to'plab, {orin}-o'rinni egalladi. Wunderkind maktabi jamoasi tabriklaydi va bundan keyin ham yuksak natijalar tilaydi!";

    public const string DefaultBottomText =
        "⚠️ **Ogohlantirish**\n\n" +
        "Hurmatli ota-ona! Farzandingiz {fish} ({sinf}) {sana} kuni o'tkazilgan «{imtihon}» da {ball} ball ({foiz}) " +
        "to'pladi — bu eng past natijalardan biri. Iltimos, farzandingizning tayyorgarligiga e'tibor qarating va " +
        "zarur bo'lsa sinf rahbari bilan bog'laning.";

    private sealed record Scored(
        string StudentId, string FullName, string FirstName, string ClassName,
        decimal? Total, decimal? Max, decimal Percent);

    private sealed record Picked(Scored Pupil, string Kind, int Rank);

    // =====================================================================
    //  Preview
    // =====================================================================

    public async Task<(ExamAwardPreviewDto? Value, string? Error)> PreviewAsync(
        string examId, int top, int bottom, bool perClass, CancellationToken ct = default)
    {
        if (CountError(top, bottom) is { } bad) return (null, bad);
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId, ct);
        if (exam is null) return (null, NotFound);

        var scored = await ScoredAsync(examId, ct);
        var picked = Pick(scored, top, bottom, perClass);
        var ids = scored.Select(s => s.StudentId).ToList();
        var chats = await ChatCountsAsync(ids, ct);
        var sent = (await db.ExamAwardNotices.AsNoTracking().Where(n => n.ExamId == examId)
                .Select(n => new { n.StudentId, n.Kind }).ToListAsync(ct))
            .Select(n => (n.StudentId, n.Kind)).ToHashSet();

        ExamAwardRowDto Row(Picked p) => new(
            p.Pupil.StudentId, p.Pupil.FullName, p.Pupil.ClassName, p.Rank, p.Pupil.Total, p.Pupil.Max,
            p.Pupil.Percent, chats.GetValueOrDefault(p.Pupil.StudentId), sent.Contains((p.Pupil.StudentId, p.Kind)));

        var groups = perClass
            ? scored.GroupBy(s => s.ClassName, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new ExamAwardGroupDto(g.Key, g.Count(),
                    [.. picked.Where(p => p.Kind == ExamAwardKinds.Top && p.Pupil.ClassName == g.Key).Select(Row)],
                    [.. picked.Where(p => p.Kind == ExamAwardKinds.Bottom && p.Pupil.ClassName == g.Key).Select(Row)]))
                .ToList()
            : [new ExamAwardGroupDto(null, scored.Count,
                [.. picked.Where(p => p.Kind == ExamAwardKinds.Top).Select(Row)],
                [.. picked.Where(p => p.Kind == ExamAwardKinds.Bottom).Select(Row)])];

        return (new ExamAwardPreviewDto(
            exam.Id, exam.Title, DateOf(exam), scored.Count, top, bottom, perClass, groups,
            DefaultTopText, DefaultBottomText, Tokens, telegram.IsConfigured), null);
    }

    // =====================================================================
    //  Send
    // =====================================================================

    public async Task<(ExamAwardSendResultDto? Value, string? Error)> SendAsync(
        string examId, ExamAwardSendRequest req, string userId, CancellationToken ct = default)
    {
        if (CountError(req.Top, req.Bottom) is { } bad) return (null, bad);
        if (req.Top == 0 && req.Bottom == 0) return (null, "Nechta o'quvchiga yuborilishini kiriting");
        var topText = (req.TopText ?? "").Trim();
        var bottomText = (req.BottomText ?? "").Trim();
        if (req.Top > 0 && topText.Length == 0) return (null, "Tashakkurnoma matnini kiriting");
        if (req.Bottom > 0 && bottomText.Length == 0) return (null, "Ogohlantirish matnini kiriting");
        if (topText.Length > MaxTextLength || bottomText.Length > MaxTextLength)
            return (null, $"Matn {MaxTextLength} belgidan oshmasin");
        if (!telegram.IsConfigured) return (null, "Telegram bot sozlanmagan — Sozlamalar → Telegram");

        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId, ct);
        if (exam is null) return (null, NotFound);

        var excluded = (req.ExcludeStudentIds ?? []).ToHashSet(StringComparer.Ordinal);
        var picked = Pick(await ScoredAsync(examId, ct), req.Top, req.Bottom, req.PerClass)
            .Where(p => !excluded.Contains(p.Pupil.StudentId)).ToList();
        var already = (await db.ExamAwardNotices.Where(n => n.ExamId == examId)
                .Select(n => new { n.StudentId, n.Kind }).ToListAsync(ct))
            .Select(n => (n.StudentId, n.Kind)).ToHashSet();
        var skipped = picked.Count(p => already.Contains((p.Pupil.StudentId, p.Kind)));
        picked = picked.Where(p => !already.Contains((p.Pupil.StudentId, p.Kind))).ToList();

        var ids = picked.Select(p => p.Pupil.StudentId).Distinct().ToList();
        var regs = (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => ids.Contains(r.StudentId) && r.TeacherId == null)
                .Select(r => new { r.StudentId, r.ChatId, r.ParentName }).ToListAsync(ct))
            .GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.DistinctBy(r => r.ChatId).ToList());
        var sender = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        var school = await db.SchoolMeta.AsNoTracking().Select(m => m.Name).FirstOrDefaultAsync(ct);
        var date = DateOf(exam);

        int topSent = 0, bottomSent = 0, delivered = 0, noTelegram = 0, failed = 0;
        foreach (var p in picked)
        {
            var isTop = p.Kind == ExamAwardKinds.Top;
            var template = isTop ? topText : bottomText;
            var values = Values(p, exam.Title, date);
            var html = Fill(TelegramHtml.FromMarkup(template), values, TelegramHtml.Escape);
            var chats = regs.GetValueOrDefault(p.Pupil.StudentId) ?? [];
            var withDiploma = isTop && req.Diploma;
            var pdf = withDiploma
                ? new ExamDiplomaDocument(new ExamDiplomaModel(
                    string.IsNullOrWhiteSpace(school) ? "Maktab" : school, p.Pupil.FullName, p.Pupil.ClassName,
                    exam.Title, date, p.Rank, values["{ball}"], values["{foiz}"], AppClock.Today)).Render()
                : null;

            var recipients = new List<BroadcastRecipient>();
            var reached = 0;
            foreach (var chat in chats)
            {
                var ok = await telegram.SendRichAsync(chat.ChatId, html, ct);
                if (ok && pdf is not null)
                    await telegram.SendDocumentAsync(chat.ChatId, pdf, "Tashakkurnoma.pdf", $"Tashakkurnoma — {p.Pupil.FullName}", ct);
                if (ok) reached++;
                recipients.Add(new BroadcastRecipient
                {
                    StudentId = p.Pupil.StudentId, ChatId = chat.ChatId, ParentName = chat.ParentName,
                    Status = ok ? BroadcastDelivery.Sent : BroadcastDelivery.Failed,
                });
            }
            if (chats.Count == 0)
                recipients.Add(new BroadcastRecipient { StudentId = p.Pupil.StudentId, Status = BroadcastDelivery.NoTelegram });

            var broadcast = new Broadcast
            {
                ClassName = $"{(isTop ? "Tashakkurnoma" : "Ogohlantirish")} — {exam.Title}",
                // The feed shows the markup; the pupil's own values are already in it.
                Text = Fill(template, values, v => v),
                SenderUserId = userId,
                SenderName = sender ?? "Administrator",
                CreatedAt = AppClock.Now,
                RecipientCount = chats.Count,
                SentCount = reached,
            };
            db.Broadcasts.Add(broadcast);
            foreach (var r in recipients) r.BroadcastId = broadcast.Id;
            db.BroadcastRecipients.AddRange(recipients);
            db.ExamAwardNotices.Add(new ExamAwardNotice
            {
                ExamId = examId, StudentId = p.Pupil.StudentId, Kind = p.Kind, Rank = p.Rank, Percent = p.Pupil.Percent,
                BroadcastId = broadcast.Id, Delivered = reached, WithDiploma = withDiploma && reached > 0,
                SentBy = userId, SentAt = AppClock.NowInstant,
            });
            // Saved per pupil: a crash half-way must not send the first ones again on the next press.
            await db.SaveChangesAsync(ct);

            if (isTop) topSent++; else bottomSent++;
            delivered += reached;
            if (chats.Count == 0) noTelegram++;
            else if (reached == 0) failed++;
        }

        logger.LogInformation(
            "Test natijasi xabari: exam={ExamId}, top={Top}, bottom={Bottom}, chats={Delivered}, noTelegram={NoTelegram}, failed={Failed}, skipped={Skipped}",
            examId, topSent, bottomSent, delivered, noTelegram, failed, skipped);
        return (new ExamAwardSendResultDto(topSent, bottomSent, delivered, noTelegram, failed, skipped), null);
    }

    /// <summary>Messages already sent for the exam, newest first.</summary>
    public async Task<List<ExamAwardNoticeDto>> SentAsync(string examId, CancellationToken ct = default) =>
        await (from n in db.ExamAwardNotices.AsNoTracking()
               join s in db.Students.AsNoTracking() on n.StudentId equals s.Id
               join u in db.Users.AsNoTracking() on n.SentBy equals u.Id into us
               from u in us.DefaultIfEmpty()
               where n.ExamId == examId
               orderby n.SentAt descending, n.Kind, n.Rank
               select new ExamAwardNoticeDto(n.Id, n.StudentId, s.FullName, s.ClassName, n.Kind, n.Rank, n.Percent,
                   n.Delivered, n.WithDiploma, u != null ? u.FullName : "", n.SentAt))
            .ToListAsync(ct);

    public const string NotFound = "Imtihon topilmadi";

    // =====================================================================
    //  Ranking
    // =====================================================================

    private static string? CountError(int top, int bottom) =>
        top is < 0 or > MaxCount || bottom is < 0 or > MaxCount
            ? $"O'quvchilar soni 0–{MaxCount} oralig'ida bo'lsin"
            : null;

    /// <summary>Pupils of the exam who have a result: a percent, and neither absent nor cancelled.</summary>
    private async Task<List<Scored>> ScoredAsync(string examId, CancellationToken ct)
    {
        var rows = await (
            from p in db.ExamParticipants.AsNoTracking()
            join s in db.Students.AsNoTracking() on p.StudentId equals s.Id
            join c in db.Classes.AsNoTracking() on p.ClassId equals c.Id into cs
            from c in cs.DefaultIfEmpty()
            where p.ExamId == examId && p.ParticipantKind == ExamParticipantKind.Student && p.Percent != null
                  && p.Status != ExamParticipantStatus.Absent && p.Status != ExamParticipantStatus.Cancelled
                  && !s.IsArchived
            select new { s.Id, s.FullName, s.FirstName, StudentClass = s.ClassName, ClassName = c != null ? c.Name : null,
                p.TotalPoints, p.MaxPoints, p.Percent }).ToListAsync(ct);
        return [.. rows.Select(r => new Scored(r.Id, r.FullName, r.FirstName, r.ClassName ?? r.StudentClass ?? "",
            r.TotalPoints, r.MaxPoints, r.Percent!.Value))];
    }

    /// <summary>
    /// Top by percent (then points), bottom from the other end; equal results share a place and all of them are in
    /// when the place is within the count. A pupil already in the top list is never in the bottom one.
    /// </summary>
    private static List<Picked> Pick(List<Scored> scored, int top, int bottom, bool perClass)
    {
        var result = new List<Picked>();
        var groups = perClass
            ? scored.GroupBy(s => s.ClassName, StringComparer.Ordinal).Select(g => g.ToList())
            : [scored];
        foreach (var group in groups)
        {
            var best = Ranked(group.OrderByDescending(s => s.Percent).ThenByDescending(s => s.Total ?? 0)
                .ThenBy(s => s.FullName, StringComparer.Ordinal).ToList(), top);
            result.AddRange(best.Select(x => new Picked(x.Pupil, ExamAwardKinds.Top, x.Rank)));
            var inTop = best.Select(x => x.Pupil.StudentId).ToHashSet(StringComparer.Ordinal);
            var worst = Ranked(group.Where(s => !inTop.Contains(s.StudentId))
                .OrderBy(s => s.Percent).ThenBy(s => s.Total ?? 0)
                .ThenBy(s => s.FullName, StringComparer.Ordinal).ToList(), bottom);
            result.AddRange(worst.Select(x => new Picked(x.Pupil, ExamAwardKinds.Bottom, x.Rank)));
        }
        return result;
    }

    /// <summary>Competition ranking (1, 2, 2, 4) over an ordered list; keeps places ≤ <paramref name="count"/>.</summary>
    private static List<(Scored Pupil, int Rank)> Ranked(List<Scored> ordered, int count)
    {
        var list = new List<(Scored, int)>();
        if (count <= 0) return list;
        var rank = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            var same = i > 0 && ordered[i].Percent == ordered[i - 1].Percent
                             && (ordered[i].Total ?? 0) == (ordered[i - 1].Total ?? 0);
            if (!same) rank = i + 1;
            if (rank > count) break;
            list.Add((ordered[i], rank));
        }
        return list;
    }

    private async Task<Dictionary<string, int>> ChatCountsAsync(List<string> ids, CancellationToken ct) =>
        (await db.TelegramRegistrations.AsNoTracking()
            .Where(r => ids.Contains(r.StudentId) && r.TeacherId == null)
            .Select(r => new { r.StudentId, r.ChatId }).Distinct().ToListAsync(ct))
        .GroupBy(r => r.StudentId).ToDictionary(g => g.Key, g => g.Count());

    // =====================================================================
    //  Text
    // =====================================================================

    /// <summary>"dd.MM.yyyy" of the exam (manual: its date; online: the window's start).</summary>
    private static string? DateOf(Exam exam)
    {
        if (DateOnly.TryParseExact(exam.ExamDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        return exam.OpensAt?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, string> Values(Picked p, string examTitle, string? date)
    {
        static string Num(decimal? v) => v?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—";
        var ball = p.Pupil.Max is { } max ? $"{Num(p.Pupil.Total)}/{Num(max)}" : Num(p.Pupil.Total);
        return new Dictionary<string, string>
        {
            ["{fish}"] = p.Pupil.FullName,
            ["{ism}"] = string.IsNullOrWhiteSpace(p.Pupil.FirstName) ? p.Pupil.FullName : p.Pupil.FirstName,
            ["{sinf}"] = p.Pupil.ClassName,
            ["{imtihon}"] = examTitle,
            ["{sana}"] = date ?? "",
            ["{ball}"] = ball,
            ["{foiz}"] = $"{Num(p.Pupil.Percent)}%",
            ["{orin}"] = p.Rank.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Replaces every token (case-insensitive); <paramref name="encode"/> escapes values for HTML.</summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> values, Func<string, string> encode) =>
        TokenRx().Replace(template, m =>
            values.TryGetValue(m.Value.ToLowerInvariant(), out var v) ? encode(v) : m.Value);

    [GeneratedRegex(@"\{[a-z_]+\}", RegexOptions.IgnoreCase)]
    private static partial Regex TokenRx();
}
