using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using System.Security.Claims;

namespace SchoolLms.Application.Hubs;

/// <summary>
/// Sinf guruh chati uchun real-time SignalR hub'i (/hubs/chat). Ulanishda foydalanuvchi
/// a'zo bo'lgan sinf guruhlariga qo'shiladi; yangi xabarlar ChatService orqali shu guruhlarga
/// "message" hodisasi bilan push qilinadi. Xabar YOZISH REST orqali (ruxsat tekshiriladi),
/// bu hub faqat tarqatish (push) uchun.
/// </summary>
[Authorize]
public class ChatHub(ChatService chat) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var uid = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        // 2026-10-11: guruh chati faqat admin panelida — o'qituvchi, o'quvchi va ota-ona hech qaysi
        // kanalga qo'shilmaydi (eski ilova ulansa ham xabar olmaydi).
        if (uid is not null && role is not null && role is not (Roles.Teacher or Roles.Student or "parent"))
        {
            foreach (var className in await chat.ClassNamesForUserAsync(uid, role))
                await Groups.AddToGroupAsync(Context.ConnectionId, ChatService.Group(className));
        }
        await base.OnConnectedAsync();
    }
}
