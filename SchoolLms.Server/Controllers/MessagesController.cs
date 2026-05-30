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
/// Admin "Xabarlar" bo'limi: (1) sinf ota-onalariga Telegram bot orqali e'lon yuborish;
/// (2) sinf guruh chati (o'quvchilar + dars beruvchi o'qituvchilar + admin). Faqat "admin" roli.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/messages")]
public class MessagesController(AppDbContext db, ChatService chat, TelegramService telegram) : ControllerBase
{
    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    // ---------- Sinflar ro'yxati (chat/e'lon tanlash uchun) ----------

    [HttpGet("classes")]
    public async Task<ActionResult<IEnumerable<ChatClassDto>>> Classes()
    {
        var classes = await db.Classes.OrderBy(c => c.Grade).ThenBy(c => c.Name).ToListAsync();
        var students = await db.Students.Select(s => new { s.Id, s.ClassName }).ToListAsync();
        var regs = await db.TelegramRegistrations.Select(r => new { r.StudentId, r.ChatId }).ToListAsync();
        var lastByClass = (await db.ChatMessages
                .GroupBy(m => m.ClassName)
                .Select(g => new { Name = g.Key, Last = g.Max(x => x.CreatedAt) })
                .ToListAsync())
            .ToDictionary(x => x.Name, x => x.Last);

        var studentClass = students
            .Where(s => !string.IsNullOrEmpty(s.ClassName))
            .ToLookup(s => s.Id, s => s.ClassName);
        var studentCountByClass = students
            .Where(s => !string.IsNullOrEmpty(s.ClassName))
            .GroupBy(s => s.ClassName).ToDictionary(g => g.Key, g => g.Count());
        // Har sinf bo'yicha alohida (distinct) Telegram chatlar soni — e'lon oluvchilar.
        var parentChatsByClass = regs
            .Where(r => studentClass.Contains(r.StudentId))
            .GroupBy(r => studentClass[r.StudentId].First())
            .ToDictionary(g => g.Key, g => g.Select(x => x.ChatId).Distinct().Count());

        return classes.Select(c => new ChatClassDto(
            c.Name, c.Grade,
            studentCountByClass.GetValueOrDefault(c.Name, 0),
            parentChatsByClass.GetValueOrDefault(c.Name, 0),
            lastByClass.TryGetValue(c.Name, out var last) ? last.ToString("o") : null)).ToList();
    }

    /// <summary>
    /// Har bir kanal uchun oxirgi xabar vaqti (ISO) — frontend o'qilmagan xabarlarni aniqlaydi.
    /// Admin uchun barcha sinflar + xodimlar kanali qaytadi. Xabari yo'q kanal uchun null.
    /// </summary>
    [HttpGet("last-messages")]
    public async Task<ActionResult<Dictionary<string, string?>>> LastMessages()
    {
        var channels = await chat.ClassNamesForUserAsync(Uid, "admin");
        var lastByChannel = (await db.ChatMessages
                .Where(m => channels.Contains(m.ClassName))
                .GroupBy(m => m.ClassName)
                .Select(g => new { Name = g.Key, Last = g.Max(x => x.CreatedAt) })
                .ToListAsync())
            .ToDictionary(x => x.Name, x => (string?)x.Last.ToString("o"));
        return channels.ToDictionary(c => c, c => lastByChannel.GetValueOrDefault(c, null));
    }

    // ---------- Guruh chati ----------

    [HttpGet("chat/{className}")]
    public async Task<ActionResult<IEnumerable<ChatMessageDto>>> Chat(string className, [FromQuery] string? since)
        => await chat.GetMessagesAsync(className, ChatService.ParseSince(since));

    [HttpPost("chat/{className}")]
    public async Task<ActionResult<ChatMessageDto>> SendChat(string className, SendChatRequest req)
    {
        var dto = await chat.PostAsync(className, Uid, req.Text);
        return dto is null ? BadRequest(new { message = "Xabar bo'sh" }) : dto;
    }

    // ---------- E'lon (Telegram) ----------

    [HttpGet("broadcasts")]
    public async Task<ActionResult<IEnumerable<BroadcastDto>>> Broadcasts([FromQuery] string? className)
    {
        var q = db.Broadcasts.AsQueryable();
        if (!string.IsNullOrEmpty(className)) q = q.Where(b => b.ClassName == className);
        var list = await q.OrderByDescending(b => b.CreatedAt).Take(100).ToListAsync();
        return list.Select(b => new BroadcastDto(
            b.Id, b.ClassName, b.Text, b.SenderName, b.CreatedAt.ToString("o"),
            b.RecipientCount, b.SentCount)).ToList();
    }

    [HttpPost("broadcast")]
    public async Task<ActionResult<BroadcastDto>> SendBroadcast(SendBroadcastRequest req)
    {
        var className = req.ClassName?.Trim() ?? "";
        var text = req.Text?.Trim() ?? "";
        if (className.Length == 0 || text.Length == 0)
            return BadRequest(new { message = "Sinf va xabar matni kerak" });

        // Shu sinf o'quvchilarining Telegramda ro'yxatdan o'tgan ota-onalari (alohida chatlar).
        var studentIds = await db.Students.Where(s => s.ClassName == className)
            .Select(s => s.Id).ToListAsync();
        var chatIds = await db.TelegramRegistrations
            .Where(r => studentIds.Contains(r.StudentId))
            .Select(r => r.ChatId).Distinct().ToListAsync();

        var message = $"📢 {className} sinfi — maktab e'loni:\n\n{text}";
        var sent = 0;
        foreach (var chatId in chatIds)
            if (await telegram.SendMessageAsync(chatId, message)) sent++;

        var user = await db.Users.FindAsync(Uid);
        var bc = new Broadcast
        {
            ClassName = className,
            Text = text,
            SenderUserId = Uid,
            SenderName = user?.FullName ?? "Administrator",
            CreatedAt = DateTime.UtcNow,
            RecipientCount = chatIds.Count,
            SentCount = sent,
        };
        db.Broadcasts.Add(bc);
        await db.SaveChangesAsync();

        return new BroadcastDto(bc.Id, bc.ClassName, bc.Text, bc.SenderName,
            bc.CreatedAt.ToString("o"), bc.RecipientCount, bc.SentCount);
    }

    // ---------- Telegram ro'yxati (ota-onalar) ----------

    [HttpGet("telegram/registrations")]
    public async Task<ActionResult<IEnumerable<TelegramParentDto>>> Registrations([FromQuery] string? className)
    {
        var studentsQ = db.Students.AsQueryable();
        if (!string.IsNullOrEmpty(className)) studentsQ = studentsQ.Where(s => s.ClassName == className);
        var students = await studentsQ.ToListAsync();
        var byId = students.ToDictionary(s => s.Id);
        var ids = students.Select(s => s.Id).ToList();

        var regs = await db.TelegramRegistrations
            .Where(r => ids.Contains(r.StudentId))
            .OrderByDescending(r => r.CreatedAt).ToListAsync();

        return regs.Select(r => new TelegramParentDto(
            r.StudentId, byId.TryGetValue(r.StudentId, out var s) ? s.FullName : "",
            r.ParentName, r.Phone, r.ChatId.ToString(), r.CreatedAt.ToString("o"))).ToList();
    }

    /// <summary>Telegram bot holati (sozlanganmi, bot foydalanuvchi nomi) — admin UI ko'rsatishi uchun.</summary>
    [HttpGet("telegram/status")]
    public ActionResult<object> TelegramStatus() =>
        Ok(new { configured = telegram.IsConfigured, botUsername = telegram.BotUsername ?? "" });
}
