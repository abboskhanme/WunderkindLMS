using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// "Faqat ko'rish" huquqining YOZISH tomoni (Boshqaruv → Rollar, 2026-09-26).
///
/// <para>
/// Oddiy bo'limlarda bu <see cref="AdminPermAttribute"/> ning o'zida: yozish to'liq
/// ruxsat kalitini (<c>students</c>) talab qiladi, <c>students:view</c> esa faqat o'qiydi.
/// Moliya esa rol darvozalari (<c>[Authorize(Roles = Roles.FinanceStaff)]</c>,
/// <see cref="Roles.CashierOrAdmin"/>) bilan yopilgan va "faqat ko'rish" xodimi u yerdan
/// <see cref="Roles.FinanceViewer"/> roli bilan O'QISH uchun o'tadi. Bu filtr o'sha
/// darvozalar ortidagi har qanday yozish so'rovini rad etadi — hatto amal
/// <see cref="FinanceRoleAttribute"/> bilan belgilanmagan bo'lsa ham (fail-closed).
/// </para>
/// </summary>
public sealed class ViewOnlyWriteGuard : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) return;
        if (!user.IsInRole(Roles.FinanceViewer) || user.IsInRole(Roles.FinanceDelegate)) return;

        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)) return;

        var gatedByFinanceRole = context.ActionDescriptor.EndpointMetadata
            .OfType<AuthorizeAttribute>()
            .Any(a => (a.Roles ?? "").Split(',', StringSplitOptions.TrimEntries).Contains(Roles.FinanceViewer));

        if (gatedByFinanceRole) context.Result = new ForbidResult();
    }
}
