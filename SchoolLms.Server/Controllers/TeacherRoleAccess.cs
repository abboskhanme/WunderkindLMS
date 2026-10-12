using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// A teacher's access role (2026-10-12: teachers who are also dorm commandants). The role sits on the
/// teacher's account exactly as on a staff member's (<see cref="AppUser.AccessRoleId"/> and a copy of its
/// keys in <see cref="AppUser.Permissions"/>), but only <see cref="TeacherPermissions.RoleKeys"/> ever apply
/// to a teacher — the token pipeline (Program.cs) and the profile both filter by them.
/// </summary>
internal static class TeacherRoleAccess
{
    /// <summary>The role's name and the keys of it a teacher can use; (null, []) without a role.</summary>
    public static async Task<(string? Name, List<string> Permissions)> OfAsync(
        AppDbContext db, AppUser? user, CancellationToken ct = default)
    {
        if (user?.AccessRoleId is not { } roleId) return (null, []);
        var name = await db.AccessRoles.AsNoTracking()
            .Where(r => r.Id == roleId).Select(r => r.Name).FirstOrDefaultAsync(ct);
        return (name, user.Permissions.Where(TeacherPermissions.IsRoleKey).ToList());
    }
}
