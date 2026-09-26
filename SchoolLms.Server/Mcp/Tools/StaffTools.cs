using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using SchoolLms.Domain;

namespace SchoolLms.Server.Mcp.Tools;

/// <summary>Employees (teachers + staff). Sections: <c>staff</c> or <c>teachers</c>; salary only with finance.</summary>
[McpServerToolType]
public sealed class StaffTools(McpToolContext t)
{
    [McpServerTool(Name = "employees_list", Title = "Xodimlar ro'yxati",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'qituvchilar va boshqa xodimlar: ism, lavozim yoki fanlar, sinf rahbarligi, rol, telefon, toifa. "
        + "Maosh faqat moliya ruxsati bo'lsa chiqadi. Hech qanday login/parol berilmaydi. Employees (teachers and staff).")]
    public async Task<string> EmployeesList(
        [Description("teachers | staff | all (sukut all)")] string kind = "all",
        [Description("Ism bo'lagi (ixtiyoriy)")] string? search = null,
        [Description("Arxivdagi o'qituvchilar ham")] bool includeArchived = false,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Employees);
        var db = t.Db;
        var finance = t.CanReadFinance;
        var needle = search?.Trim().ToLower();

        object? teachers = null, staff = null;
        var count = 0;
        if (kind is "all" or "teachers")
        {
            var subjects = await db.Subjects.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            var list = await db.Teachers.Where(x => includeArchived || !x.IsArchived)
                .Where(x => needle == null || x.FullName.ToLower().Contains(needle))
                .OrderBy(x => x.FullName).ToListAsync(ct);
            teachers = list.Select(x => new
            {
                x.Id, x.FullName, x.Phone, x.Gender, homeroomClass = x.HomeroomClass == "" ? null : x.HomeroomClass,
                subjects = x.SubjectIds.Select(id => subjects.GetValueOrDefault(id, id)).ToList(),
                category = x.Category == "" ? null : x.Category, x.IsArchived,
                salary = finance ? x.Salary : (decimal?)null, bonusPercent = finance ? x.BonusPct : (decimal?)null,
            }).ToList();
            count += list.Count;
        }
        if (kind is "all" or "staff")
        {
            var roles = await db.AccessRoles.Select(r => new { r.Id, r.Name }).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
            var list = await db.Users
                .Where(u => u.Role == Roles.Staff || u.Role == Roles.Admin || u.Role == Roles.SuperAdmin || u.Role == Roles.Cashier)
                .Where(u => needle == null || u.FullName.ToLower().Contains(needle))
                .OrderBy(u => u.FullName)
                .Select(u => new { u.Id, u.FullName, u.Role, u.Position, u.Phone, u.AccessRoleId, u.Salary, u.LastLoginAt })
                .ToListAsync(ct);
            staff = list.Select(u => new
            {
                u.Id, u.FullName, systemRole = u.Role, position = u.Position == "" ? null : u.Position, u.Phone,
                accessRole = u.AccessRoleId is { } rid ? roles.GetValueOrDefault(rid) : null,
                lastSeen = u.LastLoginAt,
                salary = finance && u.Role == Roles.Staff ? u.Salary : (decimal?)null,
            }).ToList();
            count += list.Count;
        }
        return t.Json(new { teachers, staff, note = finance ? null : "Maosh ko'rsatilmadi: moliya ruxsati yo'q." }, count);
    }
}
