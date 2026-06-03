using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Services;
using System.Security.Claims;

namespace SchoolLms.Application.Hubs;

/// <summary>
/// Sinf guruh chati uchun real-time SignalR hub'i (/hubs/chat). Ulanishda foydalanuvchi
/// a'zo bo'lgan sinf guruhlariga qo'shiladi; yangi xabarlar ChatService orqali shu guruhlarga
/// "message" hodisasi bilan push qilinadi. Xabar YOZISH REST orqali (ruxsat tekshiriladi),
/// bu hub faqat tarqatish (push) uchun.
/// </summary>
[Authorize]
public class ChatHub(ChatService chat, ITenantContext tenant) : Hub
{
    public override async Task OnConnectedAsync()
    {
        // MUHIM (multi-tenant): SignalR hub chaqiruvi alohida DI scope'da ishlaydi, shuning uchun
        // TenantResolutionMiddleware to'ldirgan tenant bu scope'ga O'TMAYDI. Ulanish HttpContext'idagi
        // (middleware allaqachon aniqlagan) tenantni shu scope'ga ko'chiramiz — aks holda AppDbContext
        // global query filter hech narsa qaytarmaydi, foydalanuvchi hech qaysi sinf guruhiga qo'shilmaydi
        // va real-time xabarlar yetib bormaydi (chat avtomatik yangilanmaydi).
        var httpTenant = Context.GetHttpContext()?.RequestServices.GetService<ITenantContext>();
        if (httpTenant?.TenantId is { } tid)
            tenant.SetTenant(tid, httpTenant.Slug ?? "");
        else
            tenant.SetPlatform();

        var uid = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        if (uid is not null && role is not null)
        {
            foreach (var className in await chat.ClassNamesForUserAsync(uid, role))
                await Groups.AddToGroupAsync(Context.ConnectionId, ChatService.Group(tenant.TenantId, className));
        }
        await base.OnConnectedAsync();
    }
}
