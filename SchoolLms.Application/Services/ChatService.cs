using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Hubs;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  G-17 — O'QUV GURUHINING CHAT KANALI (students-parity.md §2.1.6).
// ===========================================================================
//
//  QAROR: HA, GURUHNING O'Z KANALI BO'LADI
//  ---------------------------------------
//  Kanal bugun SINF NOMI bilan kalitlanadi (`chat_messages.class_name`), ya'ni
//  o'qituvchi faqat sinf rahbarligi yoki sinfda darsi orqali kanalga tushadi.
//  FAQAT GURUHDA dars beradigan o'qituvchi hech qanday kanalga tushmaydi —
//  u guruh ota-onalariga umuman yeta olmaydi. Guruh bir nechta sinfdan
//  yig'ilgani uchun "boquvchi sinflar kanaliga qo'shamiz" ham yaramaydi:
//  o'qituvchi o'zi o'qitmaydigan bolalarning ota-onalari yozishmasini ham
//  ko'rib qolardi. Shuning uchun guruh — ALOHIDA kanal.
//
//  KALIT: `grp:<guruh id>` — VA NEGA AYNAN SHUNDAY
//  ----------------------------------------------
//  · Kalit SINF NOMI bilan TO'QNASHMASLIGI shart. Sinf nomi — erkin matn
//    ("5-A"), ya'ni nazariy jihatdan har qanday satr bo'lishi mumkin. `grp:`
//    prefiksi + GUID esa faqat MAVJUD guruh id'si bilan mos kelganda
//    to'qnashardi, uni esa admin qo'lda yozib chiqa olmaydi. Xuddi shu usul
//    allaqachon ishlatilgan: `__xodimlar__` (<see cref="StaffChannel"/>).
//  · NOM emas, ID: guruh nomi o'zgarishi mumkin (sinf nomini o'zgartirish
//    G-1 dagi kabi butun yozishmani yetim qoldirardi), id esa o'zgarmaydi.
//  · Ustun turi o'zgarmaydi — `class_name` da kalit turadi, migratsiya kerak
//    emas (1-qoida).
//
//  O'CHIRGICH O'CHIQ = BUGUNGI KANALLAR
//  ------------------------------------
//  `group_lessons_enabled` o'chiq ekan guruh kanali HECH KIMNING ro'yxatida
//  paydo bo'lmaydi va `CanAccessAsync` uni rad etadi — ya'ni bugungi ekran.
// ===========================================================================

/// <summary>
/// Sinf va o'quv guruhi chatining umumiy mantig'i (a'zolik, xabar olish/yuborish).
/// Admin web va o'qituvchi/o'quvchi portal controllerlari shu xizmatdan foydalanadi.
/// Xabar saqlangach SignalR orqali shu kanal guruhiga (real-time) push qilinadi.
/// </summary>
public class ChatService(IAppDbContext db, IHubContext<ChatHub> hub)
{
    /// <summary>Kanal kalitidan SignalR guruh nomi.</summary>
    public static string Group(string className) => $"class:{className}";

    /// <summary>
    /// Barcha xodimlar (o'qituvchilar + adminlar) uchun umumiy guruh chati kanali kaliti.
    /// Sinf nomi bo'la olmaydigan zahiraviy qiymat — ChatMessage.ClassName ustunida saqlanadi.
    /// </summary>
    public const string StaffChannel = "__xodimlar__";

    /// <summary>
    /// O'quv guruhi kanalining prefiksi: to'liq kalit — <c>grp:&lt;guruh id&gt;</c>
    /// (G-17). Fayl boshidagi izohda nega aynan shunday.
    /// </summary>
    public const string GroupChannelPrefix = "grp:";

    /// <summary>Guruh id'sidan kanal kaliti.</summary>
    public static string GroupChannel(Guid groupId) => GroupChannelPrefix + groupId;

    /// <summary>Kanal kaliti o'quv guruhiniki mi.</summary>
    public static bool IsGroupChannel(string channel) =>
        channel.StartsWith(GroupChannelPrefix, StringComparison.Ordinal);

    /// <summary>Kanal kalitidan guruh id'si (guruh kanali bo'lmasa — null).</summary>
    public static Guid? GroupIdOf(string channel) =>
        IsGroupChannel(channel) && Guid.TryParse(channel[GroupChannelPrefix.Length..], out var id)
            ? id
            : null;

