using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Application.HrTasks;

// ===========================================================================
//  People are managed in Boshqaruv → Xodimlar, the HR list follows it (client, 2026-09-28:
//  "odamlar ro'yxatiga o'qituvchilarni ham tiqsin"; "xodim qo'shish va boshqarish Boshqaruv
//  menusi ichida allaqachon bor"). Without an HR row a teacher could neither receive a task
//  nor be placed in the subordination tree — the old HR only knew the 11 people imported.
//
//  Idempotent. It ADDS missing people and keeps name, position ("Lavozim" — client: "lavozimlar
//  ham bu qismda boshqarilmasin") and a missing phone in step with the LMS. It never
//  deactivates anyone (firing cancels open tasks — a human decision in HR) and never touches
//  managers or HR role. An empty LMS "Lavozim" leaves the HR position as it is. New rows are
//  plain employees with no manager: "Biriktirilmagan" in the tree until a director places them.
// ===========================================================================

/// <summary>What one sync pass changed.</summary>
public sealed record HrPeopleSyncResult(int Added, int Updated);

public sealed partial class HrTasksService
{
    /// <summary>HR position given to every synced teacher.</summary>
    public const string TeacherPositionName = "O'qituvchi";

    /// <summary>Fallback position when a staff member's LMS "Lavozim" is empty.</summary>
    public const string DefaultPositionName = "Xodim";

    /// <summary>
    /// Adds an HR row for each active teacher (non-archived <c>teachers</c> row with an account)
    /// and each staff/cashier/admin user that has none — superadmins are skipped, the built-in
    /// system account is one. For EVERY row it copies the LMS name and, when the row has no
    /// phone, the LMS phone.
    /// </summary>
    public async Task<HrPeopleSyncResult> SyncLmsPeopleAsync(CancellationToken ct = default)
    {
        string[] roles = [Roles.Teacher, Roles.Staff, Roles.Cashier, Roles.Admin];
        var known = db.HrEmployees.Select(e => e.UserId);
        var newcomers = await db.Users.AsNoTracking()
            .Where(u => roles.Contains(u.Role) && !known.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName, u.Role, u.Position, u.Phone })
            .ToListAsync(ct);
        var existing = await (from e in db.HrEmployees
                              join u in db.Users on e.UserId equals u.Id
                              select new { Employee = e, u.FullName, u.Phone, u.Position })
            .ToListAsync(ct);

        var userIds = newcomers.Select(u => u.Id).Concat(existing.Select(x => x.Employee.UserId)).ToList();
        var activeTeachers = (await db.Teachers.AsNoTracking()
                .Where(t => t.UserId != null && userIds.Contains(t.UserId) && !t.IsArchived)
                .Select(t => new { t.UserId, t.Phone })
                .ToListAsync(ct))
            .GroupBy(t => t.UserId!)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Phone).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)));
        var takenPhones = existing.Select(x => x.Employee.Phone).OfType<string>().ToHashSet();

        // An unreadable or already-used number is dropped — the phone is only a label here.
        string? FreePhone(string? userPhone, string userId)
        {
            var raw = string.IsNullOrWhiteSpace(userPhone) ? activeTeachers.GetValueOrDefault(userId) : userPhone;
            var phone = HrValues.NormalizePhone(raw);
            return phone is not null && takenPhones.Add(phone) ? phone : null;
        }

        var positions = await db.HrPositions.ToListAsync(ct);
        var now = Now;
        HrPosition PositionNamed(string name)
        {
            var found = positions.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (found is not null) return found;
            found = new HrPosition { Name = name, IsActive = true, CreatedAt = now, UpdatedAt = now };
            db.HrPositions.Add(found);
            positions.Add(found);
            return found;
        }

        var updated = 0;
        foreach (var x in existing)
        {
            var e = x.Employee;
            var changed = false;
            if (CleanSyncedName(x.FullName) is { } name && name != e.FullName)
            {
                e.FullName = name;
                changed = true;
            }
            if (e.Phone is null && FreePhone(x.Phone, e.UserId) is { } phone)
            {
                e.Phone = phone;
                changed = true;
            }
            if (LmsPositionName(activeTeachers.ContainsKey(e.UserId), x.Position) is { } positionName
                && PositionNamed(positionName).Id is var positionId && positionId != e.PositionId)
            {
                e.PositionId = positionId;
                changed = true;
            }
            if (!changed) continue;
            e.UpdatedAt = now;
            updated++;
        }

        var added = 0;
        foreach (var u in newcomers)
        {
            var isTeacher = activeTeachers.ContainsKey(u.Id);
            // A teacher account whose teacher card is archived has left — not an employee.
            if (u.Role == Roles.Teacher && !isTeacher) continue;
            if (CleanSyncedName(u.FullName) is not { } name) continue;

            var position = PositionNamed(LmsPositionName(isTeacher, u.Position) ?? DefaultPositionName);

            db.HrEmployees.Add(new HrEmployee
            {
                UserId = u.Id,
                HrRole = HrRoles.Employee,
                FullName = name,
                Phone = FreePhone(u.Phone, u.Id),
                PositionId = position.Id,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            added++;
        }

        if (added + updated > 0) await db.SaveChangesAsync(ct);
        return new HrPeopleSyncResult(added, updated);
    }

    /// <summary>
    /// The LMS "Lavozim" the Boshqaruv list shows: "O'qituvchi" for a teacher, else the user's
    /// own position; <c>null</c> when empty (the HR position is then left alone).
    /// </summary>
    private static string? LmsPositionName(bool isTeacher, string? userPosition)
    {
        if (isTeacher) return TeacherPositionName;
        var name = (userPosition ?? "").Trim();
        if (name.Length == 0) return null;
        return name.Length > HrLimits.PositionNameMax ? name[..HrLimits.PositionNameMax] : name;
    }

    /// <summary>LMS name as the HR row stores it; <c>null</c> when too short to use.</summary>
    private static string? CleanSyncedName(string? raw)
    {
        var name = (raw ?? "").Trim();
        if (name.Length < 2) return null;
        return name.Length > HrLimits.EmployeeNameMax ? name[..HrLimits.EmployeeNameMax] : name;
    }

    /// <summary>
    /// Deleting a teacher/staff ACCOUNT: an HR row with no history (no task, no recurring rule
    /// aimed at it) must not block it — since every employee has a row, it would block every
    /// delete. The row is staged for removal (manager links cascade) and <c>true</c> returned;
    /// with history <c>false</c> — the caller answers 409 and the row stays.
    /// </summary>
    public static async Task<bool> ReleaseUnusedEmployeeAsync(
        Abstractions.IAppDbContext db, string userId, CancellationToken ct = default)
    {
        var employee = await db.HrEmployees.FirstOrDefaultAsync(e => e.UserId == userId, ct);
        if (employee is null) return true;
        var id = employee.Id;
        if (await db.HrTasks.AnyAsync(t => t.EmployeeId == id, ct)) return false;
        if (await db.HrTaskTemplates.AnyAsync(t => t.TargetEmployeeId == id, ct)) return false;
        db.HrEmployees.Remove(employee);
        return true;
    }
}
