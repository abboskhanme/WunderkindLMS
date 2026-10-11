using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Bitta o'quvchiga (va uning ota-onasiga) tegishli e'lonlar — Mini App'ning "Maktab e'lonlari"
/// kartasi va ota-ona endpointi uchun yagona qoida (2026-10-11).
///
/// <para>
/// 2026-10-11 dan har e'lonning oluvchilari saqlanadi (<c>broadcast_recipients</c>) — tanlangan,
/// filtr va guruh e'lonlari ham aniq shu bolaga tegishlisi ko'rinadi. Undan oldingilari eski qoida
/// bilan: <c>broadcasts.class_name</c> dagi qamrov YORLIG'I — sinf nomi, "&lt;sinf&gt; — qarzdorlar"
/// yoki "Barcha sinflar[ — qarzdorlar]".
/// </para>
/// </summary>
public static class BroadcastFeed
{
    public static IQueryable<Broadcast> For(IAppDbContext db, string studentId, string className) =>
        db.Broadcasts.AsNoTracking().Where(b =>
            db.BroadcastRecipients.Any(r => r.BroadcastId == b.Id && r.StudentId == studentId)
            || (!db.BroadcastRecipients.Any(r => r.BroadcastId == b.Id)
                && (b.ClassName == className
                    || b.ClassName.StartsWith(className + " —")
                    || b.ClassName.StartsWith("Barcha sinflar"))));
}
