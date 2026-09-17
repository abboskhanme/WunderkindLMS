using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Billing;
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
[Authorize]
[AdminPerm("messages")]
[Route("api/admin/messages")]
public class MessagesController(AppDbContext db, ChatService chat, TelegramService telegram, FcmService fcm) : ControllerBase
{
    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    /// <summary>
    /// <c>{balans}</c> / <c>{qarzdorlik}</c> o'rinbosarlari uchun qoldiq manbai.
    ///
    /// <para>
    /// <b>P1-21:</b> ilgari o'quvchi qatoridagi <c>balance</c> ustuni o'qilardi;
    /// endi qoldiq <c>invoices</c> va <c>payment_allocations</c> dan hisoblanadi.
    /// Har e'lon uchun BITTA partiya so'rov yuboriladi (o'quvchi boshiga emas) —
    /// 500 ta ota-onaga e'lon 500 ta so'rov bo'lib ketmasin.
    /// </para>
    /// </summary>
    private StudentBalanceQuery Balances => new(db);

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
    /// Chat kanallari NOMI bilan (G-17, students-parity.md §2.1.6): sinflar,
    /// o'quv guruhlari va xodimlar kanali.
    ///
    /// <para>
    /// Nega alohida endpoint: <c>classes</c> sinf NOMINI kalit sifatida
    /// qaytaradi, guruh kanalining kaliti esa <c>grp:&lt;id&gt;</c> — uni
    /// ekranda ko'rsatib bo'lmaydi. Guruhlar FAQAT cut-over o'chirgichi
    /// yoqilganda paydo bo'ladi (§4.3).
    /// </para>
    /// </summary>
    [HttpGet("channels")]
    public async Task<ActionResult<IEnumerable<ChatChannelDto>>> Channels() =>
        await chat.ChannelsForUserAsync(Uid, "admin");

    /// <summary>
    /// Har bir kanal uchun oxirgi xabar vaqti (ISO) — frontend o'qilmagan xabarlarni aniqlaydi.
    /// Admin uchun barcha sinflar + o'quv guruhlari + xodimlar kanali qaytadi.
    /// Xabari yo'q kanal uchun null.
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

