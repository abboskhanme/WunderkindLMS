using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SchoolLms.Application.Hubs;

/// <summary>
/// Admin paneli uchun real-time push hub'i (/hubs/live). Mijoz kerakli mavzularga (topic) qo'shiladi:
///  • "gps" — avtobus joylashuvi (busLocation hodisasi),
///  • "turnstile" — turniket o'tishlari (turnstileChanged hodisasi).
/// Server yangilik bo'lganda shu guruhlarga push qiladi; mijoz UI'ni jonli yangilaydi.
/// Faqat admin/superadmin/xodim ulanadi.
/// </summary>
[Authorize(Roles = "admin,superadmin,staff")]
public class LiveHub : Hub
{
    public static string Group(string topic) => "live:" + topic;

    /// <summary>Mavzuga ulanish (masalan "gps" yoki "turnstile").</summary>
    public Task Join(string topic) => Groups.AddToGroupAsync(Context.ConnectionId, Group(topic));

    /// <summary>Mavzudan chiqish.</summary>
    public Task Leave(string topic) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(topic));
}
