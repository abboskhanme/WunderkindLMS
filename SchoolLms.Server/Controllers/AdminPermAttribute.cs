using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Admin-bo'lim controlleri uchun ruxsat darvozasi (xodim/staff rollari uchun).
/// <list type="bullet">
///   <item><b>admin / superadmin</b> — to'liq kirish (ruxsat tekshirilmaydi).</item>
///   <item><b>staff</b> — <b>O'QISH</b> (GET/HEAD) har doim ochiq: bir bo'lim sahifasi boshqa
///     bo'lim ma'lumotini o'qishi (masalan Moliya → o'quvchilar ro'yxati) buzilmasligi uchun.
///     <b>YOZISH</b> (POST/PUT/DELETE/PATCH) esa FAQAT shu bo'lim ruxsati (<see cref="ClaimType"/>
///     claim'i) bo'lganda ruxsat etiladi.</item>
///   <item>Boshqa rollar (teacher/student/parent) — taqiqlanadi.</item>
/// </list>
/// Ruxsat claim'lari tokenga yozilmaydi — ular HAR so'rovda DB'dan (Program.cs OnTokenValidated)
/// yuklanadi. Shuning uchun superadmin xodim ruxsatini o'zgartirsa, xodim qayta login qilmasdan
/// darrov yangi ruxsat bilan ishlaydi.
///
/// <para>
/// <b>Opt-in read gate — <see cref="GatedRead"/></b> (admission-and-testing.md §4.3).
/// The open read above is right for pupil lists and finance categories, and wrong
/// for a controller whose GET returns secrets — the question bank returns the
/// correct answers of a live entrance exam. <c>[AdminPerm("admission", GatedRead = true)]</c>
/// makes a staff account need the permission for GET/HEAD/OPTIONS too. The default
/// is <c>false</c>, so every existing <c>[AdminPerm("...")]</c> behaves exactly as before.
/// admin / superadmin still pass unconditionally; other roles are still refused.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class AdminPermAttribute(string perm) : Attribute, IAuthorizationFilter
{
    /// <summary>Staff ruxsatlari shu turdagi claim sifatida principal'ga qo'shiladi.</summary>
    public const string ClaimType = "perm";

    private readonly string _perm = perm;

    /// <summary>
    /// <c>true</c> = a staff account needs the permission to READ as well as to
    /// write (GET/HEAD/OPTIONS are no longer waved through). Default <c>false</c>:
    /// the historical "any staff may read any admin section" behaviour.
    /// Use only where the response itself is sensitive — today the two
    /// question-bank controllers (§4.3).
    /// </summary>
    public bool GatedRead { get; set; }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) { context.Result = new UnauthorizedResult(); return; }

        // To'liq huquqli rollar — cheklovsiz.
        if (user.IsInRole(Roles.Admin) || user.IsInRole(Roles.SuperAdmin)) return;

        // Faqat xodim (staff) shu darvozadan o'tishi mumkin; qolganlari — rad.
        if (!user.IsInRole(Roles.Staff)) { context.Result = new ForbidResult(); return; }

        // O'qish har doim ochiq (bo'limlararo bog'liqliklar uchun); yozish — ruxsatga bog'liq.
        // Exception: a controller that opted in with `GatedRead = true` checks reads too.
        var method = context.HttpContext.Request.Method;
        var isRead = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
        if (isRead && !GatedRead) return;

        var has = user.Claims.Any(c => c.Type == ClaimType && c.Value == _perm);
        if (!has) context.Result = new ForbidResult();
    }
}
