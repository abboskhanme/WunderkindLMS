using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Xulq-atvor — the TEACHER side (docs/modules/behaviour.md, client 2026-10-15).
//
//  In EduSchool the homeroom and subject teachers record most behaviour incidents (15 of 16 homeroom
//  teachers did last year). Here the teacher records them from the lesson screen of the Mini App.
//
//  SAME GATE AS THE JOURNAL: role `teacher` and the `journal` key — the incidents are recorded where the
//  journal is kept. A pupil is reachable when the teacher reaches their class or one of their active
//  groups, or covers a lesson of theirs today (DisciplineIncidents.TeacherReachesAsync). The author is
//  ALWAYS the signed-in teacher (name and user id from the token) — nothing in the request names one.
//
//  A teacher deletes only their own entry and only on the day it was made; editing and deleting other
//  entries stays in the admin "Harakatlar" screen (`discipline` key). The parent hears about an incident
//  exactly as when the office records it — Telegram, only for reasons with NotifyParent.
// ===========================================================================

/// <summary>Behaviour incidents recorded by teachers (<c>/api/teacher/discipline</c>).</summary>
[ApiController]
[Authorize(Roles = Roles.Teacher)]
[Route("api/teacher/discipline")]
public sealed class TeacherDisciplineController(
    AppDbContext db, TelegramService telegram, ILogger<DisciplineParentNotifier> notifierLogger,
    AuditService audit)
    : ControllerBase
{
    /// <summary>How many history lines the pupil sheet shows.</summary>
    private const int RecentItems = 20;

    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    /// <summary>The signed-in teacher, or null when the account has no teacher row.</summary>
    private async Task<Teacher?> MeAsync(CancellationToken ct) =>
        Uid.Length == 0 ? null : await db.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == Uid, ct);

    /// <summary>The journal key opens the behaviour actions — they live on the lesson screen.</summary>
    private static bool MayRecord(Teacher t) => t.Permissions.Contains(TeacherPermissions.Journal);

    private DisciplineParentNotifier Notifier => new(db, telegram, notifierLogger);

    /// <summary>A teacher gives only the reasons they are an owner of (DisciplineOwners).</summary>
    private bool Owns(DisciplineReason r) => DisciplineOwners.MayUse(r, Uid, isFullAdmin: false);

    /// <summary>
    /// Active behaviour reasons with their type (positive | negative), by type then name — only those
    /// the teacher is an owner of.
    /// </summary>
    [HttpGet("reasons")]
    public async Task<ActionResult<IEnumerable<TeacherDisciplineReasonDto>>> Reasons(CancellationToken ct)
    {
        var t = await MeAsync(ct);
        if (t is null) return NotFound(new { message = "O'qituvchi topilmadi" });
        if (!MayRecord(t)) return Forbid();

        var reasons = await db.DisciplineReasons.AsNoTracking()
            .Where(r => r.IsActive && r.Points != 0)
            .OrderBy(r => r.Name).ToListAsync(ct);
        return reasons
            .Where(Owns)
            .Select(r => new TeacherDisciplineReasonDto(
                r.Id, r.Name, r.Points, DisciplineType.Of(r.Points), r.Description, r.NotifyParent, r.Period))
            .ToList();
    }

    /// <summary>
    /// Score and today's points of every pupil on a lesson roster (<paramref name="ownerId"/> — class
    /// or group) — the badges of the lesson screen in one call. The roster rule is the journal's:
    /// the teacher reaches the owner, or covers a lesson of it today.
    /// </summary>
    [HttpGet("scores")]
    public async Task<ActionResult<IEnumerable<TeacherDisciplineScoreDto>>> Scores(
        [FromQuery] string ownerId, CancellationToken ct)
    {
        var t = await MeAsync(ct);
        if (t is null) return NotFound(new { message = "O'qituvchi topilmadi" });
        if (!MayRecord(t) || string.IsNullOrWhiteSpace(ownerId)) return Forbid();
        if (!await TeacherOwnerAccess.ReachesAsync(db, t, ownerId, ct)
            && !await CoversTodayAsync(t.Id, ownerId, ct))
            return Forbid();

        var owner = await LessonRoster.OwnerAsync(db, ownerId, ct);
        if (owner is null) return NotFound(new { message = "Sinf topilmadi" });
        var ids = (await LessonRoster.ForLessonAsync(db, owner, ct: ct)).Select(s => s.Id).ToList();

        var totals = await DisciplineIncidents.TotalsAsync(db, ids, ct);
        var today = await DisciplineIncidents.TodayAsync(db, ids, ct);
        return ids.Select(id =>
        {
            var (plus, minus) = totals.GetValueOrDefault(id);
            return new TeacherDisciplineScoreDto(id, DisciplineIncidents.BaseScore + plus - minus, today.GetValueOrDefault(id));
        }).ToList();
    }

    /// <summary>One pupil: score, today's points and the recent history (manual and attendance).</summary>
    [HttpGet("points")]
    public async Task<ActionResult<TeacherStudentDisciplineDto>> Points(
        [FromQuery] string studentId, CancellationToken ct)
    {
        var t = await MeAsync(ct);
        if (t is null) return NotFound(new { message = "O'qituvchi topilmadi" });
        if (!MayRecord(t)) return Forbid();
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });
        if (!await DisciplineIncidents.TeacherReachesAsync(db, t, student, ct)) return Forbid();

        var (plus, minus) = (await DisciplineIncidents.TotalsAsync(db, [student.Id], ct)).GetValueOrDefault(student.Id);
        var today = (await DisciplineIncidents.TodayAsync(db, [student.Id], ct)).GetValueOrDefault(student.Id);

        // The same start day as the score (DisciplineIncidents.ScoreSinceAsync), so the list adds up to it.
        var since = await DisciplineIncidents.ScoreSinceAsync(db, ct);
        var manual = await db.DisciplinePoints.AsNoTracking()
            .Where(p => p.StudentId == student.Id && (since == null || string.Compare(p.CreatedAt, since) >= 0))
            .OrderByDescending(p => p.CreatedAt).Take(RecentItems).ToListAsync(ct);
        var items = manual.Select(p => new TeacherDisciplinePointDto(
            p.Id, p.ReasonName, p.Points, p.Note, p.CreatedAt, p.CreatedBy, "manual",
            DisciplineIncidents.TeacherMayDelete(p, Uid))).ToList();

        // Attendance-reason days move the score too — shown so the number adds up, never deletable here.
        var reasonRows = await db.AbsenceReasons.AsNoTracking().Where(r => r.Points != 0)
            .ToDictionaryAsync(r => r.Id, r => new { r.Name, r.Points }, ct);
        if (reasonRows.Count > 0)
        {
            var marks = await db.JournalEntries.AsNoTracking()
                .Where(e => e.StudentId == student.Id && e.ReasonId != null
                    && (since == null || string.Compare(e.Date, since) >= 0))
                .Select(e => new AttendanceDisciplineDays.Mark(e.Id, e.StudentId, e.Date, e.ReasonId!))
                .ToListAsync(ct);
            foreach (var d in AttendanceDisciplineDays.Collapse(marks))
                if (reasonRows.TryGetValue(d.ReasonId, out var r))
                    items.Add(new TeacherDisciplinePointDto(
                        d.Id, r.Name, r.Points, AttendanceDisciplineDays.Note(d.Lessons), d.Date, "", "attendance", false));
        }

        return new TeacherStudentDisciplineDto(
            student.Id, student.FullName, DisciplineIncidents.BaseScore + plus - minus, plus, minus, today,
            [.. items.OrderByDescending(i => i.CreatedAt, StringComparer.Ordinal).Take(RecentItems)],
            await DisciplineIncidents.TakenReasonIdsAsync(db, student.Id,
                await db.DisciplineReasons.AsNoTracking().Where(r => r.IsActive && r.Period != DisciplinePeriod.None).ToListAsync(ct), ct));
    }

    /// <summary>
    /// Records an incident for one of the teacher's pupils, saved at once (independent of the lesson's
    /// "Saqlash"). Only an active behaviour reason — attendance reasons are the nurse's roll-call. The
    /// parent gets a Telegram message when the reason has NotifyParent, as for the office.
    /// </summary>
    [ActiveStudent]
    [HttpPost("points")]
    public async Task<ActionResult<TeacherDisciplinePointDto>> Add(
        AddDisciplinePointRequest req, CancellationToken ct = default)
    {
        var t = await MeAsync(ct);
        if (t is null) return NotFound(new { message = "O'qituvchi topilmadi" });
        if (!MayRecord(t)) return Forbid();
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == req.StudentId, ct);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });
        if (!await DisciplineIncidents.TeacherReachesAsync(db, t, student, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = DisciplineIncidents.NotYourPupilMessage });

        var reason = await db.DisciplineReasons.FirstOrDefaultAsync(r => r.Id == req.ReasonId, ct);
        if (reason is null) return BadRequest(new { message = "Sabab tanlanmadi" });
        if (!reason.IsActive) return BadRequest(new { message = "Bu sabab faol emas" });
        if (!Owns(reason))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = DisciplineOwners.NotOwnerMessage });
        if ((await DisciplineIncidents.PeriodTakenAsync(db, reason, [student.Id], AppClock.Today, ct: ct)).Count > 0)
            return Conflict(new { message = DisciplineIncidents.PeriodTakenMessage(reason) });

        var point = new DisciplinePoint
        {
            StudentId = student.Id,
            ReasonId = reason.Id,
            ReasonName = reason.Name,
            Points = reason.Points,
            Note = (req.Note ?? "").Trim(),
            CreatedAt = AppClock.Now.ToString("o"),
            CreatedBy = t.FullName,
            CreatedByUserId = Uid,
        };
        db.DisciplinePoints.Add(point);
        await db.SaveChangesAsync(ct);

        // The message AFTER the write: a failed send never loses the entry.
        var notified = await Notifier.NotifyAsync(student, reason, point, ct);

        return new TeacherDisciplinePointDto(
            point.Id, point.ReasonName, point.Points, point.Note, point.CreatedAt, point.CreatedBy, "manual",
            true, notified);
    }

    /// <summary>Takes back the teacher's OWN entry, on the day it was made; anything else is 403.</summary>
    [HttpDelete("points/{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var t = await MeAsync(ct);
        if (t is null) return NotFound(new { message = "O'qituvchi topilmadi" });
        if (!MayRecord(t)) return Forbid();

        var point = await db.DisciplinePoints.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (point is null) return NotFound(new { message = "Yozuv topilmadi" });
        if (!DisciplineIncidents.TeacherMayDelete(point, Uid))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Faqat o'zingiz bugun qo'ygan yozuvni o'chira olasiz." });

        var studentName = await db.Students.AsNoTracking().Where(s => s.Id == point.StudentId)
            .Select(s => s.FullName).FirstOrDefaultAsync(ct) ?? point.StudentId;
        audit.Record(AuditService.EntityDisciplinePoint, point.Id, "delete",
            $"Xulq-atvor yozuvi o'chirildi: {point.ReasonName} ({(point.Points > 0 ? "+" : "")}{point.Points}) — {studentName}",
            before: new { point.ReasonId, point.ReasonName, point.Points, point.Note, point.CreatedAt, point.CreatedBy },
            studentId: point.StudentId, teacherId: t.Id);
        db.DisciplinePoints.Remove(point);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>A lesson of this class or group the teacher covers today (the substitute's rule).</summary>
    private Task<bool> CoversTodayAsync(string teacherId, string ownerId, CancellationToken ct)
    {
        var today = AppClock.Today;
        return db.LessonSubstitutions.AsNoTracking().AnyAsync(s =>
            s.CancelledAt == null && s.SubstituteTeacherId == teacherId && s.OwnerId == ownerId && s.Date == today, ct);
    }
}
