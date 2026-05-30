using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Hubs;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Sinf guruh chatining umumiy mantig'i (a'zolik, xabar olish/yuborish). Admin web va
/// o'qituvchi/o'quvchi portal controllerlari shu xizmatdan foydalanadi. Xabar saqlangach
/// SignalR orqali shu sinf guruhiga (real-time) push qilinadi.
/// </summary>
public class ChatService(IAppDbContext db, IHubContext<ChatHub> hub)
{
    /// <summary>Sinf nomidan SignalR guruh nomi.</summary>
    public static string Group(string className) => $"class:{className}";

    /// <summary>
    /// Barcha xodimlar (o'qituvchilar + adminlar) uchun umumiy guruh chati kanali kaliti.
    /// Sinf nomi bo'la olmaydigan zahiraviy qiymat — ChatMessage.ClassName ustunida saqlanadi.
    /// </summary>
    public const string StaffChannel = "__xodimlar__";

    /// <summary>"since" so'rov parametrini (ISO sana) DateTime'ga aylantiradi (xato/bo'sh → null).</summary>
    public static DateTime? ParseSince(string? s) =>
        DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;

    /// <summary>
    /// Foydalanuvchi a'zo bo'lgan chat kanallari (nomlari). admin/superadmin = barcha sinflar + xodimlar;
    /// o'qituvchi = sinf rahbarligi + dars beradigan sinflar + xodimlar; o'quvchi = faqat o'z sinfi.
    /// "Xodimlar" (<see cref="StaffChannel"/>) — barcha o'qituvchi va adminlar uchun umumiy kanal.
    /// </summary>
    public async Task<List<string>> ClassNamesForUserAsync(string userId, string role)
    {
        switch (role)
        {
            case "admin":
            case "superadmin":
                {
                    var names = await db.Classes.OrderBy(c => c.Grade).ThenBy(c => c.Name)
                        .Select(c => c.Name).ToListAsync();
                    names.Add(StaffChannel);
                    return names;
                }

            case "student":
                {
                    var s = await db.Students.FirstOrDefaultAsync(x => x.UserId == userId);
                    return s is null || string.IsNullOrEmpty(s.ClassName)
                        ? new List<string>() : new List<string> { s.ClassName };
                }

            case "teacher":
                {
                    var t = await db.Teachers.FirstOrDefaultAsync(x => x.UserId == userId);
                    if (t is null) return new();
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    if (!string.IsNullOrEmpty(t.HomeroomClass)) names.Add(t.HomeroomClass);

                    // Dars beradigan sinflar — jadval template'laridan.
                    var taughtClassIds = (await db.ScheduleTemplates.Include(x => x.Lessons).ToListAsync())
                        .Where(tpl => tpl.Lessons.Any(l => l.TeacherId == t.Id))
                        .Select(tpl => tpl.ClassId).Distinct().ToList();
                    var taughtNames = await db.Classes.Where(c => taughtClassIds.Contains(c.Id))
                        .Select(c => c.Name).ToListAsync();
                    foreach (var n in taughtNames) names.Add(n);

                    var list = names.ToList();
                    list.Add(StaffChannel); // har bir o'qituvchi — xodim
                    return list;
                }

            default:
                return new();
        }
    }

    /// <summary>Foydalanuvchi shu sinf chatiga kira oladimi.</summary>
    public async Task<bool> CanAccessAsync(string userId, string role, string className)
    {
        if (role == "admin") return true;
        var names = await ClassNamesForUserAsync(userId, role);
        return names.Contains(className);
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
            CreatedAt = DateTime.UtcNow,
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