    /// <summary>"since" so'rov parametrini (ISO sana) DateTime'ga aylantiradi (xato/bo'sh → null).</summary>
    public static DateTime? ParseSince(string? s) =>
        DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;

    /// <summary>
    /// Foydalanuvchi a'zo bo'lgan chat kanallari (kalitlari). admin/superadmin = barcha sinflar
    /// + o'quv guruhlari + xodimlar; o'qituvchi = sinf rahbarligi + dars beradigan sinflar
    /// + o'z guruhlari + xodimlar; o'quvchi = o'z sinfi + o'z guruhlari.
    /// "Xodimlar" (<see cref="StaffChannel"/>) — barcha o'qituvchi va adminlar uchun umumiy kanal.
    ///
    /// <para>
    /// Guruh kanallari (<c>grp:&lt;id&gt;</c>) FAQAT cut-over o'chirgichi yoqilganda qo'shiladi —
    /// o'chiq bo'lsa ro'yxat bugungisining aynan o'zi (G-17, §4.3).
    /// </para>
    /// </summary>
    public async Task<List<string>> ClassNamesForUserAsync(string userId, string role) =>
        [.. (await ChannelsForUserAsync(userId, role)).Select(c => c.Key)];

    /// <summary>
    /// <see cref="ClassNamesForUserAsync"/> ning NOMLI varianti: kalit + odam
    /// ko'radigan nom + turi. Guruh kaliti (<c>grp:&lt;GUID&gt;</c>) ekranda
    /// ko'rsatib bo'lmaydigan satr, shuning uchun nom serverda beriladi —
    /// har bir mijoz (admin SPA, o'qituvchi PWA, Mini App) uni alohida
    /// yechishga urinmasin.
    /// </summary>
    public async Task<List<ChatChannelDto>> ChannelsForUserAsync(string userId, string role)
    {
        switch (role)
        {
            case "admin":
            case "superadmin":
                {
                    var channels = (await db.Classes.OrderBy(c => c.Grade).ThenBy(c => c.Name)
                            .Select(c => c.Name).ToListAsync())
                        .Select(ClassChannel).ToList();
                    channels.AddRange(await GroupChannelsAsync(null));
                    channels.Add(Staff);
                    return channels;
                }

            case "student":
                {
                    var s = await db.Students.FirstOrDefaultAsync(x => x.UserId == userId);
                    if (s is null || string.IsNullOrEmpty(s.ClassName)) return [];
                    var channels = new List<ChatChannelDto> { ClassChannel(s.ClassName) };
                    channels.AddRange(await GroupChannelsAsync(studentId: s.Id));
                    return channels;
                }

            case "teacher":
                {
                    var t = await db.Teachers.FirstOrDefaultAsync(x => x.UserId == userId);
                    if (t is null) return [];
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    if (!string.IsNullOrEmpty(t.HomeroomClass)) names.Add(t.HomeroomClass);

                    // Dars beradigan sinflar — jadval template'laridan.
                    var taughtClassIds = (await db.ScheduleTemplates.Include(x => x.Lessons).ToListAsync())
                        .Where(tpl => tpl.Lessons.Any(l => l.TeacherId == t.Id))
                        .Select(tpl => tpl.ClassId).Distinct().ToList();
                    var taughtNames = await db.Classes.Where(c => taughtClassIds.Contains(c.Id))
                        .Select(c => c.Name).ToListAsync();
                    foreach (var n in taughtNames) names.Add(n);

                    var channels = names.Select(ClassChannel).ToList();
                    channels.AddRange(await GroupChannelsAsync(teacher: t));
                    channels.Add(Staff); // har bir o'qituvchi — xodim
                    return channels;
                }

            default:
                return [];
        }
    }

    /// <summary>Foydalanuvchi shu kanalga (sinf yoki o'quv guruhi) kira oladimi.</summary>
    public async Task<bool> CanAccessAsync(string userId, string role, string className)
    {
        if (role == "admin") return true;
        var names = await ClassNamesForUserAsync(userId, role);
        return names.Contains(className);
    }

    private static ChatChannelDto ClassChannel(string name) =>
        new(name, name, LessonOwnerKind.Class);

    private static ChatChannelDto Staff =>
        new(StaffChannel, "Xodimlar guruhi", "staff");