    /// <summary>
    /// E'lon — HAQIQIY ota-onalarga Telegram xabari.
    ///
    /// <para>
    /// Qamrov (<c>scope</c>): <c>class</c> (sukut) · <c>group</c> (o'quv guruhining FAOL
    /// a'zolari) · <c>all</c> · <c>selected</c>. Xabar faqat botga RO'YXATDAN O'TGAN
    /// chatlarga boradi (<c>telegram_registrations</c>); bir bolaning bir nechta chati
    /// bo'lsa — har biriga. Matn har bola uchun alohida moslanadi (<see cref="Personalize"/>)
    /// va "📢 Maktab e'loni" sarlavhasi bilan ketadi.
    /// </para>
    /// <para>
    /// Javobdagi <c>recipientCount</c> — mos kelgan chatlar soni, <c>sentCount</c> — Telegram
    /// HAQIQATAN qabul qilgan xabarlar soni. Bot sozlanmagan bo'lsa (testda ham)
    /// <c>TelegramService.IsConfigured</c> false bo'ladi, hech qayerga so'rov ketmaydi va
    /// <c>sentCount = 0</c> qaytadi — e'lon esa tarixga baribir yoziladi.
    /// </para>
    /// </summary>
    [HttpPost("broadcast")]
    public async Task<ActionResult<BroadcastDto>> SendBroadcast(SendBroadcastRequest req)
    {
        var text = req.Text?.Trim() ?? "";
        if (text.Length == 0) return BadRequest(new { message = "Xabar matni kerak" });
        var scope = (req.Scope ?? "class").Trim().ToLowerInvariant();

        // Qamrov bo'yicha maqsadli o'quvchilar to'plami.
        var studentsQ = db.Students.AsQueryable();
        string audience;
        switch (scope)
        {
            case "selected":
                var ids = req.StudentIds ?? new();
                if (ids.Count == 0) return BadRequest(new { message = "Hech kim tanlanmadi" });
                studentsQ = studentsQ.Where(s => ids.Contains(s.Id));
                audience = $"Tanlangan ({ids.Count})";
                break;
            // O'QUV GURUHI (G-8, students-parity.md §2.1.1 "Roster page") —
            // guruhning FAOL a'zolarining ota-onalari.
            //
            // Nega o'quvchining sinfi emas, guruh a'zoligi bo'yicha: guruhni bir
            // nechta sinf boqadi, ya'ni "5-A ga e'lon" guruhning yarmiga yetmasdi.
            //
            // Nega `group_lessons_enabled` o'chirgichiga BOG'LIQ EMAS: o'chirgich
            // guruh DARSLARINI (jadval, jurnal, maosh) ushlab turadi; guruhning
            // o'zi va ro'yxati esa undan oldin ham bor va ekranda ko'rinadi.
            // Bu yerda hech qanday ro'yxat KENGAYMAYDI — qamrov faqat chaqiruvchi
            // guruhni ANIQ ko'rsatganda ishlaydi.
            case "group":
                if (!Guid.TryParse((req.GroupId ?? "").Trim(), out var groupId))
                    return BadRequest(new { message = "Guruh tanlanmadi" });
                var group = await db.StudyGroups.FirstOrDefaultAsync(g => g.Id == groupId);
                if (group is null) return NotFound(new { message = "Guruh topilmadi" });
                var memberIds = await db.StudyGroupMembers
                    .Where(m => m.GroupId == groupId && m.LeftOn == null)
                    .Select(m => m.StudentId).ToListAsync();
                studentsQ = studentsQ.Where(s => memberIds.Contains(s.Id));
                audience = $"Guruh: {group.Name}";
                break;
            case "all":
                audience = "Barcha sinflar";
                break;
            default: // class
                var cn = req.ClassName?.Trim() ?? "";
                if (cn.Length == 0) return BadRequest(new { message = "Sinf kerak" });
                studentsQ = studentsQ.Where(s => s.ClassName == cn);
                audience = cn;
                break;
        }

        var students = await studentsQ.ToListAsync();

        // Qoldiq HISOBLANADI (P1-21) — bitta partiya so'rov, siklda emas.
        var balances = await Balances.ForManyAsync([.. students.Select(s => s.Id)]);

        if (req.OnlyDebtors)
        {
            students = students.Where(s => balances.GetValueOrDefault(s.Id) < 0m).ToList();
            audience += " — qarzdorlar";
        }
        var byId = students.ToDictionary(s => s.Id);
        var sids = students.Select(s => s.Id).ToList();

        // Har bir o'quvchi (ro'yxatdagi chat) uchun matn alohida moslashtiriladi (mail-merge).
        var regs = await db.TelegramRegistrations
            .Where(r => sids.Contains(r.StudentId))
            .ToListAsync();

        var sent = 0;
        foreach (var r in regs)
        {
            if (!byId.TryGetValue(r.StudentId, out var s)) continue;
            var message = $"📢 Maktab e'loni\n\n{Personalize(text, s, r, balances.GetValueOrDefault(s.Id))}";
            if (await telegram.SendMessageAsync(r.ChatId, message)) sent++;
        }

        var user = await db.Users.FindAsync(Uid);
        var bc = new Broadcast
        {
            ClassName = audience,
            Text = text,
            SenderUserId = Uid,
            SenderName = user?.FullName ?? "Administrator",
            CreatedAt = AppClock.Now,
            RecipientCount = regs.Count,
            SentCount = sent,
        };
        db.Broadcasts.Add(bc);
        await db.SaveChangesAsync();

        return new BroadcastDto(bc.Id, bc.ClassName, bc.Text, bc.SenderName,
            bc.CreatedAt.ToString("o"), bc.RecipientCount, bc.SentCount);
    }

    /// <summary>
    /// E'lon matnidagi o'rinbosarlarni shu o'quvchi ma'lumotiga moslab almashtiradi.
    /// <paramref name="balance"/> — HISOBLANGAN qoldiq (manfiy = qarz), P1-21.
    /// </summary>
    private static string Personalize(string template, Student s, TelegramRegistration reg, decimal balance)
    {
        var debt = balance < 0 ? -balance : 0m;
        var parent = string.IsNullOrWhiteSpace(reg.ParentName) ? "Ota-ona" : reg.ParentName;
        var result = template;
        result = ReplaceToken(result, "{fish}", s.FullName);
        result = ReplaceToken(result, "{sinf}", s.ClassName);
        result = ReplaceToken(result, "{qarzdorlik}", Money(debt));
        result = ReplaceToken(result, "{balans}", Money(balance));
        result = ReplaceToken(result, "{ota-ona}", parent);
        result = ReplaceToken(result, "{ota_ona}", parent);
        result = ReplaceToken(result, "{telefon}", reg.Phone);
        return result;
    }

