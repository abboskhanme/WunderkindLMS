using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// LMS (Ta'lim) — admin tomoni.
/// Ierarxiya: Sinflar → Fanlar (har sinf uchun alohida) → Mavzular (video/matn/material).
/// Ochilish tartibi: all / sequential / batch — har fanda alohida sozlanadi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("app")]
[Route("api/admin/lms")]
public class LmsController(AppDbContext db, IWebHostEnvironment env) : ControllerBase
{
    /* ─── Fanlar (Subjects) ─────────────────────────────────── */

    /// <summary>Barcha fanlar yoki sinfga tegishli fanlar.</summary>
    [HttpGet("subjects")]
    public async Task<ActionResult<IEnumerable<LmsSubjectDto>>> Subjects([FromQuery] string? classId)
    {
        var q = db.LmsSubjects.Include(s => s.Modules).ThenInclude(m => m.Topics).AsQueryable();
        if (!string.IsNullOrEmpty(classId)) q = q.Where(s => s.ClassId == classId);
        var list = await q.OrderBy(s => s.CreatedAt).ToListAsync();

        // Sinf nomlarini topamiz (bir so'rovda)
        var classIds = list.Select(s => s.ClassId).Distinct().ToList();
        var classNames = await db.Classes
            .Where(c => classIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        return list.Select(s => ToSubjectDto(s, classNames.GetValueOrDefault(s.ClassId, ""))).ToList();
    }

    [HttpPost("subjects")]
    public async Task<ActionResult<LmsSubjectDto>> CreateSubject(SaveLmsSubjectRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { message = "Fan nomi kerak" });
        if (string.IsNullOrWhiteSpace(req.ClassId))
            return BadRequest(new { message = "Sinf kerak" });

        var cls = await db.Classes.FindAsync(req.ClassId);
        if (cls is null) return NotFound(new { message = "Sinf topilmadi" });

        var s = new LmsSubject
        {
            ClassId = req.ClassId,
            Title = req.Title.Trim(),
            Description = req.Description?.Trim() ?? "",
            UnlockMode = ValidMode(req.UnlockMode),
            BatchSize = Math.Max(1, req.BatchSize),
        };
        db.LmsSubjects.Add(s);
        await db.SaveChangesAsync();
        return ToSubjectDto(s, cls.Name);
    }

    [HttpPut("subjects/{id}")]
    public async Task<IActionResult> UpdateSubject(string id, SaveLmsSubjectRequest req)
    {
        var s = await db.LmsSubjects.FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { message = "Fan nomi kerak" });

