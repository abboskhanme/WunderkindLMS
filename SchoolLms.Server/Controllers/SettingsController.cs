using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("settings")]
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

    // Web (PWA) push to'liq sozlanganmi: service account + web config (apiKey) + VAPID kalit.
    private static bool WebPushReady(string serviceAccount, string webConfig, string vapid)
    {
        if (!FcmService.IsConfigured(serviceAccount) || vapid.Trim().Length == 0) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(webConfig);
            return doc.RootElement.TryGetProperty("apiKey", out var a)
                && !string.IsNullOrWhiteSpace(a.GetString());
        }
        catch { return false; }
    }

    [HttpGet("firebase")]
    public async Task<ActionResult<FirebaseSettingsDto>> GetFirebase()
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        var json = m?.FcmServiceAccountJson ?? "";
        var web = m?.FcmWebConfigJson ?? "";
        var vapid = m?.FcmVapidKey ?? "";
        return new FirebaseSettingsDto(
            json, FcmService.IsConfigured(json), web, vapid, WebPushReady(json, web, vapid));
    }

    [HttpPut("firebase")]
    public async Task<ActionResult<FirebaseSettingsDto>> SaveFirebase(SaveFirebaseSettingsRequest req)
    {
        var json = (req.ServiceAccountJson ?? "").Trim();
        if (json.Length > 0 && !FcmService.IsConfigured(json))
            return BadRequest(new { message = "Service account JSON noto'g'ri (client_email/private_key/project_id kerak)" });
        var web = (req.WebConfigJson ?? "").Trim();
        if (web.Length > 0)
        {
            try { using var _ = System.Text.Json.JsonDocument.Parse(web); }
            catch { return BadRequest(new { message = "Web config JSON noto'g'ri (apiKey, projectId, messagingSenderId, appId bo'lishi kerak)" }); }
        }
        var vapid = (req.VapidKey ?? "").Trim();

        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        if (m is null) { m = new SchoolMeta(); db.SchoolMeta.Add(m); }
        m.FcmServiceAccountJson = json;
        m.FcmWebConfigJson = web;
        m.FcmVapidKey = vapid;
        await db.SaveChangesAsync();
        return new FirebaseSettingsDto(
            json, FcmService.IsConfigured(json), web, vapid, WebPushReady(json, web, vapid));
    }

    // ---------- Turniket / FaceID integratsiyasi ----------

    [HttpGet("turnstile")]
    public async Task<ActionResult<TurnstileSettingsDto>> GetTurnstile()
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        var teachers = (await db.Teachers.Where(t => !t.IsArchived).OrderBy(t => t.FullName).ToListAsync())
            .Select(t => new TeacherDeviceMapDto(t.Id, t.FullName, t.DeviceUserId)).ToList();
        return new TurnstileSettingsDto(
            m?.TurnstileEnabled ?? false,
            string.IsNullOrEmpty(m?.TurnstileVendor) ? "hikvision" : m!.TurnstileVendor,
            m?.TurnstileHost ?? "", m?.TurnstilePort ?? 80, m?.TurnstileUsername ?? "",
            !string.IsNullOrEmpty(m?.TurnstilePassword),
            string.IsNullOrEmpty(m?.WorkStartTime) ? "08:30" : m!.WorkStartTime,
            m?.LateGraceMinutes ?? 10, m?.TurnstileLastSync ?? "", teachers);
    }

    [HttpPut("turnstile")]
    public async Task<ActionResult<TurnstileSettingsDto>> SaveTurnstile(SaveTurnstileSettingsRequest req)
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        if (m is null) { m = new SchoolMeta(); db.SchoolMeta.Add(m); }
        m.TurnstileEnabled = req.Enabled;
        m.TurnstileVendor = (req.Vendor ?? m.TurnstileVendor).Trim().ToLowerInvariant();
        m.TurnstileHost = (req.Host ?? "").Trim();
        m.TurnstilePort = req.Port is > 0 ? req.Port.Value : 80;
        m.TurnstileUsername = (req.Username ?? "").Trim();
        // Parol bo'sh kelsa — eskisi saqlanadi (UI parolni qaytarmaydi).
        if (!string.IsNullOrEmpty(req.Password)) m.TurnstilePassword = req.Password;
        if (!string.IsNullOrEmpty(req.WorkStartTime)) m.WorkStartTime = req.WorkStartTime.Trim();
        if (req.LateGraceMinutes is >= 0) m.LateGraceMinutes = req.LateGraceMinutes.Value;

        // O'qituvchi ↔ qurilma ID moslamasi.
        if (req.Teachers is not null)
        {
            var byId = await db.Teachers.ToDictionaryAsync(t => t.Id);
            foreach (var map in req.Teachers)
                if (byId.TryGetValue(map.TeacherId, out var te))
                    te.DeviceUserId = (map.DeviceUserId ?? "").Trim();
        }
        await db.SaveChangesAsync();
        return await GetTurnstile();
    }

    // ---------- GPS (avtobus kuzatuvi) integratsiyasi ----------

    [HttpGet("gps")]
    public async Task<ActionResult<GpsSettingsDto>> GetGps()
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        var busCount = await db.Buses.CountAsync();
        return new GpsSettingsDto(
            m?.GpsEnabled ?? false, m?.GpsIngestToken ?? "",
            m?.GpsOnlineMinutes ?? 5, m?.GpsStopRadiusM ?? 60, m?.GpsStopMinMinutes ?? 3, busCount);
    }

    [HttpPut("gps")]
    public async Task<ActionResult<GpsSettingsDto>> SaveGps(SaveGpsSettingsRequest req)
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        if (m is null) { m = new SchoolMeta(); db.SchoolMeta.Add(m); }
        m.GpsEnabled = req.Enabled;
        m.GpsIngestToken = (req.IngestToken ?? "").Trim();
        if (req.OnlineMinutes is > 0) m.GpsOnlineMinutes = req.OnlineMinutes.Value;
        if (req.StopRadiusM is > 0) m.GpsStopRadiusM = req.StopRadiusM.Value;
        if (req.StopMinMinutes is > 0) m.GpsStopMinMinutes = req.StopMinMinutes.Value;
        await db.SaveChangesAsync();
        return await GetGps();
    }

    // ---------- Kamera (videokuzatuv) integratsiyasi ----------

    [HttpGet("cameras")]
    public async Task<ActionResult<CameraSettingsDto>> GetCameras()
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        var count = await db.Cameras.CountAsync();
        return new CameraSettingsDto(m?.CameraEnabled ?? false, count);
    }

    [HttpPut("cameras")]
    public async Task<ActionResult<CameraSettingsDto>> SaveCameras(SaveCameraSettingsRequest req)
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        if (m is null) { m = new SchoolMeta(); db.SchoolMeta.Add(m); }
        m.CameraEnabled = req.Enabled;
        await db.SaveChangesAsync();
        return await GetCameras();
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
