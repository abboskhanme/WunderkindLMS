using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/settings")]
public class SettingsController(AppDbContext db, TelegramService telegram) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SchoolSettingsDto>> Get()
    {
        var quarters = await db.Quarters.OrderBy(q => q.Quarter)
            .Select(q => new QuarterPeriodDto(q.Quarter, q.StartDate, q.EndDate, q.GradesOpen)).ToListAsync();
        var lessonTimes = await db.LessonTimes.OrderBy(t => t.Period)
            .Select(t => new LessonTimeDto(t.Period, t.StartTime, t.EndTime)).ToListAsync();
        var reasons = await db.AbsenceReasons
            .Select(r => new AbsenceReasonDto(r.Id, r.Name, r.Short, r.IsLate)).ToListAsync();
        return new SchoolSettingsDto(quarters, lessonTimes, reasons);
    }

    [HttpPut("quarters")]
    public async Task<IActionResult> SaveQuarters(SaveQuartersRequest req)
    {
        db.Quarters.RemoveRange(db.Quarters);
        db.Quarters.AddRange(req.Quarters.Select(q => new QuarterPeriod
        {
            Quarter = q.Quarter,
            StartDate = q.StartDate,
            EndDate = q.EndDate,
            GradesOpen = q.GradesOpen,
        }));
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("lesson-times")]
    public async Task<IActionResult> SaveLessonTimes(SaveLessonTimesRequest req)
    {
        db.LessonTimes.RemoveRange(db.LessonTimes);
        db.LessonTimes.AddRange(req.LessonTimes.Select(t => new LessonTime
        {
            Period = t.Period,
            StartTime = t.StartTime,
            EndTime = t.EndTime,
        }));
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("school")]
    public async Task<ActionResult<SchoolInfoDto>> GetSchool()
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        return new SchoolInfoDto(
            m?.Name ?? "", m?.Director ?? "", m?.Phone ?? "", m?.Email ?? "",
            m?.Address ?? "", m?.Region ?? "", m?.District ?? "");
    }

    [HttpPut("school")]
    public async Task<IActionResult> SaveSchool(SchoolInfoDto req)
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        if (m is null)
        {
            m = new SchoolMeta();
            db.SchoolMeta.Add(m);
        }
        m.Name = req.Name;
        m.Director = req.Director;
        m.Phone = req.Phone;
        m.Email = req.Email;
        m.Address = req.Address;
        m.Region = req.Region;
        m.District = req.District;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Telegram bot ----------

    [HttpGet("telegram")]
    public async Task<ActionResult<TelegramSettingsDto>> GetTelegram()
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        return new TelegramSettingsDto(
            m?.TelegramBotToken ?? "", m?.TelegramBotUsername ?? "", m?.TelegramBotName ?? "",
            telegram.IsConfigured);
    }

    [HttpPut("telegram")]
    public async Task<ActionResult<TelegramSettingsDto>> SaveTelegram(SaveTelegramSettingsRequest req)
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        if (m is null)
        {
            m = new SchoolMeta();
            db.SchoolMeta.Add(m);
        }
        m.TelegramBotToken = (req.BotToken ?? "").Trim();
        m.TelegramBotUsername = (req.BotUsername ?? "").Trim().TrimStart('@');
        m.TelegramBotName = (req.BotName ?? "").Trim();
        await db.SaveChangesAsync();

        // Ishlab turgan xizmat (va bot) darrov yangi tokenni ishlatishi uchun keshni yangilaymiz.
        telegram.Set(m.TelegramBotToken, m.TelegramBotUsername, m.TelegramBotName);

        return new TelegramSettingsDto(m.TelegramBotToken, m.TelegramBotUsername, m.TelegramBotName, telegram.IsConfigured);
    }

    // ---------- Push (Firebase / FCM) ----------

    [HttpGet("firebase")]
    public async Task<ActionResult<FirebaseSettingsDto>> GetFirebase()
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        var json = m?.FcmServiceAccountJson ?? "";
        return new FirebaseSettingsDto(json, FcmService.IsConfigured(json));
    }

    [HttpPut("firebase")]
    public async Task<ActionResult<FirebaseSettingsDto>> SaveFirebase(SaveFirebaseSettingsRequest req)
    {
        var json = (req.ServiceAccountJson ?? "").Trim();
        if (json.Length > 0 && !FcmService.IsConfigured(json))
            return BadRequest(new { message = "Service account JSON noto'g'ri (client_email/private_key/project_id kerak)" });
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        if (m is null) { m = new SchoolMeta(); db.SchoolMeta.Add(m); }
        m.FcmServiceAccountJson = json;
        await db.SaveChangesAsync();
        return new FirebaseSettingsDto(json, FcmService.IsConfigured(json));
    }

    [HttpPut("absence-reasons")]
    public async Task<IActionResult> SaveAbsenceReasons(SaveAbsenceReasonsRequest req)
    {
        // Mavjud id'larni saqlab qolamiz (jurnal yozuvlari reasonId orqali bog'langan).
        // Intizomiy ball (Points) "Ball sabablar"da belgilanadi — bu yerda qayta yaratilganda
        // id bo'yicha eski ballni saqlab qolamiz (aks holda 0 ga tushib qolardi).
        var oldPoints = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, r => r.Points);
        db.AbsenceReasons.RemoveRange(db.AbsenceReasons);
        db.AbsenceReasons.AddRange(req.AbsenceReasons.Select(r => new AbsenceReason
        {
            Id = string.IsNullOrWhiteSpace(r.Id) ? Guid.NewGuid().ToString() : r.Id,
            Name = r.Name,
            Short = r.Short,
            IsLate = r.IsLate,
            Points = oldPoints.GetValueOrDefault(r.Id, 0),
        }));
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Topshiriq turlari ----------

    [HttpGet("assignment-types")]
    public async Task<ActionResult<IEnumerable<AssignmentTypeDto>>> GetAssignmentTypes() =>
        await db.AssignmentTypes.Select(t => new AssignmentTypeDto(t.Id, t.Name)).ToListAsync();

    [HttpPut("assignment-types")]
    public async Task<IActionResult> SaveAssignmentTypes(SaveAssignmentTypesRequest req)
    {
        // Mavjud id'larni saqlaymiz (topshiriqlar TypeId orqali bog'langan).
        db.AssignmentTypes.RemoveRange(db.AssignmentTypes);
        db.AssignmentTypes.AddRange(req.Types
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => new AssignmentType
            {
                Id = string.IsNullOrWhiteSpace(t.Id) ? Guid.NewGuid().ToString() : t.Id,
                Name = t.Name.Trim(),
            }));
        await db.SaveChangesAsync();
        return NoContent();
    }
}
