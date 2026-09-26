using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// O'quv guruhlari — <c>docs/modules/students-parity.md</c> §2.1, G-6.
///
/// <para>
/// <b>Ruxsat: <c>classes</c>.</b> Guruh — sinfning yonidagi ikkinchi "o'quvchi
/// to'plami" va u aynan sinflar bo'limidan boshqariladi (EduSchool ham
/// <c>getClasses</c>/<c>editClasses</c> kalitlarini ishlatadi). Shu sababli
/// <see cref="ClassesController"/> bilan bir xil darvoza qo'yildi: xodim
/// (staff) uchun GET har doim ochiq, yozish esa <c>classes</c> kaliti bilan.
/// </para>
/// <para>
/// <b>Bu controller dars jadvaliga TEGMAYDI.</b> Guruh yaratish hech qanday
/// shablon, hafta biriktirish, jurnal yoki davomat qatorini yozmaydi;
/// <c>school_meta.group_lessons_enabled</c> o'chiqligicha qoladi (§4.2).
/// Guruh darslari — keyingi to'lqin (G-11..G-18).
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("classes")]
[Route("api/admin/study-groups")]
public class StudyGroupsController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>
    /// Audit yorlig'i. <c>AuditService</c> da hali konstanta yo'q (u fayl
    /// wiring commit'iniki) — qiymat shu yerda saqlanadi va konstanta
    /// qo'shilganda AYNAN shu satr ishlatiladi, ya'ni bugun yozilgan qatorlar
    /// ertaga ham filtrga tushadi.
    /// </summary>
    internal const string AuditEntity = AuditService.EntityStudyGroup;

    private StudyGroupService Service => new(db);

    private string UserId =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? "";

    /* ===================================================================
     *  1. Ro'yxat va kartochka
     * ================================================================ */

    /// <summary>
    /// Guruhlar ro'yxati (§2.1.1). Filtrlar: qidiruv (nom/fan), sinf darajasi,
    /// fan, o'qituvchi va arxiv ko'rinishi.
    /// </summary>
    /// <param name="grades">Vergul bilan ajratilgan darajalar, masalan <c>5,6</c>.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StudyGroupListItemDto>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] string? grades = null,
        [FromQuery] string? subjectId = null,
        [FromQuery] string? teacherId = null,
        [FromQuery] bool archived = false,
        CancellationToken ct = default)
    {
        var q = db.StudyGroups.AsNoTracking().Where(g => g.IsArchived == archived);

        if (!string.IsNullOrWhiteSpace(subjectId)) q = q.Where(g => g.SubjectId == subjectId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(g => EF.Functions.ILike(g.Name, $"%{term}%"));
        }

        if (!string.IsNullOrWhiteSpace(teacherId))
        {
            var byTeacher = db.StudyGroupTeachers.Where(t => t.TeacherId == teacherId).Select(t => t.GroupId);
            q = q.Where(g => byTeacher.Contains(g.Id));
        }

        var gradeList = ParseGrades(grades);
        if (gradeList.Count > 0)
        {
            var byGrade = db.StudyGroupClasses
                .Join(db.Classes, gc => gc.ClassId, c => c.Id, (gc, c) => new { gc.GroupId, c.Grade })
                .Where(x => gradeList.Contains(x.Grade))
                .Select(x => x.GroupId);
            q = q.Where(g => byGrade.Contains(g.Id));
        }

        var groups = await q.OrderBy(g => g.Name).ToListAsync(ct);
        return await ToListItemsAsync(groups, ct);
    }

    /// <summary>Bitta guruh — forma uchun, FAOL a'zolari bilan.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StudyGroupDetailDto>> Get(Guid id, CancellationToken ct = default)
    {
        var group = await db.StudyGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return NotFound();

        var subject = group.SubjectId is null
            ? null
            : await db.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == group.SubjectId, ct);
        var classes = await ClassesOfAsync([group.Id], ct);
        var teachers = await TeachersOfAsync([group.Id], ct);

        return new StudyGroupDetailDto(
            group.Id, group.Name, group.SubjectId, subject?.Name ?? "", group.Gender,
            group.IsArchived, group.ArchivedAt,
            classes.GetValueOrDefault(group.Id, []),
            teachers.GetValueOrDefault(group.Id, []),
            await MembersAsync(group.Id, includeHistory: false, ct),
            group.IsTrack);
    }

    /// <summary>
    /// Guruh ro'yxati sahifasi (§2.1.1 <c>/group/:id/students</c>).
    /// <paramref name="includeHistory"/>=true bo'lsa chiqib ketganlar ham qaytadi.
    /// </summary>
    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<IEnumerable<StudyGroupMemberDto>>> GetMembers(
        Guid id, [FromQuery] bool includeHistory = false, CancellationToken ct = default)
    {
        if (!await db.StudyGroups.AnyAsync(g => g.Id == id, ct)) return NotFound();
        return await MembersAsync(id, includeHistory, ct);
    }

    /// <summary>
    /// Ro'yxatga qo'shish uchun nomzodlar (chap panel). Sinflar VA fan
    /// berilmaguncha bo'sh qaytadi — EduSchool'dagi kabi.
    /// </summary>
    [HttpGet("candidates")]
    public async Task<ActionResult<IEnumerable<GroupCandidateDto>>> Candidates(
        [FromQuery] string? classIds = null,
        [FromQuery] string? subjectId = null,
        [FromQuery] string? gender = null,
        [FromQuery] Guid? excludeGroupId = null,
        [FromQuery] bool track = false,
        CancellationToken ct = default)
    {
        // Yo'nalish guruhi fansiz — nomzodlar fan bo'lmasa ham qaytadi.
        if (!track && string.IsNullOrWhiteSpace(subjectId)) return new List<GroupCandidateDto>();
        var ids = SplitIds(classIds);
        return await Service.CandidatesAsync(ids, gender, subjectId, excludeGroupId, ct, track);
    }

    /* ===================================================================
     *  2. Yaratish, tahrirlash, arxiv, nusxa
     * ================================================================ */

    [HttpPost]
    public async Task<ActionResult<StudyGroupDetailDto>> Create(
        SaveStudyGroupRequest req, CancellationToken ct = default)
    {
        var error = await Service.ValidateAsync(req, excludeGroupId: null, ct);
        if (error is not null) return BadRequest(new { message = error });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var group = Service.Create(req, UserId);

        if (req.StudentIds is { Count: > 0 })
        {
            // A'zolar guruhning O'ZI yozilgandan keyin qo'shiladi: kompozit FK
            // `(group_id, subject_id)` guruh qatorini talab qiladi.
            await db.SaveChangesAsync(ct);
            var memberError = await Service.AddMembersAsync(group, req.StudentIds, UserId, ct);
            if (memberError is not null) return BadRequest(new { message = memberError });
        }

        audit.Record(AuditEntity, group.Id.ToString(), "create",
            $"Yangi o'quv guruhi: {group.Name}",
            after: new { group.Name, group.SubjectId, group.Gender });

        var saveError = await SaveAsync(ct);
        if (saveError is not null) return Conflict(new { message = saveError });
        await tx.CommitAsync(ct);

        return await Get(group.Id, ct);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StudyGroupDetailDto>> Update(
        Guid id, SaveStudyGroupRequest req, CancellationToken ct = default)
    {
        var group = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return NotFound();

        var error = await Service.ValidateAsync(req, excludeGroupId: id, ct);
        if (error is not null) return BadRequest(new { message = error });

        var oldName = group.Name;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var updateError = await Service.UpdateAsync(group, req, ct);
        if (updateError is not null) return BadRequest(new { message = updateError });
        await db.SaveChangesAsync(ct);

        // `StudentIds` null bo'lsa ro'yxatga TEGILMAYDI (DTO izohi): nomni
        // o'zgartirish bolalarni guruhdan chiqarib yubormasligi kerak.
        if (req.StudentIds is not null)
        {
            var syncError = await SyncMembersAsync(group, req.StudentIds, ct);
            if (syncError is not null) return BadRequest(new { message = syncError });
        }

        audit.Record(AuditEntity, group.Id.ToString(), "update",
            $"O'quv guruhi yangilandi: {group.Name}",
            before: new { Name = oldName }, after: new { group.Name, group.Gender });

        var saveError = await SaveAsync(ct);
        if (saveError is not null) return Conflict(new { message = saveError });
        await tx.CommitAsync(ct);

        return await Get(group.Id, ct);
    }

    /// <summary>
    /// Guruhni arxivlash (§2.1.1 dagi "Archive" qatori — EduSchool'da u
    /// <c>DELETE</c> deb atalgan, lekin ma'nosi ARXIV). Faol a'zoliklar
    /// yopiladi, tarix qoladi.
    /// </summary>
    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct = default)
    {
        var group = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return NotFound();
        if (group.IsArchived) return BadRequest(new { message = "Guruh allaqachon arxivda" });

        await Service.ArchiveAsync(group, ct);
        audit.Record(AuditEntity, group.Id.ToString(), "update", $"O'quv guruhi arxivlandi: {group.Name}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/unarchive")]
    public async Task<IActionResult> Unarchive(Guid id, CancellationToken ct = default)
    {
        var group = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return NotFound();
        if (!group.IsArchived) return BadRequest(new { message = "Guruh arxivda emas" });

        var error = await Service.UnarchiveAsync(group, ct);
        if (error is not null) return Conflict(new { message = error });

        audit.Record(AuditEntity, group.Id.ToString(), "update",
            $"O'quv guruhi arxivdan chiqarildi: {group.Name}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Guruhni nusxalash (§2.1.1 "Duplicate"). Fan, sinflar va jins
    /// KO'CHIRILADI va o'zgartirilmaydi; ro'yxat ixtiyoriy.
    ///
    /// <para>
    /// Ro'yxat ko'chirilsa, manba guruh a'zolari nusxaga <b>o'tkaziladi</b>
    /// emas, <b>qo'shiladi</b> — ya'ni ular ikkita faol guruhda bo'lib qolardi
    /// va "bitta fandan bitta guruh" qoidasi buzilardi. Shuning uchun
    /// <c>copyMembers</c> faqat manba guruh ARXIVLANGAN bo'lsa ishlaydi
    /// (yangi o'quv yiliga nusxalash — §2.1.4 dagi rollover ssenariysi).
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/duplicate")]
    public async Task<ActionResult<StudyGroupDetailDto>> Duplicate(
        Guid id, DuplicateStudyGroupRequest req, CancellationToken ct = default)
    {
        var source = await db.StudyGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (source is null) return NotFound();

        var classIds = await db.StudyGroupClasses.Where(c => c.GroupId == id)
            .Select(c => c.ClassId).ToListAsync(ct);
        var teacherIds = req.TeacherIds is { Count: > 0 }
            ? [.. req.TeacherIds]
            : await db.StudyGroupTeachers.Where(t => t.GroupId == id)
                .Select(t => t.TeacherId).ToListAsync(ct);

        List<string>? studentIds = null;
        if (req.CopyMembers)
        {
            if (!source.IsArchived)
                return BadRequest(new
                {
                    message = "Faol guruhning ro'yxatini nusxalab bo'lmaydi — o'quvchi bitta "
                              + "fandan faqat bitta guruhda bo'ladi. Avval manba guruhni arxivlang.",
                });
            studentIds = await db.StudyGroupMembers
                .Where(m => m.GroupId == id).Select(m => m.StudentId).Distinct().ToListAsync(ct);
        }

        var create = new SaveStudyGroupRequest(
            req.Name, source.SubjectId, classIds, teacherIds, source.Gender, studentIds, source.IsTrack);
        return await Create(create, ct);
    }

    /* ===================================================================
     *  3. Ro'yxat amallari
     * ================================================================ */

    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> AddMembers(
        Guid id, AddGroupMembersRequest req, CancellationToken ct = default)
    {
        var group = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return NotFound();

        var error = await Service.AddMembersAsync(group, req.StudentIds ?? [], UserId, ct);
        if (error is not null) return BadRequest(new { message = error });

        audit.Record(AuditEntity, group.Id.ToString(), "update",
            $"Guruhga {req.StudentIds?.Count ?? 0} ta o'quvchi qo'shildi ({group.Name})");

        var saveError = await SaveAsync(ct);
        return saveError is not null ? Conflict(new { message = saveError }) : NoContent();
    }

    /// <summary>
    /// O'quvchini guruhdan chiqarish — a'zolik SABAB bilan yopiladi, tarix qoladi.
    /// <c>DELETE</c> emas <c>POST</c>: sabab so'rov tanasida keladi.
    /// </summary>
    [HttpPost("{id:guid}/members/{memberId:guid}/remove")]
    public async Task<IActionResult> RemoveMember(
        Guid id, Guid memberId, RemoveGroupMemberRequest req, CancellationToken ct = default)
    {
        var member = await db.StudyGroupMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.GroupId == id, ct);
        if (member is null) return NotFound();
        if (member.LeftOn is not null)
            return BadRequest(new { message = "Bu a'zolik allaqachon yopilgan" });

        var group = await db.StudyGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        StudyGroupService.CloseMember(member, req?.Reason);

        audit.Record(AuditEntity, id.ToString(), "update",
            $"O'quvchi guruhdan chiqarildi ({group?.Name})"
            + (string.IsNullOrWhiteSpace(req?.Reason) ? "" : $" — sabab: {req!.Reason!.Trim()}"),
            studentId: member.StudentId);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// O'quvchini boshqa guruhga o'tkazish — FAQAT ayni fan ichida (§2.1.1
    /// "Transfer"). Natijada o'quvchida shu fan bo'yicha AYNAN BITTA faol
    /// a'zolik qoladi.
    /// </summary>
    [HttpPost("members/{memberId:guid}/transfer")]
    public async Task<IActionResult> TransferMember(
        Guid memberId, TransferGroupMemberRequest req, CancellationToken ct = default)
    {
        var member = await db.StudyGroupMembers.FirstOrDefaultAsync(m => m.Id == memberId, ct);
        if (member is null) return NotFound();
        if (member.LeftOn is not null)
            return BadRequest(new { message = "Yopilgan a'zolikni o'tkazib bo'lmaydi" });

        var from = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == member.GroupId, ct);
        var to = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == req.ToGroupId, ct);
        if (from is null || to is null) return NotFound();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        string? error;
        try
        {
            error = await Service.TransferAsync(member, from, to, req.Reason, UserId, ct);
        }
        catch (DbUpdateException ex) when (StudyGroupService.IsOneGroupPerSubjectViolation(ex))
        {
            return Conflict(new { message = StudyGroupService.OneGroupPerSubjectMessage });
        }
        if (error is not null) return BadRequest(new { message = error });

        audit.Record(AuditEntity, to.Id.ToString(), "update",
            $"O'quvchi guruhdan guruhga o'tkazildi: {from.Name} → {to.Name}",
            studentId: member.StudentId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    /* ===================================================================
     *  4. Ichki yordamchilar
     * ================================================================ */

    /// <summary>
    /// Formadagi ro'yxatni bazadagisiga MOSLAYDI: ro'yxatda yo'q a'zolar
    /// yopiladi, yangilari qo'shiladi. Yopish AVVAL saqlanadi — qisman unikal
    /// indeks bir lahzada ikki faol qatorni ko'rmasligi uchun.
    /// </summary>
    private async Task<string?> SyncMembersAsync(
        StudyGroup group, IReadOnlyList<string> studentIds, CancellationToken ct)
    {
        var wanted = studentIds.Where(i => !string.IsNullOrWhiteSpace(i))
            .Distinct(StringComparer.Ordinal).ToList();

        var active = await db.StudyGroupMembers
            .Where(m => m.GroupId == group.Id && m.LeftOn == null).ToListAsync(ct);

        foreach (var member in active.Where(m => !wanted.Contains(m.StudentId, StringComparer.Ordinal)))
            StudyGroupService.CloseMember(member, "Guruh ro'yxatidan olib tashlandi");
        await db.SaveChangesAsync(ct);

        return await Service.AddMembersAsync(group, wanted, UserId, ct);
    }

    /// <summary>
    /// Saqlaydi va baza kafolatlarini o'zbekcha xabarga o'giradi — foydalanuvchi
    /// hech qachon 23505 ni ko'rmasligi kerak.
    /// </summary>
    private async Task<string?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (StudyGroupService.IsOneGroupPerSubjectViolation(ex))
        {
            return StudyGroupService.OneGroupPerSubjectMessage;
        }
        catch (DbUpdateException ex) when (StudyGroupService.IsDuplicateNameViolation(ex))
        {
            return StudyGroupService.DuplicateNameMessage;
        }
    }

    private async Task<List<StudyGroupListItemDto>> ToListItemsAsync(
        List<StudyGroup> groups, CancellationToken ct)
    {
        if (groups.Count == 0) return [];

        var ids = groups.Select(g => g.Id).ToList();
        var classes = await ClassesOfAsync(ids, ct);
        var teachers = await TeachersOfAsync(ids, ct);

        var subjectIds = groups.Where(g => g.SubjectId != null).Select(g => g.SubjectId!).Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking()
            .Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var counts = await db.StudyGroupMembers.AsNoTracking()
            .Where(m => ids.Contains(m.GroupId) && m.LeftOn == null)
            .GroupBy(m => m.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count, ct);

        return [.. groups.Select(g => new StudyGroupListItemDto(
            g.Id, g.Name, g.SubjectId,
            g.SubjectId is null ? "" : subjects.GetValueOrDefault(g.SubjectId, ""), g.Gender,
            g.IsArchived, g.ArchivedAt,
            classes.GetValueOrDefault(g.Id, []),
            teachers.GetValueOrDefault(g.Id, []),
            counts.GetValueOrDefault(g.Id),
            g.IsTrack))];
    }

    private async Task<Dictionary<Guid, List<StudyGroupClassRefDto>>> ClassesOfAsync(
        IReadOnlyList<Guid> groupIds, CancellationToken ct)
    {
        var rows = await db.StudyGroupClasses.AsNoTracking()
            .Where(gc => groupIds.Contains(gc.GroupId))
            .Join(db.Classes.AsNoTracking(), gc => gc.ClassId, c => c.Id,
                (gc, c) => new { gc.GroupId, c.Id, c.Name, c.Grade })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.GroupId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.Grade).ThenBy(r => r.Name)
                    .Select(r => new StudyGroupClassRefDto(r.Id, r.Name, r.Grade)).ToList());
    }

    private async Task<Dictionary<Guid, List<StudyGroupTeacherRefDto>>> TeachersOfAsync(
        IReadOnlyList<Guid> groupIds, CancellationToken ct)
    {
        var rows = await db.StudyGroupTeachers.AsNoTracking()
            .Where(gt => groupIds.Contains(gt.GroupId))
            .Join(db.Teachers.AsNoTracking(), gt => gt.TeacherId, t => t.Id,
                (gt, t) => new { gt.GroupId, t.Id, t.FullName })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.GroupId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.FullName)
                    .Select(r => new StudyGroupTeacherRefDto(r.Id, r.FullName)).ToList());
    }

    /// <summary>
    /// Guruh a'zolari. Sinf nomi o'quvchining BUGUNGI sinfi
    /// (<c>students.class_name</c>) — DTO izohiga qarang.
    /// </summary>
    private async Task<List<StudyGroupMemberDto>> MembersAsync(
        Guid groupId, bool includeHistory, CancellationToken ct)
    {
        var q = db.StudyGroupMembers.AsNoTracking().Where(m => m.GroupId == groupId);
        if (!includeHistory) q = q.Where(m => m.LeftOn == null);

        var rows = await q
            .Join(db.Students.AsNoTracking(), m => m.StudentId, s => s.Id, (m, s) => new
            {
                m.Id, m.StudentId, s.FullName, s.ClassName, s.Gender,
                m.JoinedOn, m.LeftOn, m.LeaveReason,
            })
            .ToListAsync(ct);

        return [.. rows
            .OrderBy(r => r.LeftOn != null)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(r => new StudyGroupMemberDto(
                r.Id, r.StudentId, r.FullName, r.ClassName, r.Gender,
                r.JoinedOn, r.LeftOn, r.LeaveReason))];
    }

    private static List<int> ParseGrades(string? grades) =>
        string.IsNullOrWhiteSpace(grades)
            ? []
            : [.. grades.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(g => int.TryParse(g, out var n) ? n : -1)
                .Where(n => n >= 0)
                .Distinct()];

    private static List<string> SplitIds(string? ids) =>
        string.IsNullOrWhiteSpace(ids)
            ? []
            : [.. ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)];
}
