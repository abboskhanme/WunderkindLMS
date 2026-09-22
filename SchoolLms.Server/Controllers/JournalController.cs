using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Admin jurnali. Yo'ldagi/so'rovdagi <c>classId</c> — darsning EGASI: sinf
/// yoki o'quv guruhi (students-parity.md §2.1.4, G-12). Guruh darslari cut-over
/// o'chirgichi (<c>group_lessons_enabled</c>) yoqilgandagina ko'rinadi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("journal")]
[Route("api/admin/journal")]
public class JournalController(AppDbContext db, FcmService fcm) : ControllerBase
{
    /// <summary>
    /// Jurnal tanlagichi uchun egalar ro'yxati: sinflar (har doim) va o'quv
    /// guruhlari (faqat o'chirgich yoqilganda).
    ///
    /// <para>
    /// Nega jurnalning O'Z endpointi: aks holda ekran ikkita ro'yxatni
    /// (<c>classes</c> va <c>study-groups</c>) qo'shib, cut-over o'chirgichini
    /// ham O'ZI tekshirishi kerak bo'lardi — ya'ni "guruh ko'rinadimi" qoidasi
    /// brauzerda TAKRORLANARDI. Bu yerda u bir joyda va serverda.
    /// </para>
    /// </summary>
    [HttpGet("owners")]
    public async Task<ActionResult<IEnumerable<JournalOwnerDto>>> Owners(CancellationToken ct = default)
    {
        var students = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived)
            .Select(s => new { s.Id, s.ClassName })
            .ToListAsync(ct);
        var countByClassName = students
            .GroupBy(s => s.ClassName ?? "", StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var result = await db.Classes.AsNoTracking()
            .Where(c => !c.IsArchived)
            .OrderBy(c => c.Grade).ThenBy(c => c.Name)
            .Select(c => new JournalOwnerDto(
                c.Id, c.Name, LessonOwnerKind.Class, c.Grade, null, null, 0, c.Language, null))
            .ToListAsync(ct);
        // Sinf rahbari — o'qituvchi kartasidagi "sinf rahbari" maydoni (sinf NOMI bo'yicha).
        var homeroom = (await db.Teachers.AsNoTracking()
                .Where(t => !t.IsArchived && t.HomeroomClass != "")
                .Select(t => new { t.HomeroomClass, t.FullName }).ToListAsync(ct))
            .GroupBy(t => t.HomeroomClass, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.FullName)), StringComparer.Ordinal);
        result = [.. result.Select(o => o with
        {
            StudentCount = countByClassName.GetValueOrDefault(o.Name, 0),
            HomeroomTeacher = homeroom.GetValueOrDefault(o.Name),
        })];

        if (!await LessonRoster.GroupLessonsEnabledAsync(db, ct)) return result;

        var subjectNames = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var activeIds = students.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var memberCounts = (await db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.LeftOn == null)
                .Select(m => new { m.GroupId, m.StudentId }).ToListAsync(ct))
            .Where(m => activeIds.Contains(m.StudentId))
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => g.Count());

        var groups = await db.StudyGroups.AsNoTracking()
            .Where(g => !g.IsArchived).OrderBy(g => g.Name).ToListAsync(ct);
        result.AddRange(groups.Select(g => new JournalOwnerDto(
            g.Id.ToString(), g.Name, LessonOwnerKind.Group, 0,
            g.SubjectId, subjectNames.GetValueOrDefault(g.SubjectId, ""),
            memberCounts.GetValueOrDefault(g.Id, 0))));
        return result;
    }

    /// <summary>
    /// Sinf (yoki guruh) fanlari va ularni o'tadigan o'qituvchilar — Jurnal → sinf → fan
    /// oqimining ikkinchi bosqichi (EduSchool kabi, 2026-09-23). Manba — dars jadvali
    /// shablonlari: jurnal ustunlari ham aynan shulardan quriladi, ya'ni bu ro'yxatda
    /// bor fan jurnalda ham bor.
    /// </summary>
    [HttpGet("subjects")]
    public async Task<ActionResult<IEnumerable<JournalSubjectDto>>> Subjects(
        [FromQuery] string classId, CancellationToken ct = default)
    {
        var owner = await LessonRoster.OwnerAsync(db, classId, ct);
        if (owner is null) return new List<JournalSubjectDto>();

        var lessons = await db.ScheduleTemplates.AsNoTracking()
            .Where(t => t.ClassId == classId)
            .SelectMany(t => t.Lessons.Select(l => new { l.SubjectId, l.TeacherId }))
            .ToListAsync(ct);
        var pairs = lessons.Where(l => l.SubjectId != "").ToList();

        // O'quv guruhi: fani bitta, jadval hali bo'lmasa ham ro'yxat bo'sh qolmasin.
        if (owner.IsGroup && Guid.TryParse(classId, out var gid))
        {
            var group = await db.StudyGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gid, ct);
            if (group is not null)
            {
                var gTeachers = await db.StudyGroupTeachers.AsNoTracking()
                    .Where(x => x.GroupId == gid).Select(x => x.TeacherId).ToListAsync(ct);
                pairs.AddRange(gTeachers.DefaultIfEmpty("").Select(t => new { SubjectId = group.SubjectId, TeacherId = t }));
            }
        }

        var subjectNames = await db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var teacherNames = await db.Teachers.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.FullName, ct);

        return pairs
            .Where(p => subjectNames.ContainsKey(p.SubjectId))
            .GroupBy(p => p.SubjectId)
            .Select(g => new JournalSubjectDto(
                g.Key, subjectNames[g.Key],
                [.. g.Select(p => teacherNames.GetValueOrDefault(p.TeacherId))
                    .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).Distinct().Order(StringComparer.Ordinal)]))
            .OrderBy(x => x.SubjectName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Shu jurnalda (ega + fan + chorak) yozuvi bor, lekin hozir ro'yxatda YO'Q o'quvchilar —
    /// arxivlanganlar va boshqa sinfga o'tganlar. EduSchool'dagi "Arxivdagi o'quvchilar" bo'limi:
    /// ularning baholari yo'qolmaydi, faqat alohida, o'qish uchun ko'rsatiladi.
    /// </summary>
    [HttpGet("former-students")]
    public async Task<ActionResult<IEnumerable<StudentDto>>> FormerStudents(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter,
        CancellationToken ct = default)
    {
        var owner = await LessonRoster.OwnerAsync(db, classId, ct);
        if (owner is null) return new List<StudentDto>();

        var withEntries = await db.JournalEntries.AsNoTracking()
            .Where(e => e.ClassId == classId && e.SubjectId == subjectId && e.Quarter == quarter)
            .Select(e => e.StudentId).Distinct().ToListAsync(ct);
        if (withEntries.Count == 0) return new List<StudentDto>();

        var current = (await LessonRoster.ForLessonAsync(db, owner, 0, ct: ct))
            .Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var formerIds = withEntries.Where(id => !current.Contains(id)).ToList();
        if (formerIds.Count == 0) return new List<StudentDto>();

        var students = await db.Students.AsNoTracking()
            .Where(s => formerIds.Contains(s.Id))
            .OrderBy(s => s.FullName).ToListAsync(ct);
        return students.Select(s => new StudentDto(
            s.Id, s.FullName, s.BirthDate, s.Address, s.Gender,
            s.ParentFullName, s.ParentPhone, s.ClassName, s.EnrollmentDate, null,
            s.SubGroup,
            s.LastName, s.FirstName, s.MiddleName, s.BirthCertificateUrl,
            s.ParentLastName, s.ParentFirstName, s.ParentMiddleName, s.ParentPassportUrl,
            s.IsArchived, s.ArchivedAt, s.ArchiveReason)).ToList();
    }

    /// <summary>
    /// Eganing jurnal ro'yxati — <see cref="LessonRoster"/> dan. Brauzer endi
    /// butun maktab ro'yxatini sinf NOMI bo'yicha filtrlamaydi (G-12: guruh
    /// bir nechta sinfdan yig'iladi, nom bo'yicha filtr uni topa olmaydi).
    /// </summary>
    [HttpGet("students")]
    public async Task<ActionResult<IEnumerable<StudentDto>>> Students(
        [FromQuery] string classId, [FromQuery] int subGroup = 0, CancellationToken ct = default)
    {
        if (subGroup is < 0 or > 2)
            return BadRequest(new { message = "subGroup 0, 1 yoki 2 bo'lishi kerak" });

        var owner = await LessonRoster.OwnerAsync(db, classId, ct);
        if (owner is null) return new List<StudentDto>();
        if (owner.IsGroup && !await LessonRoster.GroupLessonsEnabledAsync(db, ct))
            return new List<StudentDto>();

        var students = await LessonRoster.ForLessonAsync(db, owner, subGroup, ct: ct);
        // Jurnal ro'yxatida pul KO'RSATILMAYDI — `Balance` null (P1-21).
        return students.Select(s => new StudentDto(
            s.Id, s.FullName, s.BirthDate, s.Address, s.Gender,
            s.ParentFullName, s.ParentPhone, s.ClassName, s.EnrollmentDate, null,
            s.SubGroup,
            s.LastName, s.FirstName, s.MiddleName, s.BirthCertificateUrl,
            s.ParentLastName, s.ParentFirstName, s.ParentMiddleName, s.ParentPassportUrl,
            s.IsArchived, s.ArchivedAt, s.ArchiveReason)).ToList();
    }

    /// <summary>Fanning chorakdagi darslari (sana + dars raqami). Bir kunda bir fan bir necha marta bo'lishi mumkin.</summary>
    [HttpGet("columns")]
    public async Task<ActionResult<IEnumerable<JournalColumnDto>>> GetColumns(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
        => await JournalService.ComputeColumnsAsync(db, classId, subjectId, quarter);

    /// <summary>
    /// Berilgan sanada o'tilgan darslar (ega+fan+dars raqami): ptichka yoki
    /// baho/davomat bo'lganlar. Guruh qatorlari o'chirgich o'chiq bo'lsa
    /// CHIQMAYDI — bugungi ro'yxat bir bayt ham o'zgarmaydi.
    /// </summary>
    [HttpGet("conducted")]
    public async Task<ActionResult<IEnumerable<ConductedLessonDto>>> Conducted([FromQuery] string date)
    {
        var groupsOn = await LessonRoster.GroupLessonsEnabledAsync(db);
        var fromNotes = await db.LessonNotes
            .Where(n => n.Date == date && n.Conducted)
            .Where(n => groupsOn || n.OwnerKind != LessonOwnerKind.Group)
            .Select(n => new ConductedLessonDto(n.ClassId, n.SubjectId, n.Period, n.SubGroup, n.OwnerKind))
            .ToListAsync();
        var fromEntries = await db.JournalEntries
            .Where(e => e.Date == date && (e.Grade != null || e.ReasonId != null))
            .Where(e => groupsOn || e.OwnerKind != LessonOwnerKind.Group)
            .Select(e => new ConductedLessonDto(e.ClassId, e.SubjectId, e.Period, e.SubGroup, e.OwnerKind))
            .ToListAsync();
        return fromNotes.Concat(fromEntries).Distinct().ToList();
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<JournalEntryDto>>> GetEntries(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
        => await JournalService.GetEntriesAsync(db, classId, subjectId, quarter);

    /// <summary>Bitta katakni belgilash — baho yoki davomat sababi (mavjud bo'lsa ustiga yoziladi).</summary>
    [HttpPut]
    public async Task<IActionResult> SetEntry(SetJournalEntryRequest req)
    {
        var error = await JournalService.SetEntryAsync(db, req, fcm);
        if (error is not null) return BadRequest(new { message = error });
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> ClearEntry(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter,
        [FromQuery] string studentId, [FromQuery] string date, [FromQuery] int period)
    {
        await JournalService.ClearEntryAsync(db, classId, subjectId, quarter, studentId, date, period);
        return NoContent();
    }

    /* ---------- Mavzu va uyga vazifa ---------- */

    [HttpGet("notes")]
    public async Task<ActionResult<IEnumerable<JournalTopicDto>>> GetNotes(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
        => await JournalService.GetNotesAsync(db, classId, subjectId, quarter);

    [HttpPut("notes")]
    public async Task<IActionResult> SetNote(SetLessonNoteRequest req)
    {
        var error = await JournalService.SetNoteAsync(db, req);
        if (error is not null) return BadRequest(new { message = error });
        return NoContent();
    }

    /* ---------- Mavzularni Excel'dan ommaviy yuklash (mavzu + uy vazifa) ---------- */

    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Tanlangan sinf+fan+chorak uchun mavzular shabloni (.xlsx) — jadval kunlari oldindan to'ldirilgan.</summary>
    [HttpGet("topics-template")]
    public async Task<IActionResult> TopicsTemplate(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
    {
        var bytes = await JournalService.TopicTemplateXlsxAsync(db, classId, subjectId, quarter);
        return File(bytes, XlsxMime, "mavzular_shablon.xlsx");
    }

    /// <summary>To'ldirilgan Excel'dan mavzu+uy vazifani import qiladi (darsni "o'tilgan" qilmaydi).</summary>
    [HttpPost("topics-import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<TopicImportResultDto>> TopicsImport(
        [FromForm] string classId, [FromForm] string subjectId, [FromForm] int quarter, IFormFile? file)
    {
        if (string.IsNullOrWhiteSpace(classId) || string.IsNullOrWhiteSpace(subjectId))
            return BadRequest(new { message = "Sinf va fan ko'rsatilishi shart" });
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Fayl tanlanmagan" });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Faqat .xlsx (Excel) fayl qabul qilinadi" });

        List<string[]> rows;
        try
        {
            await using var stream = file.OpenReadStream();
            rows = ExcelImport.ReadRows(stream, JournalService.TopicHeaders.Length);
        }
        catch
        {
            return BadRequest(new { message = "Faylni o'qib bo'lmadi — buzilmagan .xlsx ekanini tekshiring" });
        }

        return await JournalService.ImportTopicsAsync(db, classId, subjectId, quarter, rows);
    }

    /* ---------- Chorak (yakuniy) bahosi ---------- */

    /// <summary>Fan+chorak bo'yicha o'quvchilarning chorak bahosi + tavsiya (kunlik o'rtacha).</summary>
    [HttpGet("quarter-grades")]
    public async Task<ActionResult<IEnumerable<QuarterGradeRowDto>>> GetQuarterGrades(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
        => await JournalService.GetQuarterGradesAsync(db, classId, subjectId, quarter);

    [HttpPut("quarter-grades")]
    public async Task<IActionResult> SetQuarterGrade(SetQuarterGradeRequest req)
    {
        await JournalService.SetQuarterGradeAsync(db, req);
        return NoContent();
    }
}
