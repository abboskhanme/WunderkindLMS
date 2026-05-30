using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Application.Services;
using System.Security.Claims;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'qituvchi ilovasi uchun API — faqat "teacher" roli (web admin'ga tegishli emas).
/// Har amal tokendagi foydalanuvchidan o'qituvchini aniqlaydi va faqat o'ziga tegishli
/// ma'lumotni ko'rsatadi/o'zgartiradi. Jurnalga yozish faqat o'qituvchining o'zi dars
/// beradigan sinf+fan uchun ruxsat etiladi (boshqasiga 403).
/// </summary>
[ApiController]
[Authorize(Roles = "teacher")]
[Route("api/teacher")]
public class TeacherPortalController(
    AppDbContext db, ChatService chat, IWebHostEnvironment env, ReferenceCache refCache) : ControllerBase
{
    /// <summary>Tokendagi foydalanuvchi id'si bo'yicha joriy o'qituvchini topadi.</summary>
    private async Task<Teacher?> Me()
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return uid is null ? null : await db.Teachers.FirstOrDefaultAsync(t => t.UserId == uid);
    }

    /// <summary>O'qituvchi shu sinfda shu fanni o'qitadimi (jadval template'lari bo'yicha)?</summary>
    private async Task<bool> Teaches(string teacherId, string classId, string subjectId)
    {
        var templates = await db.ScheduleTemplates.Include(t => t.Lessons)
            .Where(t => t.ClassId == classId).ToListAsync();
        return templates.SelectMany(t => t.Lessons)
            .Any(l => l.TeacherId == teacherId && l.SubjectId == subjectId);
    }

    /// <summary>O'qituvchi shu sinfda umuman dars beradimi yoki sinf rahbarimi?</summary>
    private async Task<bool> TeachesClass(Teacher teacher, string classId)
    {
        var cls = await db.Classes.FindAsync(classId);
        if (cls is null) return false;
        if (!string.IsNullOrEmpty(teacher.HomeroomClass) && teacher.HomeroomClass == cls.Name) return true;
        var templates = await db.ScheduleTemplates.Include(t => t.Lessons)
            .Where(t => t.ClassId == classId).ToListAsync();
        return templates.SelectMany(t => t.Lessons).Any(l => l.TeacherId == teacher.Id);
    }

    // ---------- Profil ----------

    [HttpGet("me")]
    public async Task<ActionResult<TeacherProfileDto>> Profile()
    {
        var t = await Me();
        if (t is null) return NotFound();
        var user = t.UserId is null ? null : await db.Users.FindAsync(t.UserId);
        var names = (await db.Subjects.Where(s => t.SubjectIds.Contains(s.Id)).ToListAsync())
            .Select(s => new SubjectDto(s.Id, s.Name))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new TeacherProfileDto(t.Id, t.FullName, user?.Email ?? "", t.HomeroomClass, names, t.Permissions);
    }

    [HttpGet("meta")]
    public async Task<ActionResult<PortalMetaDto>> Meta() => await refCache.MetaAsync();

    // ---------- Dars beradigan sinflar ----------

    [HttpGet("classes")]
    public async Task<ActionResult<IEnumerable<TeacherClassDto>>> Classes()
    {
        var t = await Me();
        if (t is null) return NotFound();

        var templates = await db.ScheduleTemplates.Include(x => x.Lessons).ToListAsync();
        var subjectNames = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);
        var classes = await db.Classes.ToListAsync();

        // O'qituvchi qaysi sinfda qaysi fanlarni o'qitishini jadval template'laridan yig'amiz.
        var taught = new Dictionary<string, HashSet<string>>(); // classId -> subjectIds
        foreach (var tpl in templates)
            foreach (var l in tpl.Lessons.Where(l => l.TeacherId == t.Id))
            {
                if (!taught.TryGetValue(tpl.ClassId, out var set))
                    taught[tpl.ClassId] = set = new();
                set.Add(l.SubjectId);
            }

        var result = new List<TeacherClassDto>();
        foreach (var cls in classes)
        {
            var isHomeroom = !string.IsNullOrEmpty(t.HomeroomClass) && t.HomeroomClass == cls.Name;
            taught.TryGetValue(cls.Id, out var subjIds);
            if (!isHomeroom && (subjIds is null || subjIds.Count == 0)) continue;
            var subjects = (subjIds ?? new())
                .Select(id => new SubjectDto(id, subjectNames.GetValueOrDefault(id, "")))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            result.Add(new TeacherClassDto(cls.Id, cls.Name, cls.Grade, isHomeroom, subjects));
        }
        return result.OrderBy(c => c.Grade).ThenBy(c => c.ClassName).ToList();
    }

    // ---------- Jadval ----------

    [HttpGet("schedule")]
    public async Task<ActionResult<IEnumerable<TeacherLessonDto>>> Schedule(
        [FromQuery] int? quarter, [FromQuery] int? week)
    {
        var t = await Me();
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Schedule)) return Forbid();

        var (curQ, curW) = await PortalSchedule.CurrentQuarterWeekAsync(db);
        var q = quarter ?? curQ;
        var w = week ?? curW;

        var assignments = await db.WeekAssignments
            .Where(x => x.Quarter == q && x.Week == w && x.TemplateId != null).ToListAsync();
        var templateIds = assignments.Select(a => a.TemplateId!).Distinct().ToList();
        var templates = await db.ScheduleTemplates.Include(x => x.Lessons)
            .Where(x => templateIds.Contains(x.Id)).ToListAsync();
        var classes = await db.Classes.ToDictionaryAsync(c => c.Id, c => c.Name);
        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);
        var times = await db.LessonTimes.ToDictionaryAsync(x => x.Period);

        var result = new List<TeacherLessonDto>();
        foreach (var a in assignments)
        {
            var tpl = templates.FirstOrDefault(x => x.Id == a.TemplateId);
            if (tpl is null) continue;
            foreach (var l in tpl.Lessons.Where(l => l.TeacherId == t.Id))
            {
                times.TryGetValue(l.Period, out var lt);
                result.Add(new TeacherLessonDto(
                    l.Day, l.Period, lt?.StartTime, lt?.EndTime,
                    a.ClassId, classes.GetValueOrDefault(a.ClassId, ""),
                    l.SubjectId, subjects.GetValueOrDefault(l.SubjectId, ""), l.SubGroup));
            }
        }
        return result.OrderBy(r => r.Day).ThenBy(r => r.Period).ToList();
    }

    // ---------- Maosh (faqat o'ziniki) ----------

    [HttpGet("salary")]
    public async Task<ActionResult<SalaryLedgerDto>> Salary([FromQuery] string? from, [FromQuery] string? to)
    {
        var t = await Me();
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Salary)) return Forbid();
        return await SalaryLedger.BuildAsync(db, t, from, to);
    }

    // ---------- Jurnal (faqat o'zi dars beradigan sinf+fan) ----------

    [HttpGet("journal/students")]
    public async Task<ActionResult<IEnumerable<StudentDto>>> JournalStudents([FromQuery] string classId)
    {
        var t = await Me();
        if (t is null) return NotFound();
        if (!t.Permissions.Contains(TeacherPermissions.Journal) || !await TeachesClass(t, classId))
            return Forbid();

        var cls = await db.Classes.FindAsync(classId);
        if (cls is null) return NotFound();
        return await db.Students.Where(s => s.ClassName == cls.Name)
            .OrderBy(s => s.FullName)
            .Select(s => new StudentDto(
                s.Id, s.FullName, s.BirthDate, s.Address, s.Gender,
                s.ParentFullName, s.ParentPhone, s.ClassName, s.EnrollmentDate, s.Balance,
                s.DiscountPct, s.DiscountAmount, s.DiscountNote, s.SubGroup,
                s.LastName, s.FirstName, s.MiddleName, s.BirthCertificateUrl,
                s.ParentLastName, s.ParentFirstName, s.ParentMiddleName, s.ParentPassportUrl,
                s.IsArchived, s.ArchivedAt, s.ArchiveReason))
            .ToListAsync();
    }

    [HttpGet("journal/columns")]
    public async Task<ActionResult<IEnumerable<JournalColumnDto>>> Columns(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
    {
        if (!await Authorized(classId, subjectId)) return Forbid();
        return await JournalService.ComputeColumnsAsync(db, classId, subjectId, quarter);
    }

    [HttpGet("journal")]
    public async Task<ActionResult<IEnumerable<JournalEntryDto>>> Entries(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
    {
        if (!await Authorized(classId, subjectId)) return Forbid();
        return await JournalService.GetEntriesAsync(db, classId, subjectId, quarter);
    }

    [HttpPut("journal")]
    public async Task<IActionResult> SetEntry(SetJournalEntryRequest req)
    {
        if (!await Authorized(req.ClassId, req.SubjectId)) return Forbid();
        await JournalService.SetEntryAsync(db, req);
        return NoContent();
    }

    [HttpDelete("journal")]
    public async Task<IActionResult> ClearEntry(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter,
        [FromQuery] string studentId, [FromQuery] string date, [FromQuery] int period)
    {
        if (!await Authorized(classId, subjectId)) return Forbid();
        await JournalService.ClearEntryAsync(db, classId, subjectId, quarter, studentId, date, period);
        return NoContent();
    }

    [HttpGet("journal/notes")]
    public async Task<ActionResult<IEnumerable<JournalTopicDto>>> Notes(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
    {
        if (!await Authorized(classId, subjectId)) return Forbid();
        return await JournalService.GetNotesAsync(db, classId, subjectId, quarter);
    }

    [HttpPut("journal/notes")]
    public async Task<IActionResult> SetNote(SetLessonNoteRequest req)
    {
        if (!await Authorized(req.ClassId, req.SubjectId)) return Forbid();
        await JournalService.SetNoteAsync(db, req);
        return NoContent();
    }

    [HttpGet("journal/quarter-grades")]
    public async Task<ActionResult<IEnumerable<QuarterGradeRowDto>>> QuarterGrades(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
    {
        if (!await Authorized(classId, subjectId)) return Forbid();
        return await JournalService.GetQuarterGradesAsync(db, classId, subjectId, quarter);
    }

    [HttpPut("journal/quarter-grades")]
    public async Task<IActionResult> SetQuarterGrade(SetQuarterGradeRequest req)
    {
        if (!await Authorized(req.ClassId, req.SubjectId)) return Forbid();
        // Chorak bahosini kiritish FAQAT admin ochgan chorak uchun ruxsat etiladi.
        var q = await db.Quarters.FirstOrDefaultAsync(x => x.Quarter == req.Quarter);
        if (q is null || !q.GradesOpen)
            return StatusCode(403, new { message = "Bu chorak uchun baho kiritish yopiq — administrator ochishi kerak." });
        await JournalService.SetQuarterGradeAsync(db, req);
        return NoContent();
    }

    /// <summary>Joriy o'qituvchida shu bo'limga (perm) ruxsat bormi.</summary>
    private async Task<bool> HasPerm(string perm)
    {
        var t = await Me();
        return t is not null && t.Permissions.Contains(perm);
    }

    /// <summary>Jurnal ruxsati + shu sinf+fanga dars beradimi.</summary>
    private async Task<bool> Authorized(string classId, string subjectId)
    {
        var t = await Me();
        return t is not null && t.Permissions.Contains(TeacherPermissions.Journal)
            && await Teaches(t.Id, classId, subjectId);
    }

    // ---------- Guruh chati (dars beradigan sinflar + sinf rahbarligi) ----------

    /// <summary>
    /// Har bir kanal uchun oxirgi xabar vaqti (ISO) — frontend o'qilmagan xabarlarni aniqlaydi.
    /// O'qituvchining barcha kanallari (sinflar + xodimlar) qaytadi. Xabari yo'q kanal uchun null.
    /// </summary>
    [HttpGet("chat/last-messages")]
    public async Task<ActionResult<Dictionary<string, string?>>> ChatLastMessages()
    {
        if (!await HasPerm(TeacherPermissions.Messages)) return Forbid();
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var channels = await chat.ClassNamesForUserAsync(uid, "teacher");
        var lastByChannel = (await db.ChatMessages
                .Where(m => channels.Contains(m.ClassName))
                .GroupBy(m => m.ClassName)
                .Select(g => new { Name = g.Key, Last = g.Max(x => x.CreatedAt) })
                .ToListAsync())
            .ToDictionary(x => x.Name, x => (string?)x.Last.ToString("o"));
        return channels.ToDictionary(c => c, c => lastByChannel.GetValueOrDefault(c, null));
    }

    [HttpGet("chat/classes")]
    public async Task<ActionResult<IEnumerable<string>>> ChatClasses()
    {
        if (!await HasPerm(TeacherPermissions.Messages)) return Forbid();
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        return await chat.ClassNamesForUserAsync(uid, "teacher");
    }

    [HttpGet("chat/{className}")]
    public async Task<ActionResult<IEnumerable<ChatMessageDto>>> Chat(string className, [FromQuery] string? since)
    {
        if (!await HasPerm(TeacherPermissions.Messages)) return Forbid();
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        if (!await chat.CanAccessAsync(uid, "teacher", className)) return Forbid();
        return await chat.GetMessagesAsync(className, ChatService.ParseSince(since));
    }

    [HttpPost("chat/{className}")]
    public async Task<ActionResult<ChatMessageDto>> SendChat(string className, SendChatRequest req)
    {
        if (!await HasPerm(TeacherPermissions.Messages)) return Forbid();
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        if (!await chat.CanAccessAsync(uid, "teacher", className)) return Forbid();
        var dto = await chat.PostAsync(className, uid, req.Text);
        return dto is null ? BadRequest(new { message = "Xabar bo'sh" }) : dto;
    }

    // ---------- Topshiriqlar / testlar (o'qituvchi yaratadi) ----------

    /// <summary>O'qituvchining o'zi yaratgan topshiriqlari.</summary>
    [HttpGet("assignments")]
    public async Task<ActionResult<IEnumerable<AssignmentDto>>> Assignments()
    {
        if (!await HasPerm(TeacherPermissions.Assignments)) return Forbid();
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        return await AssignmentService.ListForTeacherAsync(db, uid);
    }

    [HttpPost("assignments")]
    public async Task<ActionResult<AssignmentDto>> CreateAssignment(SaveAssignmentRequest req)
    {
        if (!await HasPerm(TeacherPermissions.Assignments)) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest(new { message = "Topshiriq nomi kerak" });
        if (req.ClassIds is null || req.ClassIds.Count == 0)
            return BadRequest(new { message = "Kamida bitta sinf tanlang" });
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        return await AssignmentService.CreateAsync(db, uid, req);
    }

    [HttpPut("assignments/{id}")]
    public async Task<IActionResult> UpdateAssignment(string id, SaveAssignmentRequest req)
    {
        if (!await HasPerm(TeacherPermissions.Assignments)) return Forbid();
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var a = await db.Assignments.FindAsync(id);
        if (a is null) return NotFound();
        if (a.CreatedByUserId != uid) return Forbid(); // faqat o'zinikini tahrirlaydi
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest(new { message = "Topshiriq nomi kerak" });
        await AssignmentService.UpdateAsync(db, id, req);
        return NoContent();
    }

    [HttpDelete("assignments/{id}")]
    public async Task<IActionResult> DeleteAssignment(string id)
    {
        if (!await HasPerm(TeacherPermissions.Assignments)) return Forbid();
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var a = await db.Assignments.FindAsync(id);
        if (a is null) return NotFound();
        if (a.CreatedByUserId != uid) return Forbid();
        await AssignmentService.DeleteAsync(db, id);
        return NoContent();
    }

    /// <summary>Topshiriq materiali sifatida fayl yuklash (PDF/rasm/doc, maks ~20MB).</summary>
    [HttpPost("uploads")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<UploadedFileDto>> Upload(IFormFile file)
    {
        if (!await HasPerm(TeacherPermissions.Assignments)) return Forbid();
        if (Application.Services.UploadGuard.Validate(file) is { } error)
            return BadRequest(new { message = error });

        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var stored = Application.Services.UploadGuard.SafeName(file);
        await using (var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored)))
            await file.CopyToAsync(fs);

        return new UploadedFileDto(file.FileName, $"/uploads/{stored}", file.Length, file.ContentType ?? "");
    }

    /// <summary>Topshiriq natijalari — kim bajardi/bajarmadi (faqat o'zining topshirig'i).</summary>
    [HttpGet("assignments/{id}/results")]
    public async Task<ActionResult<AssignmentResultDto>> AssignmentResults(string id)
    {
        if (!await HasPerm(TeacherPermissions.Assignments)) return Forbid();
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var a = await db.Assignments.FindAsync(id);
        if (a is null) return NotFound();
        if (a.CreatedByUserId != uid) return Forbid();
        var res = await AssignmentService.GetResultsAsync(db, id);
        return res is null ? NotFound() : res;
    }

    /// <summary>O'quvchining bajarish holatini belgilash (bajardi/bajarmadi + ixtiyoriy ball).</summary>
    [HttpPut("assignments/{id}/submissions/{studentId}")]
    public async Task<IActionResult> SetSubmission(string id, string studentId, SetSubmissionRequest req)
    {
        if (!await HasPerm(TeacherPermissions.Assignments)) return Forbid();
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var a = await db.Assignments.FindAsync(id);
        if (a is null) return NotFound();
        if (a.CreatedByUserId != uid) return Forbid();
        await AssignmentService.SetSubmissionAsync(db, id, studentId, req.Completed, req.Score);
        return NoContent();
    }

    /// <summary>Topshiriq turlari (faqat o'qish) — topshiriq formasidagi dropdown uchun.</summary>
    [HttpGet("assignment-types")]
    public async Task<ActionResult<IEnumerable<AssignmentTypeDto>>> AssignmentTypes()
    {
        if (!await HasPerm(TeacherPermissions.Assignments)) return Forbid();
        return await db.AssignmentTypes.Select(t => new AssignmentTypeDto(t.Id, t.Name)).ToListAsync();
    }

    // ---------- Fan progresi (o'zi o'tgan darslar) ----------

    /// <summary>
    /// O'qituvchining o'tilgan darslar progresi — umumiy (o'tilgan/reja) + har bir
    /// (sinf, fan, guruh) kesimi bo'yicha yoyilma. Reja jadvaldan, o'tilgan jurnaldan ("dars o'tildi").
    /// </summary>
    [HttpGet("progress")]
    public async Task<ActionResult<TeacherProgressDto>> Progress([FromQuery] int? quarter)
    {
        var t = await Me();
        if (t is null) return NotFound();
        var (curQ, _) = await PortalSchedule.CurrentQuarterWeekAsync(db);
        return await SubjectProgressService.ForTeacherAsync(db, t.Id, quarter ?? curQ);
    }

    // ---------- Taklif va shikoyatlar (o'qituvchi → admin) ----------

    /// <summary>
    /// O'qituvchi taklif yoki shikoyat yuboradi (matn + ixtiyoriy rasm). Admin/superadmin
    /// "Taklif va shikoyatlar" bo'limida ko'radi (yuboruvchi = o'qituvchi).
    /// </summary>
    [HttpPost("feedback")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> SubmitFeedback(
        [FromForm] string type, [FromForm] string text, IFormFile? image)
    {
        var t = await Me();
        if (t is null) return NotFound();
        var body = (text ?? "").Trim();
        if (body.Length == 0) return BadRequest(new { message = "Matn bo'sh" });
        if (image is not null && Application.Services.UploadGuard.Validate(image) is { } imgError)
            return BadRequest(new { message = imgError });

        string? imageUrl = null;
        if (image is { Length: > 0 })
        {
            var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
            System.IO.Directory.CreateDirectory(dir);
            var stored = Application.Services.UploadGuard.SafeName(image);
            await using var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored));
            await image.CopyToAsync(fs);
            imageUrl = $"/uploads/{stored}";
        }

        db.Feedbacks.Add(new Feedback
        {
            StudentId = "",
            ParentName = "",
            SenderRole = "teacher",
            SenderName = t.FullName,
            TeacherId = t.Id,
            Type = type == "complaint" ? "complaint" : "suggestion",
            Text = body,
            ImageUrl = imageUrl,
            CreatedAt = DateTime.UtcNow,
            Status = "new",
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- LMS (Ta'lim) — FAQAT KO'RISH + progress ----------
    // O'qituvchi LMS kontentini yaratmaydi (uni admin qiladi); faqat o'zi dars beradigan
    // (yoki rahbarlik qiladigan) sinflarning materialini va o'quvchilar tugatishini ko'radi.

    /// <summary>O'qituvchi dars beradigan/rahbarlik qiladigan sinflar id'lari (jadval + rahbarlik).</summary>
    private async Task<HashSet<string>> TaughtClassIdsAsync(Teacher t)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var templates = await db.ScheduleTemplates.Include(x => x.Lessons).ToListAsync();
        foreach (var tpl in templates)
            if (tpl.Lessons.Any(l => l.TeacherId == t.Id))
                ids.Add(tpl.ClassId);
        if (!string.IsNullOrEmpty(t.HomeroomClass))
        {
            var hc = await db.Classes.FirstOrDefaultAsync(c => c.Name == t.HomeroomClass);
            if (hc is not null) ids.Add(hc.Id);
        }
        return ids;
    }

    /// <summary>O'qituvchi sinflaridagi LMS fanlar. ?classId= bilan bitta sinfga filtrlash mumkin.</summary>
    [HttpGet("lms/subjects")]
    public async Task<ActionResult<IEnumerable<LmsSubjectDto>>> LmsSubjects([FromQuery] string? classId)
    {
        var t = await Me();
        if (t is null) return NotFound();

        var taught = await TaughtClassIdsAsync(t);
        if (!string.IsNullOrEmpty(classId))
        {
            if (!taught.Contains(classId)) return Forbid();
            taught = new HashSet<string>(StringComparer.Ordinal) { classId };
        }

        var subjects = await db.LmsSubjects.Include(s => s.Topics)
            .Where(s => taught.Contains(s.ClassId))
            .OrderBy(s => s.CreatedAt).ToListAsync();
        var classNames = await db.Classes.Where(c => taught.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        return subjects.Select(s => new LmsSubjectDto(
            s.Id, s.ClassId, classNames.GetValueOrDefault(s.ClassId, ""),
            s.Title, s.Description, s.UnlockMode, s.BatchSize,
            s.Topics.Count, s.CreatedAt.ToString("o"))).ToList();
    }

    /// <summary>Fanning mavzulari (to'liq kontent — o'qituvchiga hammasi ochiq). Tugatgan o'quvchi soni bilan.</summary>
    [HttpGet("lms/subjects/{subjectId}/topics")]
    public async Task<ActionResult<IEnumerable<LmsTopicDto>>> LmsTopics(string subjectId)
    {
        var t = await Me();
        if (t is null) return NotFound();
        var subject = await db.LmsSubjects.FindAsync(subjectId);
        if (subject is null) return NotFound();
        if (!(await TaughtClassIdsAsync(t)).Contains(subject.ClassId)) return Forbid();

        var topics = await db.LmsTopics.Include(x => x.Materials)
            .Where(x => x.SubjectId == subjectId).OrderBy(x => x.Order).ToListAsync();
        var topicIds = topics.Select(x => x.Id).ToList();
        var completedMap = (await db.LmsProgresses
                .Where(p => topicIds.Contains(p.TopicId))
                .GroupBy(p => p.TopicId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.Id, x => x.Count);

        return topics.Select(x => new LmsTopicDto(
            x.Id, x.SubjectId, x.Title, x.Description, x.VideoUrl, x.TextContent, x.Order,
            x.Materials.Select(m => new LmsMaterialRowDto(m.Id, m.Name, m.Url, m.Size, m.ContentType)).ToList(),
            completedMap.GetValueOrDefault(x.Id, 0))).ToList();
    }

    /// <summary>Fan bo'yicha o'quvchilar progress matritsasi: kim qaysi mavzuni tugatgan.</summary>
    [HttpGet("lms/subjects/{subjectId}/progress")]
    public async Task<ActionResult<LmsProgressReportDto>> LmsProgress(string subjectId)
    {
        var t = await Me();
        if (t is null) return NotFound();
        var subject = await db.LmsSubjects.FindAsync(subjectId);
        if (subject is null) return NotFound();
        if (!(await TaughtClassIdsAsync(t)).Contains(subject.ClassId)) return Forbid();

        var cls = await db.Classes.FindAsync(subject.ClassId);
        if (cls is null) return NotFound();

        var topics = await db.LmsTopics.Where(x => x.SubjectId == subjectId)
            .OrderBy(x => x.Order).ToListAsync();
        var topicIds = topics.Select(x => x.Id).ToList();

        var students = await db.Students
            .Where(s => s.ClassName == cls.Name && !s.IsArchived)
            .OrderBy(s => s.FullName).ToListAsync();
        var studentIds = students.Select(s => s.Id).ToList();

        var byStudent = (await db.LmsProgresses
                .Where(p => topicIds.Contains(p.TopicId) && studentIds.Contains(p.StudentId))
                .ToListAsync())
            .GroupBy(p => p.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TopicId).ToList());

        return new LmsProgressReportDto(
            topics.Select(x => new LmsTopicBriefDto(x.Id, x.Title, x.Order)).ToList(),
            students.Select(s =>
            {
                var done = byStudent.GetValueOrDefault(s.Id, new List<string>());
                return new LmsStudentProgressDto(s.Id, s.FullName, done, done.Count, topics.Count);
            }).ToList());
    }
}
