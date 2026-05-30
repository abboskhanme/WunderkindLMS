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
[Authorize(Roles = "admin,superadmin,staff")]
[Route("api/admin/lms")]
public class LmsController(AppDbContext db, IWebHostEnvironment env) : ControllerBase
{
    /* ─── Fanlar (Subjects) ─────────────────────────────────── */

    /// <summary>Barcha fanlar yoki sinfga tegishli fanlar.</summary>
    [HttpGet("subjects")]
    public async Task<ActionResult<IEnumerable<LmsSubjectDto>>> Subjects([FromQuery] string? classId)
    {
        var q = db.LmsSubjects.Include(s => s.Topics).AsQueryable();
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
        db.LmsSubjects.Remove(s);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /* ─── Mavzular (Topics) ─────────────────────────────────── */

    [HttpGet("subjects/{subjectId}/topics")]
    public async Task<ActionResult<IEnumerable<LmsTopicDto>>> Topics(string subjectId)
    {
        var topics = await db.LmsTopics
            .Include(t => t.Materials)
            .Where(t => t.SubjectId == subjectId)
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

    [HttpPost("subjects/{subjectId}/topics")]
    public async Task<ActionResult<LmsTopicDto>> CreateTopic(string subjectId, SaveLmsTopicRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { message = "Mavzu sarlavhasi kerak" });
        if (!await db.LmsSubjects.AnyAsync(s => s.Id == subjectId))
            return NotFound(new { message = "Fan topilmadi" });

        var maxOrder = await db.LmsTopics
            .Where(t => t.SubjectId == subjectId)
            .Select(t => (int?)t.Order)
            .MaxAsync() ?? 0;

        var topic = new LmsTopic
        {
            SubjectId = subjectId,
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

    [HttpPut("subjects/{subjectId}/topics/reorder")]
    public async Task<IActionResult> Reorder(string subjectId, ReorderLmsTopicsRequest req)
    {
        var topics = await db.LmsTopics.Where(t => t.SubjectId == subjectId).ToListAsync();
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
        if (file is null || file.Length == 0) return BadRequest(new { message = "Fayl bo'sh" });
        if (file.Length > 20_000_000) return BadRequest(new { message = "Fayl 20 MB dan katta" });
        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var ext = System.IO.Path.GetExtension(file.FileName);
        var stored = $"{Guid.NewGuid():N}{ext}";
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
        s.UnlockMode, s.BatchSize, s.Topics.Count, s.CreatedAt.ToString("o"));

    private static LmsTopicDto ToTopicDto(LmsTopic t, int completedCount) => new(
        t.Id, t.SubjectId, t.Title, t.Description, t.VideoUrl, t.TextContent, t.Order,
        t.Materials.Select(m => new LmsMaterialRowDto(m.Id, m.Name, m.Url, m.Size, m.ContentType)).ToList(),
        completedCount);
}