    private static string ReplaceToken(string input, string token, string value) =>
        System.Text.RegularExpressions.Regex.Replace(
            input,
            System.Text.RegularExpressions.Regex.Escape(token),
            (value ?? "").Replace("$", "$$"),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>So'm formatlash: 1 500 000 so'm (probel bilan ajratilgan).</summary>
    private static string Money(decimal v)
    {
        var nfi = new System.Globalization.NumberFormatInfo { NumberGroupSeparator = " ", NumberDecimalDigits = 0 };
        return v.ToString("#,0", nfi) + " so'm";
    }

    /// <summary>Push matnini o'quvchi (ota-ona akkaunti) ma'lumotiga moslaydi.
    /// <paramref name="balance"/> — HISOBLANGAN qoldiq (P1-21).</summary>
    private static string PersonalizePush(string text, Student s, decimal balance)
    {
        var debt = balance < 0 ? -balance : 0m;
        var parent = string.IsNullOrWhiteSpace(s.ParentFullName) ? "Ota-ona" : s.ParentFullName;
        var r = text;
        r = ReplaceToken(r, "{fish}", s.FullName);
        r = ReplaceToken(r, "{sinf}", s.ClassName);
        r = ReplaceToken(r, "{qarzdorlik}", Money(debt));
        r = ReplaceToken(r, "{balans}", Money(balance));
        r = ReplaceToken(r, "{ota-ona}", parent);
        r = ReplaceToken(r, "{ota_ona}", parent);
        r = ReplaceToken(r, "{telefon}", s.ParentPhone);
        return r;
    }

    /// <summary>Push matnini o'qituvchiga moslaydi — {fish} ismi, qolgan o'quvchi-o'rinbosarlari bo'sh.</summary>
    private static string PersonalizeTeacherPush(string text, Teacher t)
    {
        var r = ReplaceToken(text, "{fish}", t.FullName);
        foreach (var tok in new[] { "{sinf}", "{qarzdorlik}", "{balans}", "{ota-ona}", "{ota_ona}", "{telefon}" })
            r = ReplaceToken(r, tok, "");
        return r;
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

        // Qoldiq HISOBLANADI (P1-21) — bitta partiya so'rov butun ro'yxat uchun.
        var balances = await Balances.ForManyAsync(ids);

        return regs.Select(r =>
        {
            byId.TryGetValue(r.StudentId, out var s);
            return new TelegramParentDto(
                r.StudentId, s?.FullName ?? "", s?.ClassName ?? "",
                balances.GetValueOrDefault(r.StudentId),
                r.ParentName, r.Phone, r.ChatId.ToString(), r.CreatedAt.ToString("o"));
        }).ToList();
    }

    /// <summary>Telegram bot holati (sozlanganmi, bot foydalanuvchi nomi) — admin UI ko'rsatishi uchun.</summary>
    [HttpGet("telegram/status")]
    public ActionResult<object> TelegramStatus() =>
        Ok(new { configured = telegram.IsConfigured, botUsername = telegram.BotUsername ?? "" });

    // ---------- Push (Firebase / FCM) ----------

    /// <summary>Firebase (push) sozlanganmi — admin UI ko'rsatishi uchun.</summary>
    [HttpGet("push/status")]
    public async Task<ActionResult<object>> PushStatus()
    {
        var m = await db.SchoolMeta.FirstOrDefaultAsync();
        return Ok(new { configured = FcmService.IsConfigured(m?.FcmServiceAccountJson) });
    }

    /// <summary>"Tanlab" push uchun oluvchilar ro'yxati: ota-onalar (o'quvchi akkaunti) + o'qituvchilar.</summary>
    [HttpGet("push/recipients")]
    public async Task<ActionResult<IEnumerable<PushRecipientDto>>> PushRecipients()
    {
        var students = await db.Students.Where(s => !s.IsArchived && s.UserId != null)
            .Select(s => new { s.UserId, s.FullName, s.ClassName }).ToListAsync();
        var teachers = await db.Teachers.Where(t => !t.IsArchived && t.UserId != null)
            .Select(t => new { t.UserId, t.FullName }).ToListAsync();
        var withDevice = (await db.DeviceTokens.Select(d => d.UserId).Distinct().ToListAsync()).ToHashSet();

        var list = new List<PushRecipientDto>();
        foreach (var s in students)
            list.Add(new PushRecipientDto(s.UserId!, s.FullName, "Ota-ona", s.ClassName, withDevice.Contains(s.UserId!)));
        foreach (var t in teachers)
            list.Add(new PushRecipientDto(t.UserId!, t.FullName, "O'qituvchi", "", withDevice.Contains(t.UserId!)));
        return list
            .OrderBy(r => r.Group, StringComparer.Ordinal)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    [HttpGet("push")]
    public async Task<ActionResult<IEnumerable<PushMessageDto>>> PushHistory()
    {
        var list = await db.PushMessages.OrderByDescending(p => p.CreatedAt).Take(100).ToListAsync();
        return list.Select(p => new PushMessageDto(
            p.Id, p.Audience, p.Title, p.Body, p.SenderName, p.CreatedAt.ToString("o"),
            p.RecipientCount, p.SentCount)).ToList();
    }

    /// <summary>
    /// Ilovaga push yuboradi. Audience "parents" (ixtiyoriy ClassName bilan) yoki "teachers".
    /// Maqsadli foydalanuvchilarning ro'yxatdan o'tgan qurilma tokenlariga FCM orqali yuboriladi.
    /// </summary>
    [HttpPost("push/send")]
    public async Task<ActionResult<PushMessageDto>> SendPush(SendPushRequest req)
    {
        var title = (req.Title ?? "").Trim();
        var body = (req.Body ?? "").Trim();
        if (title.Length == 0 && body.Length == 0)
            return BadRequest(new { message = "Sarlavha yoki matn kerak" });
        var audience = (req.Audience ?? "parents").Trim().ToLowerInvariant();

        List<string> userIds;
        string label;
        if (audience == "selected")
        {
            userIds = (req.UserIds ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
            if (userIds.Count == 0) return BadRequest(new { message = "Hech kim tanlanmadi" });
            label = $"Tanlangan ({userIds.Count})";
        }
        else if (audience == "teachers")
        {
            userIds = await db.Teachers.Where(t => !t.IsArchived && t.UserId != null)
                .Select(t => t.UserId!).ToListAsync();
            label = "O'qituvchilar";
        }
        else
        {
            var q = db.Students.Where(s => !s.IsArchived && s.UserId != null);
            var cn = req.ClassName?.Trim() ?? "";
            if (cn.Length > 0) { q = q.Where(s => s.ClassName == cn); label = $"Ota-onalar — {cn}"; }
            else label = "Ota-onalar";
            userIds = await q.Select(s => s.UserId!).ToListAsync();
        }

        // Per-oluvchi moslash uchun: foydalanuvchi → o'quvchi/o'qituvchi + uning tokenlari.
        var students = await db.Students.Where(s => s.UserId != null && userIds.Contains(s.UserId)).ToListAsync();
        var studentByUser = students.GroupBy(s => s.UserId!).ToDictionary(g => g.Key, g => g.First());
        var teachers = await db.Teachers.Where(t => t.UserId != null && userIds.Contains(t.UserId)).ToListAsync();
        var teacherByUser = teachers.GroupBy(t => t.UserId!).ToDictionary(g => g.Key, g => g.First());
        var tokensByUser = (await db.DeviceTokens.Where(d => userIds.Contains(d.UserId)).ToListAsync())
            .GroupBy(d => d.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Token).Distinct().ToList());

        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        var json = meta?.FcmServiceAccountJson ?? "";
        // Qoldiq HISOBLANADI (P1-21) — matn moslashdan OLDIN, bitta partiyada.
        var balances = await Balances.ForManyAsync([.. students.Select(x => x.Id)]);
        var recipientCount = tokensByUser.Sum(kv => kv.Value.Count);
        var sent = 0;
        // Har bir foydalanuvchiga matn o'rinbosarlari moslab yuboriladi (o'quvchi/ota-ona ma'lumoti bilan).
        foreach (var (userId, toks) in tokensByUser)
        {
            var t = title;
            var b = body;
            if (studentByUser.TryGetValue(userId, out var st))
            {
                var balance = balances.GetValueOrDefault(st.Id);
                t = PersonalizePush(title, st, balance);
                b = PersonalizePush(body, st, balance);
            }
            else if (teacherByUser.TryGetValue(userId, out var tch))
            {
                t = PersonalizeTeacherPush(title, tch);
                b = PersonalizeTeacherPush(body, tch);
            }
            sent += await fcm.SendAsync(json, toks, t, b);
        }

        var user = await db.Users.FindAsync(Uid);
        var pm = new PushMessage
        {
            Audience = label,
            Title = title,
            Body = body,
            SenderUserId = Uid,
            SenderName = user?.FullName ?? "Administrator",
            CreatedAt = AppClock.Now,
            RecipientCount = recipientCount,
            SentCount = sent,
        };
        db.PushMessages.Add(pm);
        await db.SaveChangesAsync();
        return new PushMessageDto(pm.Id, pm.Audience, pm.Title, pm.Body, pm.SenderName,
            pm.CreatedAt.ToString("o"), pm.RecipientCount, pm.SentCount);
    }
}
