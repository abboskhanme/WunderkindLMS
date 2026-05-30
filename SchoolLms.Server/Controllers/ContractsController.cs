using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Application.Services;
using System.Globalization;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Shartnomalar bo'limi (admin): Word andoza yuklash (ota-ona/xodim), oluvchilarni tanlash,
/// andozadagi <c>@</c>-o'rinbosarlarni baza ma'lumoti bilan almashtirib, Telegram bot orqali
/// to'ldirilgan .docx yuborish. Bot tokeni Sozlamalardagi yagona token (mavjud TelegramService).
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/contracts")]
public class ContractsController(AppDbContext db, ContractService contracts, TelegramService telegram)
    : ControllerBase
{
    // ---------- Andozalar ----------

    [HttpGet("templates")]
    public async Task<ActionResult<IEnumerable<ContractTemplateDto>>> Templates([FromQuery] string? target)
    {
        var q = db.ContractTemplates.AsQueryable();
        if (!string.IsNullOrEmpty(target)) q = q.Where(t => t.Target == target);
        return (await q.OrderByDescending(t => t.UploadedAt).ToListAsync()).Select(ToDto).ToList();
    }

    [HttpPost("templates")]
    public async Task<ActionResult<ContractTemplateDto>> CreateTemplate(CreateContractTemplateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FileUrl))
            return BadRequest(new { message = "Word fayl yuklang" });
        var tpl = new ContractTemplate
        {
            Target = req.Target == "staff" ? "staff" : "parent",
            Name = (req.Name ?? "").Trim(),
            FileUrl = req.FileUrl,
            FileName = req.FileName ?? "",
        };
        db.ContractTemplates.Add(tpl);
        await db.SaveChangesAsync();
        return ToDto(tpl);
    }

    [HttpDelete("templates/{id}")]
    public async Task<IActionResult> DeleteTemplate(string id)
    {
        var tpl = await db.ContractTemplates.FindAsync(id);
        if (tpl is null) return NotFound();
        db.ContractTemplates.Remove(tpl);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Oluvchilar ----------

    [HttpGet("recipients/parents")]
    public async Task<ActionResult<IEnumerable<ParentRecipientDto>>> ParentRecipients()
    {
        var groups = await ParentGroupsAsync();
        var regStudentIds = (await db.TelegramRegistrations
            .Where(r => r.StudentId != "").Select(r => r.StudentId).ToListAsync())
            .ToHashSet();
        var lastByKey = await LastNumbersAsync("parent");
        return groups.Select(g => new ParentRecipientDto(
            g.Key, g.ParentName, g.Phone, g.Children,
            g.StudentIds.Any(regStudentIds.Contains),
            lastByKey.TryGetValue(g.Key, out var n) ? n : null)).ToList();
    }

    [HttpGet("recipients/staff")]
    public async Task<ActionResult<IEnumerable<StaffRecipientDto>>> StaffRecipients()
    {
        var teachers = await db.Teachers.Where(t => !t.IsArchived).OrderBy(t => t.FullName).ToListAsync();
        var regTeacherIds = (await db.TelegramRegistrations
            .Where(r => r.TeacherId != null).Select(r => r.TeacherId!).ToListAsync())
            .ToHashSet();
        var lastByKey = await LastNumbersAsync("staff");
        return teachers.Select(t => new StaffRecipientDto(
            t.Id, t.FullName, t.Phone, regTeacherIds.Contains(t.Id),
            lastByKey.TryGetValue(t.Id, out var n) ? n : null)).ToList();
    }

    // ---------- Yuborish ----------

    [HttpPost("send")]
    public async Task<ActionResult<IEnumerable<SendResultDto>>> Send(SendContractsRequest req)
    {
        var tpl = await db.ContractTemplates.FindAsync(req.TemplateId);
        if (tpl is null) return BadRequest(new { message = "Andoza topilmadi" });
        var bytes = contracts.ReadTemplate(tpl.FileUrl);
        if (bytes is null) return BadRequest(new { message = "Andoza fayli topilmadi" });

        var number = await db.Contracts.AnyAsync() ? await db.Contracts.MaxAsync(c => c.Number) : 0;
        var today = DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var results = new List<SendResultDto>();
        var keys = req.RecipientKeys.Distinct().ToList();

        if (req.Target == "staff")
        {
            var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name);
            var teachers = await db.Teachers.ToListAsync();
            foreach (var key in keys)
            {
                var t = teachers.FirstOrDefault(x => x.Id == key);
                if (t is null) { results.Add(new(key, false, null, "Topilmadi")); continue; }
                var chatIds = await db.TelegramRegistrations
                    .Where(r => r.TeacherId == t.Id).Select(r => r.ChatId).Distinct().ToListAsync();
                if (chatIds.Count == 0) { results.Add(new(key, false, null, "Telegramda ro'yxatdan o'tmagan")); continue; }

                number++;
                var filled = contracts.FillTemplate(bytes, StaffTokens(t, subjects, number, today));
                var ok = await DeliverAsync(chatIds, filled, number);
                db.Contracts.Add(new Contract
                {
                    Target = "staff",
                    RecipientKey = t.Id,
                    RecipientName = t.FullName,
                    Number = number,
                    TemplateId = tpl.Id,
                    SentAt = DateTime.UtcNow,
                    Delivered = ok,
                });
                results.Add(new(key, ok, number, ok ? "Yuborildi" : "Yuborilmadi"));
            }
        }
        else
        {
            var groups = await ParentGroupsAsync();
            foreach (var key in keys)
            {
                var g = groups.FirstOrDefault(x => x.Key == key);
                if (g is null) { results.Add(new(key, false, null, "Topilmadi")); continue; }
                var chatIds = await db.TelegramRegistrations
                    .Where(r => g.StudentIds.Contains(r.StudentId)).Select(r => r.ChatId).Distinct().ToListAsync();
                if (chatIds.Count == 0) { results.Add(new(key, false, null, "Telegramda ro'yxatdan o'tmagan")); continue; }

                number++;
                var filled = contracts.FillTemplate(bytes, ParentTokens(g, number, today));
                var ok = await DeliverAsync(chatIds, filled, number);
                db.Contracts.Add(new Contract
                {
                    Target = "parent",
                    RecipientKey = g.Key,
                    RecipientName = g.ParentName,
                    Number = number,
                    TemplateId = tpl.Id,
                    SentAt = DateTime.UtcNow,
                    Delivered = ok,
                });
                results.Add(new(key, ok, number, ok ? "Yuborildi" : "Yuborilmadi"));
            }
        }

        await db.SaveChangesAsync();
        return results;
    }

    // ---------- Yordamchilar ----------

    private async Task<bool> DeliverAsync(List<long> chatIds, byte[] filled, int number)
    {
        var ok = false;
        var fileName = $"shartnoma-{number}.docx";
        foreach (var cid in chatIds)
            ok |= await telegram.SendDocumentAsync(cid, filled, fileName, $"Shartnoma № {number}");
        return ok;
    }

    private async Task<Dictionary<string, int>> LastNumbersAsync(string target) =>
        (await db.Contracts.Where(c => c.Target == target)
            .GroupBy(c => c.RecipientKey)
            .Select(g => new { Key = g.Key, Max = g.Max(x => x.Number) }).ToListAsync())
        .ToDictionary(x => x.Key, x => x.Max);

    private sealed class ParentGroup
    {
        public string Key = "";
        public string ParentName = "";
        public string Phone = "";
        public List<string> StudentIds = new();
        public List<string> Children = new();
    }

    /// <summary>Faol o'quvchilarni ota-ona telefoni (yoki ismi) bo'yicha guruhlaydi.</summary>
    private async Task<List<ParentGroup>> ParentGroupsAsync()
    {
        var students = await db.Students.Where(s => !s.IsArchived).ToListAsync();
        var groups = new Dictionary<string, ParentGroup>();
        foreach (var s in students)
        {
            var phoneKey = PhoneUtil.Key(s.ParentPhone);
            var key = phoneKey.Length >= 7
                ? "p:" + phoneKey
                : (!string.IsNullOrWhiteSpace(s.ParentFullName)
                    ? "n:" + s.ParentFullName.Trim().ToLowerInvariant()
                    : "s:" + s.Id);
            if (!groups.TryGetValue(key, out var g))
                groups[key] = g = new ParentGroup
                {
                    Key = key,
                    ParentName = s.ParentFullName ?? "",
                    Phone = s.ParentPhone ?? "",
                };
            g.StudentIds.Add(s.Id);
            g.Children.Add($"{s.FullName} ({s.ClassName})");
            if (string.IsNullOrWhiteSpace(g.ParentName)) g.ParentName = s.ParentFullName ?? "";
            if (string.IsNullOrWhiteSpace(g.Phone)) g.Phone = s.ParentPhone ?? "";
        }
        return groups.Values.OrderBy(g => g.ParentName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Dictionary<string, string> ParentTokens(ParentGroup g, int number, string today) => new()
    {
        ["@ota_ona"] = g.ParentName,
        ["@telefon"] = g.Phone,
        ["@farzandlar"] = string.Join(", ", g.Children),
        ["@sana"] = today,
        ["@raqam"] = number.ToString(),
    };

    private static Dictionary<string, string> StaffTokens(
        Teacher t, Dictionary<string, string> subjects, int number, string today) => new()
        {
            ["@fish"] = t.FullName,
            ["@telefon"] = t.Phone,
            ["@lavozim"] = "O'qituvchi",
            ["@fanlar"] = string.Join(", ",
            t.SubjectIds.Select(id => subjects.GetValueOrDefault(id, "")).Where(n => n.Length > 0)),
            ["@tugilgan_kun"] = FormatDate(t.BirthDate),
            ["@manzil"] = t.Address,
            ["@oylik"] = t.Salary > 0 ? AuditService.Money(t.Salary) : "",
            ["@sana"] = today,
            ["@raqam"] = number.ToString(),
        };

    private static string FormatDate(string iso) =>
        DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : iso;

    private static ContractTemplateDto ToDto(ContractTemplate t) =>
        new(t.Id, t.Target, t.Name, t.FileUrl, t.FileName, t.UploadedAt.ToString("o"));
}
