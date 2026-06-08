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
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class AdminPermAttribute(string perm) : Attribute, IAuthorizationFilter
{
    /// <summary>Staff ruxsatlari shu turdagi claim sifatida principal'ga qo'shiladi.</summary>
    public const string ClaimType = "perm";

    private readonly string _perm = perm;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) { context.Result = new UnauthorizedResult(); return; }

        // To'liq huquqli rollar — cheklovsiz.
        if (user.IsInRole(Roles.Admin) || user.IsInRole(Roles.SuperAdmin)) return;

        // Faqat xodim (staff) shu darvozadan o'tishi mumkin; qolganlari — rad.
        if (!user.IsInRole(Roles.Staff)) { context.Result = new ForbidResult(); return; }

        // O'qish har doim ochiq (bo'limlararo bog'liqliklar uchun); yozish — ruxsatga bog'liq.
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)) return;

        var has = user.Claims.Any(c => c.Type == ClaimType && c.Value == _perm);
        if (!has) context.Result = new ForbidResult();
    }
}