        s.Title = req.Title.Trim();
        s.Description = req.Description?.Trim() ?? "";
        s.UnlockMode = ValidMode(req.UnlockMode);
        s.BatchSize = Math.Max(1, req.BatchSize);
        // ClassId o'zgarmaydi (sinf tanlash faqat yaratishda)
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("subjects/{id}")]
    public async Task<IActionResult> DeleteSubject(string id)
    {
        var s = await db.LmsSubjects.FindAsync(id);
        if (s is null) return NotFound();

        // DB endi subject→module→topic kaskadini bajarmaydi (FK olib tashlangan).
        // Qo'lda o'chiramiz: avval mavzular (DB ular orqali material/progressni kaskadlaydi),
        // keyin modullar, so'ng fanning o'zi.
        var moduleIds = await db.LmsModules.Where(m => m.SubjectId == id).Select(m => m.Id).ToListAsync();
        var topics = await db.LmsTopics.Where(t => moduleIds.Contains(t.ModuleId)).ToListAsync();
        var modules = await db.LmsModules.Where(m => m.SubjectId == id).ToListAsync();
        db.LmsTopics.RemoveRange(topics);
        db.LmsModules.RemoveRange(modules);
        db.LmsSubjects.Remove(s);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /* ─── Modullar (Modules) ────────────────────────────────── */

    /// <summary>Fanga tegishli modullar (tartib bo'yicha).</summary>
    [HttpGet("subjects/{subjectId}/modules")]
    public async Task<ActionResult<IEnumerable<LmsModuleDto>>> Modules(string subjectId)
    {
        var modules = await db.LmsModules
            .Include(m => m.Topics)
            .Where(m => m.SubjectId == subjectId)
            .OrderBy(m => m.Order)
            .ToListAsync();

        return modules
            .Select(m => new LmsModuleDto(m.Id, m.SubjectId, m.Title, m.Description, m.Order, m.Topics.Count))
            .ToList();
    }

    [HttpPost("subjects/{subjectId}/modules")]
    public async Task<ActionResult<LmsModuleDto>> CreateModule(string subjectId, SaveLmsModuleRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { message = "Modul nomi kerak" });
        if (!await db.LmsSubjects.AnyAsync(s => s.Id == subjectId))
            return NotFound(new { message = "Fan topilmadi" });

        var maxOrder = await db.LmsModules
            .Where(m => m.SubjectId == subjectId)
            .Select(m => (int?)m.Order)
            .MaxAsync() ?? 0;

        var module = new LmsModule
        {
            SubjectId = subjectId,
            Title = req.Title.Trim(),
            Description = req.Description?.Trim() ?? "",
            Order = maxOrder + 1,
        };
        db.LmsModules.Add(module);
        await db.SaveChangesAsync();
        return new LmsModuleDto(module.Id, module.SubjectId, module.Title, module.Description, module.Order, 0);
    }

    [HttpPut("modules/{id}")]
    public async Task<IActionResult> UpdateModule(string id, SaveLmsModuleRequest req)
    {
        var module = await db.LmsModules.FirstOrDefaultAsync(m => m.Id == id);
        if (module is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { message = "Modul nomi kerak" });

        module.Title = req.Title.Trim();
        module.Description = req.Description?.Trim() ?? "";
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("modules/{id}")]
    public async Task<IActionResult> DeleteModule(string id)
    {
        var module = await db.LmsModules.FindAsync(id);
        if (module is null) return NotFound();

        // DB topic→material/progress kaskadini bajaradi; modul mavzularini qo'lda o'chiramiz.
        var topics = await db.LmsTopics.Where(t => t.ModuleId == id).ToListAsync();
        db.LmsTopics.RemoveRange(topics);
        db.LmsModules.Remove(module);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("subjects/{subjectId}/modules/reorder")]
    public async Task<IActionResult> ReorderModules(string subjectId, ReorderLmsModulesRequest req)
    {
        var modules = await db.LmsModules.Where(m => m.SubjectId == subjectId).ToListAsync();
        for (var i = 0; i < req.ModuleIds.Count; i++)
        {
            var m = modules.FirstOrDefault(x => x.Id == req.ModuleIds[i]);
            if (m is not null) m.Order = i + 1;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    /* ─── Mavzular (Topics) ─────────────────────────────────── */

    [HttpGet("modules/{moduleId}/topics")]
    public async Task<ActionResult<IEnumerable<LmsTopicDto>>> Topics(string moduleId)
    {
        var topics = await db.LmsTopics
            .Include(t => t.Materials)
            .Where(t => t.ModuleId == moduleId)
            .OrderBy(t => t.Order)
            .ToListAsync();

        var topicIds = topics.Select(t => t.Id).ToList();
        var completedMap = (await db.LmsProgresses
                .Where(p => topicIds.Contains(p.TopicId))
                .GroupBy(p => p.TopicId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.Id, x => x.Count);

        return topics.Select(t => ToTopicDto(t, completedMap.GetValueOrDefault(t.Id, 0))).ToList();
    }

    /// <summary>
    /// Fan bo'yicha o'quvchilar progress matritsasi (admin): sinfdagi har o'quvchi qaysi mavzularni
    /// tugatgan. O'qituvchi tomonidagi hisobotning aynan o'zi, lekin egalik tekshiruvisiz.
    /// </summary>
    [HttpGet("subjects/{subjectId}/progress")]
    public async Task<ActionResult<LmsProgressReportDto>> Progress(string subjectId)
    {
        var subject = await db.LmsSubjects.FindAsync(subjectId);
        if (subject is null) return NotFound();

        // Mavzular endi modullar ostida — fan modullari (Order bo'yicha), keyin har modul
        // mavzulari (Order bo'yicha) ketma-ket bitta ro'yxatga yig'iladi.
        var modules = await db.LmsModules.Where(m => m.SubjectId == subjectId)
            .OrderBy(m => m.Order).ToListAsync();
        var moduleIds = modules.Select(m => m.Id).ToList();
        var moduleTopics = (await db.LmsTopics.Where(x => moduleIds.Contains(x.ModuleId)).ToListAsync())
            .GroupBy(x => x.ModuleId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Order).ToList());
        var topics = modules
            .SelectMany(m => moduleTopics.GetValueOrDefault(m.Id, new List<LmsTopic>()))
            .ToList();
        var topicIds = topics.Select(x => x.Id).ToList();

        // Asosiy ro'yxat — fan biriktirilgan sinf o'quvchilari (sinf o'chirilgan bo'lsa bo'sh).
        var cls = await db.Classes.FindAsync(subject.ClassId);
        var roster = cls is null
            ? new List<Student>()
            : await db.Students.Where(s => s.ClassName == cls.Name && !s.IsArchived).ToListAsync();

        // Mavzularni tugatgan o'quvchilarning progressi (sinfga qarab cheklamaymiz).
        var byStudent = (await db.LmsProgresses
                .Where(p => topicIds.Contains(p.TopicId))
                .ToListAsync())
            .GroupBy(p => p.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TopicId).ToList());

        // Sinf ro'yxatiga, sinfda bo'lmagan lekin tugatgan o'quvchilarni ham qo'shamiz — aks holda
        // ularning bajargani ko'rinmay qoladi (boshqa sinfga ko'chgan/arxivlangan bo'lishi mumkin).
        var rosterIds = roster.Select(s => s.Id).ToHashSet();
        var extraIds = byStudent.Keys.Where(id => !rosterIds.Contains(id)).ToList();
        var extras = extraIds.Count == 0
            ? new List<Student>()
            : await db.Students.Where(s => extraIds.Contains(s.Id)).ToListAsync();

        var students = roster.Concat(extras).OrderBy(s => s.FullName).ToList();

        return new LmsProgressReportDto(
            topics.Select(x => new LmsTopicBriefDto(x.Id, x.Title, x.Order)).ToList(),
            students.Select(s =>
            {
                var done = byStudent.GetValueOrDefault(s.Id, new List<string>());
                return new LmsStudentProgressDto(s.Id, s.FullName, done, done.Count, topics.Count);
            }).ToList());
    }

    [HttpPost("modules/{moduleId}/topics")]
    public async Task<ActionResult<LmsTopicDto>> CreateTopic(string moduleId, SaveLmsTopicRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { message = "Mavzu sarlavhasi kerak" });
        if (!await db.LmsModules.AnyAsync(m => m.Id == moduleId))
            return NotFound(new { message = "Modul topilmadi" });

        var maxOrder = await db.LmsTopics
            .Where(t => t.ModuleId == moduleId)
            .Select(t => (int?)t.Order)
            .MaxAsync() ?? 0;

        var topic = new LmsTopic
        {
            ModuleId = moduleId,
            Title = req.Title.Trim(),
            Description = req.Description?.Trim() ?? "",
            VideoUrl = NullIfEmpty(req.VideoUrl),
            TextContent = NullIfEmpty(req.TextContent),
            Order = maxOrder + 1,
        };
        db.LmsTopics.Add(topic);
        await db.SaveChangesAsync();

        SaveMaterials(topic.Id, req.Materials);
        await db.SaveChangesAsync();

        var created = await db.LmsTopics.Include(t => t.Materials).FirstAsync(t => t.Id == topic.Id);
        return ToTopicDto(created, 0);
    }

    [HttpPut("topics/{id}")]
    public async Task<IActionResult> UpdateTopic(string id, SaveLmsTopicRequest req)
    {
        var topic = await db.LmsTopics.Include(t => t.Materials).FirstOrDefaultAsync(t => t.Id == id);
        if (topic is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { message = "Mavzu sarlavhasi kerak" });

        topic.Title = req.Title.Trim();
        topic.Description = req.Description?.Trim() ?? "";
        topic.VideoUrl = NullIfEmpty(req.VideoUrl);
        topic.TextContent = NullIfEmpty(req.TextContent);

        db.LmsMaterials.RemoveRange(topic.Materials);
        SaveMaterials(topic.Id, req.Materials);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("topics/{id}")]
    public async Task<IActionResult> DeleteTopic(string id)
    {
        var topic = await db.LmsTopics.FindAsync(id);
        if (topic is null) return NotFound();
        db.LmsTopics.Remove(topic);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("modules/{moduleId}/topics/reorder")]
    public async Task<IActionResult> Reorder(string moduleId, ReorderLmsTopicsRequest req)
    {
        var topics = await db.LmsTopics.Where(t => t.ModuleId == moduleId).ToListAsync();
        for (var i = 0; i < req.TopicIds.Count; i++)
        {
            var t = topics.FirstOrDefault(x => x.Id == req.TopicIds[i]);
            if (t is not null) t.Order = i + 1;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    /* ─── Fayl yuklash ──────────────────────────────────────── */

    [HttpPost("uploads")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<UploadedFileDto>> Upload(IFormFile file)
    {
        if (Application.Services.UploadGuard.Validate(file) is { } error)
            return BadRequest(new { message = error });
        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var stored = Application.Services.UploadGuard.SafeName(file);
        await using (var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored)))
            await file.CopyToAsync(fs);
        return new UploadedFileDto(file.FileName, $"/uploads/{stored}", file.Length, file.ContentType ?? "");
    }

    /* ─── Yordamchilar ──────────────────────────────────────── */

    private static string ValidMode(string? mode) =>
        mode is "all" or "sequential" or "batch" ? mode : "all";

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void SaveMaterials(string topicId, IEnumerable<LmsMaterialRowDto>? mats)
    {
        if (mats is null) return;
        foreach (var m in mats)
            db.LmsMaterials.Add(new LmsMaterial
            {
                TopicId = topicId,
                Name = m.Name,
                Url = m.Url,
                Size = m.Size,
                ContentType = m.ContentType,
            });
    }

    private static LmsSubjectDto ToSubjectDto(LmsSubject s, string className) => new(
        s.Id, s.ClassId, className, s.Title, s.Description,
        s.UnlockMode, s.BatchSize, s.Modules.Sum(m => m.Topics.Count), s.CreatedAt.ToString("o"));

    private static LmsTopicDto ToTopicDto(LmsTopic t, int completedCount) => new(
        t.Id, t.ModuleId, t.Title, t.Description, t.VideoUrl, t.TextContent, t.Order,
        t.Materials.Select(m => new LmsMaterialRowDto(m.Id, m.Name, m.Url, m.Size, m.ContentType)).ToList(),
        completedCount);
}
