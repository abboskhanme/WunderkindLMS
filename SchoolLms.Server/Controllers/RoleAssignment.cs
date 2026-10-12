using System.Security.Claims;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Who may give an access role (Boshqaruv → Rollar) to a staff member or a teacher (client,
/// 2026-10-12: "rolelarga dostupi bor odam ham role biriktira olsin").
///
/// <para>
/// The superadmin and admin always. A staff member when they hold the Rollar page at the full level:
/// the page grant <c>/admin/boshqaruv/roles|edit</c>, or a legacy role (section keys only) with the full
/// <c>staff</c> key — the same reading as the web panel (lib/access.ts <c>pageLevel</c>).
/// </para>
/// <para>
/// A staff member never hands out more than they hold (<see cref="Within"/>): every section key of the
/// new role and of the member's current access must be one the caller has at that level, or a role
/// assigner could give a colleague — or a second account — finance or anything else they lack. Their own
/// role they cannot change at all. Creating and editing roles stays with the superadmin.
/// </para>
/// </summary>
internal static class RoleAssignment
{
    public const string RolesPage = "/admin/boshqaruv/roles";

    public const string NotAllowedMessage =
        "Rolni tizim egasi yoki Rollar bo'limiga to'liq ruxsati bor xodim biriktiradi.";

    public const string OwnRoleMessage = "O'z rolingizni o'zgartira olmaysiz.";

    public const string WiderMessage =
        "Bu rol (yoki xodimning hozirgi huquqi) sizning huquqingizdan keng — uni tizim egasi biriktiradi.";

    private static bool IsFullAccess(ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) || user.IsInRole(Roles.SuperAdmin);

    /// <summary>May this user assign roles at all.</summary>
    public static bool CanAssign(ClaimsPrincipal user)
    {
        if (IsFullAccess(user)) return true;
        if (!user.IsInRole(Roles.Staff) || !user.HasPerm("staff")) return false;
        var pages = user.Claims
            .Where(c => c.Type == AdminPermAttribute.ClaimType && c.Value.StartsWith('/'))
            .Select(c => c.Value)
            .ToList();
        return pages.Count == 0 || pages.Contains(RolesPage + "|edit");
    }

    /// <summary>The caller's own account — changing it is refused to everyone but the full-access roles.</summary>
    public static bool IsSelf(ClaimsPrincipal user, string userId) =>
        !IsFullAccess(user) && user.FindFirst(ClaimTypes.NameIdentifier)?.Value == userId;

    /// <summary>
    /// Every section key of <paramref name="permissions"/> is one the caller holds at that level
    /// (a ":view" key — full or view; a full key — full). Page grants give no server access and are skipped.
    /// </summary>
    public static bool Within(ClaimsPrincipal user, IEnumerable<string> permissions)
    {
        if (IsFullAccess(user)) return true;
        foreach (var key in permissions)
        {
            if (key.StartsWith('/')) continue;
            var held = key.EndsWith(Roles.ViewSuffix, StringComparison.Ordinal)
                ? user.HasReadPerm(key[..^Roles.ViewSuffix.Length])
                : user.HasPerm(key);
            if (!held) return false;
        }
        return true;
    }
}