    /// <summary>
    /// O'quv guruhi kanallari. <paramref name="teacher"/> berilsa — o'sha
    /// o'qituvchining guruhlari (biriktirilgan YOKI jadvalda darsi bor);
    /// <paramref name="studentId"/> berilsa — o'quvchining faol guruhlari;
    /// ikkalasi ham null — hamma arxivlanmagan guruh (admin).
    ///
    /// <para>O'chirgich o'chiq bo'lsa — HAR DOIM bo'sh ro'yxat.</para>
    /// </summary>
    private async Task<List<ChatChannelDto>> GroupChannelsAsync(
        Teacher? teacher = null, string? studentId = null)
    {
        if (!await LessonRoster.GroupLessonsEnabledAsync(db)) return [];

        var groups = await db.StudyGroups.AsNoTracking()
            .Where(g => !g.IsArchived).OrderBy(g => g.Name).ToListAsync();
        if (groups.Count == 0) return [];

        HashSet<Guid>? allowed = null;
        if (teacher is not null)
        {
            allowed = (await db.StudyGroupTeachers.AsNoTracking()
                .Where(x => x.TeacherId == teacher.Id).Select(x => x.GroupId).ToListAsync()).ToHashSet();
            // Jadvalda guruh darsi bo'lgan o'qituvchi ham kanalga tushadi.
            var taught = (await db.ScheduleTemplates.AsNoTracking().Include(x => x.Lessons)
                    .Where(x => x.OwnerKind == LessonOwnerKind.Group).ToListAsync())
                .Where(tpl => tpl.Lessons.Any(l => l.TeacherId == teacher.Id))
                .Select(tpl => tpl.ClassId);
            foreach (var id in taught)
                if (Guid.TryParse(id, out var gid)) allowed.Add(gid);
        }
        else if (studentId is not null)
        {
            allowed = (await db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.StudentId == studentId && m.LeftOn == null)
                .Select(m => m.GroupId).ToListAsync()).ToHashSet();
        }

        return [.. groups
            .Where(g => allowed is null || allowed.Contains(g.Id))
            .Select(g => new ChatChannelDto(GroupChannel(g.Id), g.Name, LessonOwnerKind.Group))];
    }

    /// <summary>
    /// Sinf chatidagi xabarlar. since=null bo'lsa — eng so'nggi 200 ta (vaqt bo'yicha o'sish
    /// tartibida); since berilsa — shu vaqtdan keyingilar (yangilanish uchun).
    /// </summary>
    public async Task<List<ChatMessageDto>> GetMessagesAsync(string className, DateTime? since)
    {
        if (since is null)
        {
            var recent = await db.ChatMessages
                .Where(m => m.ClassName == className)
                .OrderByDescending(m => m.CreatedAt).Take(200).ToListAsync();
            recent.Reverse();
            return recent.Select(ToDto).ToList();
        }

        var after = await db.ChatMessages
            .Where(m => m.ClassName == className && m.CreatedAt > since)
            .OrderBy(m => m.CreatedAt).ToListAsync();
        return after.Select(ToDto).ToList();
    }

    /// <summary>
    /// Sinf chatiga xabar yozadi (jo'natuvchi nomi/roli akkauntdan olinadi), saqlaydi va
    /// SignalR orqali shu sinf guruhiga push qiladi. Bo'sh matn yuborilmaydi.
    /// </summary>
    public async Task<ChatMessageDto?> PostAsync(string className, string userId, string text)
    {
        text = text?.Trim() ?? "";
        if (text.Length == 0) return null;

        var user = await db.Users.FindAsync(userId);
        var msg = new ChatMessage
        {
            ClassName = className,
            SenderUserId = userId,
            SenderName = user?.FullName ?? "Foydalanuvchi",
            SenderRole = user?.Role ?? "",
            Text = text,
            CreatedAt = AppClock.Now,
        };
        db.ChatMessages.Add(msg);
        await db.SaveChangesAsync();

        var dto = ToDto(msg);
        await hub.Clients.Group(Group(className)).SendAsync("message", dto);
        return dto;
    }

    private static ChatMessageDto ToDto(ChatMessage m) => new(
        m.Id, m.ClassName, m.SenderUserId, m.SenderName, m.SenderRole, m.Text, m.CreatedAt.ToString("o"));
}
