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
public class MessagesController(
    AppDbContext db, ChatService chat, TelegramService telegram, IWebHostEnvironment env) : ControllerBase
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
        var ids = list.Select(b => b.Id).ToList();
        var stats = (await db.BroadcastRecipients.AsNoTracking()
                .Where(r => ids.Contains(r.BroadcastId))
                .Select(r => new { r.BroadcastId, r.StudentId, r.Status }).ToListAsync())
            .GroupBy(r => r.BroadcastId)
            .ToDictionary(g => g.Key, g => (
                Students: g.Select(r => r.StudentId).Distinct().Count(),
                NoTelegram: g.Count(r => r.Status == BroadcastDelivery.NoTelegram)));
        return list.Select(b => new BroadcastDto(
            b.Id, b.ClassName, b.Text, b.SenderName, b.CreatedAt.ToString("o"),
            b.RecipientCount, b.SentCount,
            stats.TryGetValue(b.Id, out var st) ? st.Students : null,
            stats.TryGetValue(b.Id, out var st2) ? st2.NoTelegram : null,
            b.ImageUrls)).ToList();
    }

    /// <summary>
    /// Bitta e'lon batafsil: kimga yuborilgan — har o'quvchi, ota-ona chati va holati (yetkazildi /
    /// yetmadi / Telegramga ulanmagan). Mijoz, 2026-10-11: "yuborilgan elonlar haqida batafsil
    /// malumot ... masalan kimga". 2026-10-11 dan oldingi e'lonlarda ro'yxat saqlanmagan.
    /// </summary>
    [HttpGet("broadcasts/{id}")]
    public async Task<ActionResult<BroadcastDetailDto>> BroadcastDetail(string id)
    {
        var b = await db.Broadcasts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (b is null) return NotFound();
        var rows = await db.BroadcastRecipients.AsNoTracking().Where(r => r.BroadcastId == id).ToListAsync();
        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToDictionaryAsync(s => s.Id);
        var recipients = rows
            .Select(r =>
            {
                students.TryGetValue(r.StudentId, out var st);
                return new BroadcastRecipientDto(
                    r.StudentId, st?.FullName ?? "O'chirilgan o'quvchi", st?.ClassName ?? "",
                    r.ParentName, r.ChatId?.ToString(), r.Status);
            })
            .OrderBy(r => r.Status == BroadcastDelivery.Sent ? 1 : 0)   // problems first
            .ThenBy(r => r.ClassName, StringComparer.Ordinal).ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();
        return new BroadcastDetailDto(
            new BroadcastDto(b.Id, b.ClassName, b.Text, b.SenderName, b.CreatedAt.ToString("o"),
                b.RecipientCount, b.SentCount, rows.Count > 0 ? studentIds.Count : null,
                rows.Count > 0 ? rows.Count(r => r.Status == BroadcastDelivery.NoTelegram) : null,
                b.ImageUrls),
            TelegramHtml.FromMarkup(b.Text), recipients, rows.Count > 0);
    }

    /// <summary>
    /// E'lon — HAQIQIY ota-onalarga Telegram xabari.
    ///
    /// <para>
    /// Qamrov (<c>scope</c>): <c>class</c> (sukut) · <c>group</c> (o'quv guruhining FAOL
    /// a'zolari) · <c>all</c> · <c>selected</c> · <c>filter</c> (S-6, pastga qarang). Xabar
    /// faqat botga RO'YXATDAN O'TGAN chatlarga boradi (<c>telegram_registrations</c>); bir bolaning bir nechta chati
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
        // Formatlangan matn (**qalin**, __kursiv__ …, 2026-10-11): yozilgani saqlanadi, Telegramga
        // esa HTML qismi bo'lib ketadi (TelegramHtml.FromMarkup — avval hammasi ekranlanadi).
        var text = req.Text?.Trim() ?? "";
        // Rasmlar (2026-10-24): fayllar shu yerda, yuborishdan OLDIN o'qiladi — yaroqsiz rasm
        // e'lonni yarim yo'lda to'xtatmasin. Rasm bo'lsa matn ixtiyoriy.
        var imageUrls = (req.ImageUrls ?? []).Select(u => (u ?? "").Trim()).Where(u => u.Length > 0).ToList();
        var (photos, imageError) = await BroadcastImages.LoadAsync(env, imageUrls, HttpContext.RequestAborted);
        if (imageError is not null) return BadRequest(new { message = imageError });
        if (photos.Count == 0 && TelegramHtml.MarkupToPlain(text).Trim().Length == 0)
            return BadRequest(new { message = "Xabar matni kerak" });
        var html = TelegramHtml.FromMarkup(text);
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
            // S-6 (students-parity.md §2.3.3) — O'QUVCHILAR RO'YXATIDAGI JORIY
            // FILTRGA mos BARCHA o'quvchi, tanlangan qatorlardan MUSTAQIL
            // (EduSchool'dagi "SMS yuborish → barcha sahifalar"). Qamrov
            // AYNAN o'sha `StudentListQuery` orqali hisoblanadi — ya'ni ekran
            // nechta o'quvchini ko'rsatsa, xabar ham AYNAN o'shalarga boradi.
            // Sahifa/tartib maydonlari e'tiborsiz: bu yerda BUTUN mos to'plam
            // kerak, bitta sahifa emas.
            case "filter":
                var filtered = await new StudentListQuery(db)
                    .AllAsync(req.Filter ?? new StudentListFilter());
                var filterIds = filtered.Select(r => r.Id).ToList();
                if (filterIds.Count == 0)
                    return BadRequest(new { message = "Filtrga mos o'quvchi topilmadi" });
                studentsQ = studentsQ.Where(s => filterIds.Contains(s.Id));
                audience = $"Filtr bo'yicha ({filterIds.Count} ta)";
                break;
            default: // class
                var cn = req.ClassName?.Trim() ?? "";
                if (cn.Length == 0) return BadRequest(new { message = "Sinf kerak" });
                studentsQ = studentsQ.Where(s => s.ClassName == cn);
                audience = cn;
                break;
        }

        // Former pupils get no school announcement, whatever the scope (client, 2026-10-08: "ular endi
        // bizning o'quvchi emas") — the archive tab's selection or filter included.
        var students = await studentsQ.Where(s => !s.IsArchived).ToListAsync();

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
        var delivery = new List<BroadcastRecipient>();
        foreach (var r in regs)
        {
            if (!byId.TryGetValue(r.StudentId, out var s)) continue;
            var message = html.Length == 0
                ? "📢 <b>Maktab e'loni</b>"
                : $"📢 <b>Maktab e'loni</b>\n\n{Personalize(html, s, r, balances.GetValueOrDefault(s.Id))}";
            // Rasmli e'lon: birinchi chatga fayl, keyingilariga Telegram'ning file_id'si ketadi.
            var ok = photos.Count == 0
                ? await telegram.SendRichAsync(r.ChatId, message)
                : await telegram.SendPhotosWithTextAsync(r.ChatId, photos, message);
            if (ok) sent++;
            delivery.Add(new BroadcastRecipient
            {
                StudentId = s.Id, ChatId = r.ChatId, ParentName = r.ParentName,
                Status = ok ? BroadcastDelivery.Sent : BroadcastDelivery.Failed,
            });
        }
        // Telegramga ulanmagan maqsadli o'quvchilar ham yoziladi — "kimga yetmadi" ko'rinsin.
        var withChat = regs.Select(r => r.StudentId).ToHashSet(StringComparer.Ordinal);
        delivery.AddRange(students.Where(s => !withChat.Contains(s.Id))
            .Select(s => new BroadcastRecipient { StudentId = s.Id, Status = BroadcastDelivery.NoTelegram }));

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
            ImageUrls = imageUrls,
        };
        db.Broadcasts.Add(bc);
        foreach (var d in delivery) d.BroadcastId = bc.Id;
        db.BroadcastRecipients.AddRange(delivery);
        await db.SaveChangesAsync();

        return new BroadcastDto(bc.Id, bc.ClassName, bc.Text, bc.SenderName,
            bc.CreatedAt.ToString("o"), bc.RecipientCount, bc.SentCount,
            students.Count, delivery.Count(d => d.Status == BroadcastDelivery.NoTelegram), bc.ImageUrls);
    }

    /// <summary>
    /// E'lon matnidagi o'rinbosarlarni shu o'quvchi ma'lumotiga moslab almashtiradi.
    /// <paramref name="balance"/> — HISOBLANGAN qoldiq (manfiy = qarz), P1-21.
    /// </summary>
    private static string Personalize(string template, Student s, TelegramRegistration reg, decimal balance)
    {
        var debt = balance < 0 ? -balance : 0m;
        var parent = string.IsNullOrWhiteSpace(reg.ParentName) ? "Ota-ona" : reg.ParentName;
        // Shablon — Telegram HTML; qiymatlar ekranlanadi (ismdagi "<" teg bo'lib qolmasin).
        var result = template;
        result = ReplaceToken(result, "{fish}", TelegramHtml.Escape(s.FullName));
        result = ReplaceToken(result, "{sinf}", TelegramHtml.Escape(s.ClassName));
        result = ReplaceToken(result, "{qarzdorlik}", Money(debt));
        result = ReplaceToken(result, "{balans}", Money(balance));
        result = ReplaceToken(result, "{ota-ona}", TelegramHtml.Escape(parent));
        result = ReplaceToken(result, "{ota_ona}", TelegramHtml.Escape(parent));
        result = ReplaceToken(result, "{telefon}", TelegramHtml.Escape(reg.Phone));
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
        // Lekin FAQAT moliya ruxsati bo'lganda: bu ekran Telegram ro'yxati uchun,
        // `messages` ruxsatiga ega har qanday xodim undan butun maktabning qarzini
        // o'qib ololmasligi kerak. Ruxsat yo'q bo'lsa qoldiq `null` — nol EMAS,
        // chunki nol "qarzi yo'q" degan YOLG'ON ma'no berardi.
        var canSeeMoney = User.HasReadPerm(PermissionCheck.Finance);
        var balances = canSeeMoney
            ? await Balances.ForManyAsync(ids)
            : new Dictionary<string, decimal>(StringComparer.Ordinal);

        return regs.Select(r =>
        {
            byId.TryGetValue(r.StudentId, out var s);
            return new TelegramParentDto(
                r.StudentId, s?.FullName ?? "", s?.ClassName ?? "",
                canSeeMoney ? balances.GetValueOrDefault(r.StudentId) : null,
                r.ParentName, r.Phone, r.ChatId.ToString(), r.CreatedAt.ToString("o"));
        }).ToList();
    }

    /// <summary>Telegram bot holati (sozlanganmi, bot foydalanuvchi nomi) — admin UI ko'rsatishi uchun.</summary>
    [HttpGet("telegram/status")]
    public ActionResult<object> TelegramStatus() =>
        Ok(new { configured = telegram.IsConfigured, botUsername = telegram.BotUsername ?? "" });
}
