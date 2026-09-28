using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Application.HrTasks;

/// <summary>A new <c>staff</c> account for an employee given by name + phone (the controller owns AccountFactory).</summary>
/// <returns>The tracked, NOT yet saved user and its one-time plain password.</returns>
public delegate (AppUser User, string Password) HrNewUserFactory(string fullName, string login, string displayPhone);

public sealed partial class HrTasksService
{
    // =====================================================================
    //  Positions (reads: any panel user; writes: directors — old hr_position:manage)
    // =====================================================================

    public async Task<HrPage<HrPositionDto>> PositionsAsync(string? q, bool? isActive, HrPaging paging, CancellationToken ct = default)
    {
        var query = db.HrPositions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term));
        }
        if (isActive is { } active) query = query.Where(p => p.IsActive == active);
        var (rows, hasMore, next, total) = await PageAsync(query.OrderBy(p => p.Name).ThenBy(p => p.Id), query, paging, ct);
        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.HrEmployees.AsNoTracking()
            .Where(e => e.PositionId != null && ids.Contains(e.PositionId))
            .GroupBy(e => e.PositionId!).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        return Page<HrPositionDto>(
            [.. rows.Select(p => ToDto(p, counts.GetValueOrDefault(p.Id)))], hasMore, next, total);
    }

    private static HrPositionDto ToDto(HrPosition p, int? employees = null) =>
        new(p.Id, p.Name, p.IsActive, p.CreatedAt, p.UpdatedAt, employees);

    public async Task<HrPositionDto> CreatePositionAsync(HrPanel panel, HrPositionCreateInput input, CancellationToken ct = default)
    {
        EnsureDirector(panel);
        var name = CleanPositionName(input.Name);
        if (await db.HrPositions.AnyAsync(p => p.Name == name, ct)) throw HrErrors.PositionNameTaken();
        var now = Now;
        var position = new HrPosition { Name = name, CreatedAt = now, UpdatedAt = now };
        db.HrPositions.Add(position);
        await db.SaveChangesAsync(ct);
        return ToDto(position, 0);
    }

    private static string CleanPositionName(string? raw)
    {
        var name = HrValues.CleanName(raw, "Lavozim nomi", HrLimits.PositionNameMax);
        if (name.Length < 2) throw HrErrors.Validation("Lavozim nomi kamida 2 belgi bo'lishi kerak");
        return name;
    }

    /// <summary>Only the fields present change; <c>isActive: true</c> reactivates.</summary>
    public async Task<HrPositionDto> UpdatePositionAsync(HrPanel panel, string id, JsonElement body, CancellationToken ct = default)
    {
        EnsureDirector(panel);
        var position = await db.HrPositions.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw HrErrors.PositionNotFound();
        var changed = false;
        if (Str(body, "name") is { } rawName)
        {
            var name = CleanPositionName(rawName);
            if (name != position.Name)
            {
                if (await db.HrPositions.AnyAsync(p => p.Name == name && p.Id != id, ct)) throw HrErrors.PositionNameTaken();
                position.Name = name;
                changed = true;
            }
        }
        if (Bool(body, "isActive") is { } active && active != position.IsActive) { position.IsActive = active; changed = true; }
        if (changed)
        {
            position.UpdatedAt = Now;
            await db.SaveChangesAsync(ct);
        }
        return ToDto(position);
    }

    /// <summary>No delete: a position with employees or templates answers 409 (move them first).</summary>
    public async Task<HrPositionDto> DeactivatePositionAsync(HrPanel panel, string id, CancellationToken ct = default)
    {
        EnsureDirector(panel);
        var position = await db.HrPositions.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw HrErrors.PositionNotFound();
        var employees = await db.HrEmployees.CountAsync(e => e.PositionId == id, ct);
        var templates = await db.HrTaskTemplates.CountAsync(t => t.TargetPositionId == id, ct);
        if (employees > 0 || templates > 0) throw HrErrors.PositionInUse();
        if (position.IsActive)
        {
            position.IsActive = false;
            position.UpdatedAt = Now;
            await db.SaveChangesAsync(ct);
        }
        return ToDto(position, 0);
    }

    // =====================================================================
    //  Employees
    // =====================================================================

    private IQueryable<HrEmployee> EmployeeQuery(HrEmployeeFilter filter, HrScope scope)
    {
        var query = db.HrEmployees.AsNoTracking().AsQueryable();
        if (scope.IsLimited)
        {
            var ids = scope.IdList;
            query = query.Where(e => ids.Contains(e.Id));
        }
        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var term = filter.Q.Trim().ToLower();
            var digits = new string(filter.Q.Where(char.IsDigit).ToArray());
            query = digits.Length > 0
                ? query.Where(e => e.FullName.ToLower().Contains(term) || (e.Phone != null && e.Phone.Contains(digits)))
                : query.Where(e => e.FullName.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(filter.PositionId)) query = query.Where(e => e.PositionId == filter.PositionId);
        if (!string.IsNullOrWhiteSpace(filter.Branch)) query = query.Where(e => e.Branch == filter.Branch);
        if (filter.IsActive is { } active) query = query.Where(e => e.IsActive == active);
        if (filter.Linked is { } linked)
            query = linked
                ? query.Where(e => db.TelegramAccounts.Any(a => a.UserId == e.UserId))
                : query.Where(e => !db.TelegramAccounts.Any(a => a.UserId == e.UserId));
        return query;
    }

    private static readonly string[] EmployeeSorts = ["full_name", "created_at"];

    private static IOrderedQueryable<HrEmployee> SortEmployees(IQueryable<HrEmployee> query, string? sort)
    {
        var (field, desc) = SortOf(sort, EmployeeSorts, "full_name");
        return (field, desc) switch
        {
            ("created_at", false) => query.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id),
            ("created_at", true) => query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id),
            (_, true) => query.OrderByDescending(e => e.FullName).ThenByDescending(e => e.Id),
            _ => query.OrderBy(e => e.FullName).ThenBy(e => e.Id),
        };
    }

    public async Task<HrPage<HrEmployeeDto>> EmployeesAsync(HrPanel panel, HrEmployeeFilter filter, HrPaging paging, CancellationToken ct = default)
    {
        var query = EmployeeQuery(filter, panel.Scope);
        var (rows, hasMore, next, total) = await PageAsync(SortEmployees(query, filter.Sort), query, paging, ct);
        return Page(await EmployeeDtosAsync(rows, ct), hasMore, next, total);
    }

    public async Task<HrEmployeeDto> EmployeeAsync(HrPanel panel, string id, CancellationToken ct = default)
    {
        var employee = await ScopedEmployeeAsync(panel.Scope, id, ct);
        return (await EmployeeDtosAsync([employee], ct))[0];
    }

    /// <summary>Loads an employee in scope — outside the scope is 404 (its existence is not revealed).</summary>
    private async Task<HrEmployee> ScopedEmployeeAsync(HrScope scope, string id, CancellationToken ct)
    {
        var employee = await db.HrEmployees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null || !scope.Allows(employee.Id)) throw HrErrors.EmployeeNotFound();
        return employee;
    }

    /// <summary>The card: + role, panel role, Telegram, position, managers, subordinates, open tasks.</summary>
    private async Task<List<HrEmployeeDto>> EmployeeDtosAsync(IReadOnlyList<HrEmployee> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var ids = rows.Select(r => r.Id).ToList();
        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var positionIds = rows.Where(r => r.PositionId != null).Select(r => r.PositionId!).Distinct().ToList();

        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Role, u.Permissions }).ToDictionaryAsync(u => u.Id, ct);
        var positions = await db.HrPositions.AsNoTracking().Where(p => positionIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var accounts = (await db.TelegramAccounts.AsNoTracking().Where(a => userIds.Contains(a.UserId))
                .Select(a => new { a.UserId, a.TelegramUserId, a.LinkedAt }).ToListAsync(ct))
            .GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.OrderBy(a => a.LinkedAt).First());
        var managers = (await (from m in db.HrEmployeeManagers.AsNoTracking()
                               join e in db.HrEmployees.AsNoTracking() on m.ManagerId equals e.Id
                               where ids.Contains(m.EmployeeId)
                               select new { m.EmployeeId, m.ManagerId, e.FullName }).ToListAsync(ct))
            .GroupBy(m => m.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.FullName, StringComparer.Ordinal).ThenBy(m => m.ManagerId, StringComparer.Ordinal).ToList());
        var subordinates = await db.HrEmployeeManagers.AsNoTracking().Where(m => ids.Contains(m.ManagerId))
            .GroupBy(m => m.ManagerId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var open = await db.HrTasks.AsNoTracking()
            .Where(t => ids.Contains(t.EmployeeId) && (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned))
            .GroupBy(t => t.EmployeeId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        return [.. rows.Select(e =>
        {
            var user = users.GetValueOrDefault(e.UserId);
            var account = accounts.GetValueOrDefault(e.UserId);
            var mine = managers.GetValueOrDefault(e.Id) ?? [];
            return new HrEmployeeDto
            {
                Id = e.Id,
                UserId = e.UserId,
                Role = user?.Role ?? "",
                HrRole = e.HrRole,
                FullName = e.FullName,
                Phone = e.Phone ?? "",
                PositionId = e.PositionId,
                Branch = e.Branch,
                TelegramUserId = account?.TelegramUserId,
                InviteCode = null,
                LinkedAt = LinkedAt(account?.LinkedAt),
                IsActive = e.IsActive,
                Note = e.Note,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt,
                PositionName = e.PositionId is null ? null : positions.GetValueOrDefault(e.PositionId),
                OpenTasksCount = open.GetValueOrDefault(e.Id),
                PanelRole = PanelRole(user?.Role, user?.Permissions, e.HrRole),
                ManagerIds = [.. mine.Select(m => m.ManagerId)],
                ManagerNames = [.. mine.Select(m => m.FullName)],
                SubordinateCount = subordinates.GetValueOrDefault(e.Id),
            };
        })];
    }

    /// <summary>
    /// hr-tasks.md §3: superadmin/admin from our role; "admin" for hr_role = director; "manager"
    /// for staff with <c>hrTasks</c>; otherwise null. Panel access is granted with the permission key.
    /// </summary>
    public static string? PanelRole(string? role, IReadOnlyCollection<string>? permissions, string hrRole)
    {
        if (role == Roles.SuperAdmin) return "superadmin";
        if (role == Roles.Admin) return "admin";
        if (hrRole == HrRoles.Director) return "admin";
        if (role == Roles.Staff && permissions is not null
            && (permissions.Contains(HrTaskAccess.PermissionKey) || permissions.Contains(HrTaskAccess.PermissionKey + Roles.ViewSuffix)))
            return "manager";
        return null;
    }

    /// <summary><c>hrRole</c> wins; the legacy <c>panelRole</c> maps admin/superadmin → director, manager → manager.</summary>
    private static string? DesiredHrRole(string? hrRole, bool hrRoleGiven, string? panelRole, bool panelRoleGiven)
    {
        if (hrRoleGiven)
        {
            if (hrRole is null) return HrRoles.Employee;
            if (!HrRoles.IsValid(hrRole)) throw HrErrors.Validation($"hrRole noto'g'ri: '{hrRole}'");
            return hrRole;
        }
        if (!panelRoleGiven) return null;
        return panelRole switch
        {
            null or "" => HrRoles.Employee,
            "admin" or "superadmin" => HrRoles.Director,
            "manager" => HrRoles.Manager,
            _ => HrRoles.Employee, // unknown value = "Yo'q" (old parse_panel_role)
        };
    }

    private static string? CleanBranch(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch)) return null;
        if (!HrBranches.IsValid(branch)) throw HrErrors.Validation($"Bo'linma noto'g'ri: '{branch}'");
        return branch;
    }

    /// <summary>
    /// Pick an existing user (<c>userId</c>) or give name + phone — then a <c>staff</c> user is created
    /// (login = phone digits, one-time password returned). A manager (limited scope) becomes the new
    /// employee's manager automatically (old ASSUMPTIONS 2026-09-09).
    /// </summary>
    public async Task<HrEmployeeCreateResultDto> CreateEmployeeAsync(
        HrPanel panel, HrEmployeeCreateInput input, HrNewUserFactory newUser, CancellationToken ct = default)
    {
        var desired = DesiredHrRole(input.HrRole, input.HrRole is not null, input.PanelRole, input.PanelRole is not null)
                      ?? HrRoles.Employee;
        if (desired != HrRoles.Employee && !panel.IsDirector) throw HrErrors.PanelAccessForbidden();

        AppUser? user = null;
        if (!string.IsNullOrWhiteSpace(input.UserId))
        {
            user = await db.Users.FirstOrDefaultAsync(u => u.Id == input.UserId, ct) ?? throw HrErrors.UserNotFound();
            if (!EmployeeUserRoles.Contains(user.Role))
                throw HrErrors.Validation("Bu foydalanuvchini xodim qilib bo'lmaydi (faqat xodim, o'qituvchi, admin yoki kassir)");
            if (await db.HrEmployees.AnyAsync(e => e.UserId == user.Id, ct)) throw HrErrors.UserAlreadyEmployee();
        }

        var fullName = HrValues.CleanName(
            string.IsNullOrWhiteSpace(input.FullName) ? user?.FullName : input.FullName, "Xodim ismi", HrLimits.EmployeeNameMax);
        if (fullName.Length < 2) throw HrErrors.Validation("Xodim ismi kamida 2 belgi bo'lishi kerak");

        var rawPhone = input.Phone;
        if (string.IsNullOrWhiteSpace(rawPhone) && user is not null)
        {
            var uid = user.Id;
            rawPhone = !string.IsNullOrWhiteSpace(user.Phone)
                ? user.Phone
                : await db.Teachers.AsNoTracking().Where(t => t.UserId == uid).Select(t => t.Phone).FirstOrDefaultAsync(ct);
        }
        // An existing user may have no phone (many teachers do) — the HR row then has none. A NEW
        // user needs one: it becomes the login.
        string? phone = null;
        if (!string.IsNullOrWhiteSpace(rawPhone))
        {
            phone = HrValues.RequirePhone(rawPhone);
            if (await db.HrEmployees.AnyAsync(e => e.Phone == phone, ct)) throw HrErrors.PhoneAlreadyUsed();
        }
        else if (user is null) throw HrErrors.Validation("Telefon raqami kiritilmagan");

        if (!string.IsNullOrWhiteSpace(input.PositionId) && !await db.HrPositions.AnyAsync(p => p.Id == input.PositionId, ct))
            throw HrErrors.PositionNotFound();
        var branch = CleanBranch(input.Branch);
        var note = HrValues.CleanText(input.Note, "Izoh", HrLimits.NoteMax);

        HrCredentialsDto? credentials = null;
        if (user is null)
        {
            var login = phone!.TrimStart('+');
            if (await db.Users.AnyAsync(u => u.Email == login, ct)) throw HrErrors.LoginTaken();
            var (created, password) = newUser(fullName, login, HrValues.DisplayPhone(phone));
            user = created;
            credentials = new HrCredentialsDto(login, password);
        }

        var now = Now;
        var employee = new HrEmployee
        {
            UserId = user.Id,
            HrRole = desired,
            FullName = fullName,
            Phone = phone,
            PositionId = string.IsNullOrWhiteSpace(input.PositionId) ? null : input.PositionId,
            Branch = branch,
            Note = note,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.HrEmployees.Add(employee);

        // A manager's new employee lands under the manager — otherwise it vanishes from their scope.
        var managerIds = input.ManagerIds;
        if (panel.Scope.IsLimited && panel.Self is not null)
            managerIds = [.. (managerIds ?? []).Append(panel.Self.Id).Distinct()];
        if (managerIds is not null)
            await StageManagersAsync(employee.Id, managerIds, panel.Scope.Including(employee.Id), isNew: true, ct);

        await db.SaveChangesAsync(ct);
        var dto = (await EmployeeDtosAsync([employee], ct))[0];
        return new HrEmployeeCreateResultDto
        {
            Id = dto.Id, UserId = dto.UserId, Role = dto.Role, HrRole = dto.HrRole, FullName = dto.FullName,
            Phone = dto.Phone, PositionId = dto.PositionId, Branch = dto.Branch, TelegramUserId = dto.TelegramUserId,
            InviteCode = dto.InviteCode, LinkedAt = dto.LinkedAt, IsActive = dto.IsActive, Note = dto.Note,
            CreatedAt = dto.CreatedAt, UpdatedAt = dto.UpdatedAt, PositionName = dto.PositionName,
            OpenTasksCount = dto.OpenTasksCount, PanelRole = dto.PanelRole, ManagerIds = dto.ManagerIds,
            ManagerNames = dto.ManagerNames, SubordinateCount = dto.SubordinateCount, Credentials = credentials,
        };
    }

    /// <summary>
    /// PATCH: absent field = untouched; <c>null</c> clears (positionId, note, branch). Firing
    /// (<c>isActive: false</c>) cancels open tasks. <c>managerIds</c> replaces the whole list.
    /// </summary>
    public async Task<HrEmployeeDto> UpdateEmployeeAsync(HrPanel panel, string id, JsonElement body, CancellationToken ct = default)
    {
        var employee = await ScopedEmployeeAsync(panel.Scope, id, ct);
        var now = Now;
        var changed = false;

        var desired = DesiredHrRole(Str(body, "hrRole"), Has(body, "hrRole"), Str(body, "panelRole"), Has(body, "panelRole"));
        if (desired is not null && desired != employee.HrRole)
        {
            if (!panel.IsDirector) throw HrErrors.PanelAccessForbidden();
            employee.HrRole = desired;
            changed = true;
        }
        if (Has(body, "fullName") && Str(body, "fullName") is { } rawName)
        {
            var name = HrValues.CleanName(rawName, "Xodim ismi", HrLimits.EmployeeNameMax);
            if (name.Length < 2) throw HrErrors.Validation("Xodim ismi kamida 2 belgi bo'lishi kerak");
            if (name != employee.FullName) { employee.FullName = name; changed = true; }
        }
        if (Has(body, "phone") && Str(body, "phone") is { } rawPhone)
        {
            var phone = HrValues.RequirePhone(rawPhone);
            if (phone != employee.Phone)
            {
                if (await db.HrEmployees.AnyAsync(e => e.Phone == phone && e.Id != employee.Id, ct)) throw HrErrors.PhoneAlreadyUsed();
                employee.Phone = phone;
                changed = true;
            }
        }
        if (Has(body, "positionId"))
        {
            var positionId = Str(body, "positionId");
            if (!string.IsNullOrWhiteSpace(positionId) && !await db.HrPositions.AnyAsync(p => p.Id == positionId, ct))
                throw HrErrors.PositionNotFound();
            positionId = string.IsNullOrWhiteSpace(positionId) ? null : positionId;
            if (positionId != employee.PositionId) { employee.PositionId = positionId; changed = true; }
        }
        if (Has(body, "branch"))
        {
            var branch = CleanBranch(Str(body, "branch"));
            if (branch != employee.Branch) { employee.Branch = branch; changed = true; }
        }
        if (Has(body, "note"))
        {
            var note = HrValues.CleanText(Str(body, "note"), "Izoh", HrLimits.NoteMax);
            if (note != employee.Note) { employee.Note = note; changed = true; }
        }
        if (Bool(body, "isActive") is { } active && active != employee.IsActive)
        {
            employee.IsActive = active;
            changed = true;
            if (!active) await CancelOpenTasksAsync(employee.Id, now, ct);
        }
        if (Has(body, "managerIds"))
        {
            await StageManagersAsync(employee.Id, Strings(body, "managerIds") ?? [], panel.Scope, isNew: false, ct);
            changed = true;
        }
        if (changed)
        {
            employee.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
        return (await EmployeeDtosAsync([employee], ct))[0];
    }

    /// <summary><c>PUT /employees/{id}/managers</c> — the list is REPLACED; [] = no managers.</summary>
    public async Task<HrEmployeeDto> SetManagersAsync(HrPanel panel, string id, IReadOnlyList<string> managerIds, CancellationToken ct = default)
    {
        var employee = await ScopedEmployeeAsync(panel.Scope, id, ct);
        await StageManagersAsync(employee.Id, managerIds, panel.Scope, isNew: false, ct);
        employee.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return (await EmployeeDtosAsync([employee], ct))[0];
    }

    /// <summary>
    /// Validates and stages the replacement of the manager edges (saved by the caller):
    /// self → 409 cycle; unknown or out-of-scope manager → 404 <c>hr_manager_not_found</c>;
    /// a manager already BELOW the employee → 409 <c>hr_manager_cycle</c>.
    /// </summary>
    private async Task StageManagersAsync(string employeeId, IEnumerable<string> managerIds, HrScope scope, bool isNew, CancellationToken ct)
    {
        var wanted = managerIds.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().OrderBy(m => m, StringComparer.Ordinal).ToList();
        if (wanted.Count > HrLimits.MaxManagers) throw HrErrors.Validation($"Rahbarlar {HrLimits.MaxManagers} tadan oshmasligi kerak");
        if (wanted.Contains(employeeId)) throw HrErrors.ManagerCycle();
        var existing = await db.HrEmployees.AsNoTracking().Where(e => wanted.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct);
        foreach (var managerId in wanted)
            if (!existing.Contains(managerId) || !scope.Allows(managerId)) throw HrErrors.ManagerNotFound();

        if (!isNew)
        {
            var below = await SubtreeAsync(employeeId, ct);
            if (wanted.Any(below.Contains)) throw HrErrors.ManagerCycle();
            var current = await db.HrEmployeeManagers.Where(m => m.EmployeeId == employeeId).ToListAsync(ct);
            foreach (var edge in current.Where(c => !wanted.Contains(c.ManagerId))) db.HrEmployeeManagers.Remove(edge);
            wanted = [.. wanted.Where(w => current.All(c => c.ManagerId != w))];
        }
        var now = Now;
        foreach (var managerId in wanted)
            db.HrEmployeeManagers.Add(new HrEmployeeManager { EmployeeId = employeeId, ManagerId = managerId, CreatedAt = now });
    }

    /// <summary>Firing: the HR row goes inactive, open tasks are cancelled. The user account is NOT touched.</summary>
    public async Task<HrEmployeeDto> DeactivateEmployeeAsync(HrPanel panel, string id, CancellationToken ct = default)
    {
        var employee = await ScopedEmployeeAsync(panel.Scope, id, ct);
        var now = Now;
        var changed = employee.IsActive;
        employee.IsActive = false;
        var cancelled = await CancelOpenTasksAsync(employee.Id, now, ct);
        if (changed || cancelled > 0)
        {
            employee.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
        return (await EmployeeDtosAsync([employee], ct))[0];
    }

    private async Task<int> CancelOpenTasksAsync(string employeeId, DateTimeOffset now, CancellationToken ct)
    {
        var open = await db.HrTasks
            .Where(t => t.EmployeeId == employeeId && (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned))
            .ToListAsync(ct);
        return open.Count(t => t.Cancel(now));
    }

    /// <summary>
    /// "Invite" = our one-time Telegram link code (<see cref="TelegramLinkService"/>): the employee enters
    /// it in the Mini App. The code is shown ONCE (stored hashed).
    /// </summary>
    public async Task<HrInviteDto> InviteAsync(HrPanel panel, string id, CancellationToken ct = default)
    {
        var employee = await ScopedEmployeeAsync(panel.Scope, id, ct);
        if (!employee.IsActive) throw HrErrors.EmployeeInactive();
        if (await db.TelegramAccounts.AnyAsync(a => a.UserId == employee.UserId, ct)) throw HrErrors.UserAlreadyLinked();
        var issued = await new TelegramLinkService(db).IssueAsync(employee.UserId, panel.Actor.UserId, ct);
        var bot = telegram.BotUsername;
        return new HrInviteDto(employee.Id, issued.Code, string.IsNullOrWhiteSpace(bot) ? null : $"https://t.me/{bot.TrimStart('@')}");
    }

    /// <summary>
    /// <c>{roots, unassigned}</c>: roots have subordinates and no manager; unassigned have neither.
    /// An employee with two managers appears under both. Not paged — at most 200 rows.
    /// </summary>
    public async Task<HrEmployeeTreeDto> TreeAsync(HrPanel panel, HrEmployeeFilter filter, CancellationToken ct = default)
    {
        var rows = await SortEmployees(EmployeeQuery(filter, panel.Scope), null).Take(HrLimits.MaxLimit).ToListAsync(ct);
        var cards = await EmployeeDtosAsync(rows, ct);
        return BuildTree(cards, await EdgesAsync(ct));
    }

    public static HrEmployeeTreeDto BuildTree(IReadOnlyList<HrEmployeeDto> employees, IEnumerable<(string EmployeeId, string ManagerId)> edges)
    {
        var byId = employees.ToDictionary(e => e.Id);
        var order = employees.Select((e, i) => (e.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var children = new Dictionary<string, List<string>>();
        var hasManager = new HashSet<string>();
        foreach (var (employeeId, managerId) in edges)
        {
            if (!byId.ContainsKey(employeeId) || !byId.ContainsKey(managerId)) continue;
            if (!children.TryGetValue(managerId, out var list)) children[managerId] = list = [];
            list.Add(employeeId);
            hasManager.Add(employeeId);
        }
        foreach (var list in children.Values) list.Sort((a, b) => order[a].CompareTo(order[b]));

        HrEmployeeNodeDto Node(string id, IReadOnlySet<string> path)
        {
            var e = byId[id];
            var next = new HashSet<string>(path) { id };
            var kids = children.TryGetValue(id, out var list)
                ? list.Where(k => !next.Contains(k)).Select(k => Node(k, next)).ToList()
                : [];
            return new HrEmployeeNodeDto
            {
                Id = e.Id, UserId = e.UserId, Role = e.Role, HrRole = e.HrRole, FullName = e.FullName, Phone = e.Phone,
                PositionId = e.PositionId, Branch = e.Branch, TelegramUserId = e.TelegramUserId, InviteCode = e.InviteCode,
                LinkedAt = e.LinkedAt, IsActive = e.IsActive, Note = e.Note, CreatedAt = e.CreatedAt, UpdatedAt = e.UpdatedAt,
                PositionName = e.PositionName, OpenTasksCount = e.OpenTasksCount, PanelRole = e.PanelRole,
                ManagerIds = e.ManagerIds, ManagerNames = e.ManagerNames, SubordinateCount = e.SubordinateCount,
                Children = kids,
            };
        }

        var roots = new List<HrEmployeeNodeDto>();
        var unassigned = new List<HrEmployeeNodeDto>();
        foreach (var e in employees)
        {
            if (hasManager.Contains(e.Id)) continue;
            var node = Node(e.Id, new HashSet<string>());
            (children.ContainsKey(e.Id) ? roots : unassigned).Add(node);
        }
        return new HrEmployeeTreeDto(roots, unassigned);
    }

    /// <summary>"Pick an existing user" candidates (staff, teacher, admin, superadmin, cashier).</summary>
    public async Task<List<HrUserCandidateDto>> UserCandidatesAsync(string? q, CancellationToken ct = default)
    {
        var roles = EmployeeUserRoles;
        var query = db.Users.AsNoTracking().Where(u => roles.Contains(u.Role));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(u => u.FullName.ToLower().Contains(term) || u.Email.ToLower().Contains(term) || u.Phone.Contains(term));
        }
        var users = await query.OrderBy(u => u.FullName).Take(HrLimits.DefaultLimit)
            .Select(u => new { u.Id, u.FullName, u.Phone, u.Role }).ToListAsync(ct);
        var ids = users.Select(u => u.Id).ToList();
        var employees = await db.HrEmployees.AsNoTracking().Where(e => ids.Contains(e.UserId))
            .ToDictionaryAsync(e => e.UserId, e => e.Id, ct);
        var teacherPhones = await db.Teachers.AsNoTracking().Where(t => t.UserId != null && ids.Contains(t.UserId))
            .Select(t => new { t.UserId, t.Phone }).ToListAsync(ct);
        return [.. users.Select(u => new HrUserCandidateDto(
            u.Id, u.FullName,
            !string.IsNullOrWhiteSpace(u.Phone) ? u.Phone : teacherPhones.FirstOrDefault(t => t.UserId == u.Id)?.Phone is { Length: > 0 } tp ? tp : null,
            u.Role, employees.GetValueOrDefault(u.Id)))];
    }

    // =====================================================================
    //  Templates (recurring rules). A limited scope sees only EMPLOYEE-targeted rules of its
    //  subtree — a position rule spans the whole organisation (old _scope.py).
    // =====================================================================

    private static readonly string[] TemplateSorts = ["title", "created_at"];

    public async Task<HrPage<HrTemplateDto>> TemplatesAsync(HrPanel panel, HrTemplateFilter filter, HrPaging paging, CancellationToken ct = default)
    {
        var query = db.HrTaskTemplates.AsNoTracking().AsQueryable();
        if (panel.Scope.IsLimited)
        {
            var ids = panel.Scope.IdList;
            query = query.Where(t => t.TargetEmployeeId != null && ids.Contains(t.TargetEmployeeId));
        }
        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var term = filter.Q.Trim().ToLower();
            query = query.Where(t => t.Title.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(filter.TargetType)) query = query.Where(t => t.TargetType == filter.TargetType);
        if (!string.IsNullOrWhiteSpace(filter.TargetEmployeeId)) query = query.Where(t => t.TargetEmployeeId == filter.TargetEmployeeId);
        if (!string.IsNullOrWhiteSpace(filter.TargetPositionId)) query = query.Where(t => t.TargetPositionId == filter.TargetPositionId);
        if (!string.IsNullOrWhiteSpace(filter.Recurrence)) query = query.Where(t => t.Recurrence == filter.Recurrence);
        if (!string.IsNullOrWhiteSpace(filter.Branch))
            query = query.Where(t => db.HrEmployees.Any(e => e.Id == t.TargetEmployeeId && e.Branch == filter.Branch));
        if (filter.IsActive is { } active) query = query.Where(t => t.IsActive == active);

        var (field, desc) = SortOf(filter.Sort, TemplateSorts, "title");
        var ordered = (field, desc) switch
        {
            ("created_at", false) => query.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id),
            ("created_at", true) => query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id),
            (_, true) => query.OrderByDescending(t => t.Title).ThenByDescending(t => t.Id),
            _ => query.OrderBy(t => t.Title).ThenBy(t => t.Id),
        };
        var (rows, hasMore, next, total) = await PageAsync(ordered, query, paging, ct);
        return Page(await TemplateDtosAsync(rows, ct), hasMore, next, total);
    }

    private async Task<List<HrTemplateDto>> TemplateDtosAsync(IReadOnlyList<HrTaskTemplate> rows, CancellationToken ct)
    {
        var employeeIds = rows.Where(r => r.TargetEmployeeId != null).Select(r => r.TargetEmployeeId!).Distinct().ToList();
        var positionIds = rows.Where(r => r.TargetPositionId != null).Select(r => r.TargetPositionId!).Distinct().ToList();
        var names = await db.HrEmployees.AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var positions = await db.HrPositions.AsNoTracking().Where(p => positionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return [.. rows.Select(t => ToDto(t,
            t.TargetEmployeeId is null ? null : names.GetValueOrDefault(t.TargetEmployeeId),
            t.TargetPositionId is null ? null : positions.GetValueOrDefault(t.TargetPositionId)))];
    }

    private static HrTemplateDto ToDto(HrTaskTemplate t, string? employeeName, string? positionName) => new(
        t.Id, t.Title, t.Description, t.TargetType, t.TargetEmployeeId, t.TargetPositionId, t.Recurrence,
        t.WeekdayList, t.DayOfMonthList, Hm(t.DueTime), t.Kind, t.TargetAmount, t.Unit, t.ProofRequired,
        t.OverduePolicy, t.IsActive, t.CreatedBy, t.CreatedAt, t.UpdatedAt, employeeName, positionName);

    /// <summary>Title/target/recurrence/kind invariants (old TaskTemplate.__post_init__).</summary>
    private static void ValidateTemplate(HrTaskTemplate t)
    {
        t.Title = HrValues.RequireText(t.Title, "Sarlavha", HrLimits.TitleMax);
        if (t.Title.Length < 2) throw HrErrors.Validation("Sarlavha kamida 2 belgi bo'lishi kerak");
        t.Description = HrValues.CleanText(t.Description, "Tavsif", HrLimits.DescriptionMax);
        if (!HrTargetTypes.IsValid(t.TargetType)) throw HrErrors.InvalidTemplate("Nishon turi noto'g'ri");
        if (t.TargetType == HrTargetTypes.Employee)
        {
            if (t.TargetEmployeeId is null) throw HrErrors.InvalidTemplate("Xodim tanlanmagan");
            if (t.TargetPositionId is not null) throw HrErrors.InvalidTemplate("Xodimga biriktirilgan shablonda lavozim bo'lmaydi");
        }
        else
        {
            if (t.TargetPositionId is null) throw HrErrors.InvalidTemplate("Lavozim tanlanmagan");
            if (t.TargetEmployeeId is not null) throw HrErrors.InvalidTemplate("Lavozimga biriktirilgan shablonda xodim bo'lmaydi");
        }
        var (week, month) = HrRecurrenceRules.Validate(t.Recurrence, t.WeekdayList, t.DayOfMonthList);
        t.Weekdays = [.. week.Select(x => (short)x)];
        t.DaysOfMonth = [.. month.Select(x => (short)x)];
        if (!HrTaskKinds.IsValid(t.Kind)) throw HrErrors.InvalidTemplate("Topshiriq turi noto'g'ri");
        (t.TargetAmount, t.Unit) = HrValues.KindAmount(t.Kind, t.TargetAmount, t.Unit);
    }

    private async Task EnsureTemplateTargetAsync(HrTaskTemplate t, HrScope scope, CancellationToken ct)
    {
        if (t.TargetType == HrTargetTypes.Employee && t.TargetEmployeeId is { } employeeId)
        {
            if (!await db.HrEmployees.AnyAsync(e => e.Id == employeeId, ct) || !scope.Allows(employeeId))
                throw HrErrors.EmployeeNotFound();
        }
        else if (t.TargetPositionId is { } positionId)
        {
            if (!await db.HrPositions.AnyAsync(p => p.Id == positionId, ct)) throw HrErrors.PositionNotFound();
            // a manager cannot write a rule for a whole position (outside the subtree)
            if (scope.IsLimited) throw HrErrors.PositionNotFound();
        }
    }

    private static void EnsureTemplateScope(HrTaskTemplate t, HrScope scope)
    {
        if (!scope.IsLimited) return;
        if (t.TargetType != HrTargetTypes.Employee || !scope.Allows(t.TargetEmployeeId)) throw HrErrors.TemplateNotFound();
    }

    public async Task<HrTemplateDto> CreateTemplateAsync(HrPanel panel, HrTemplateCreateInput input, CancellationToken ct = default)
    {
        var now = Now;
        var template = new HrTaskTemplate
        {
            Title = input.Title ?? "",
            Description = input.Description,
            TargetType = input.TargetType ?? "",
            TargetEmployeeId = string.IsNullOrWhiteSpace(input.TargetEmployeeId) ? null : input.TargetEmployeeId,
            TargetPositionId = string.IsNullOrWhiteSpace(input.TargetPositionId) ? null : input.TargetPositionId,
            Recurrence = input.Recurrence ?? "",
            Weekdays = [.. (input.Weekdays ?? []).Select(x => (short)Math.Clamp(x, short.MinValue, short.MaxValue))],
            DaysOfMonth = [.. (input.DaysOfMonth ?? []).Select(x => (short)Math.Clamp(x, short.MinValue, short.MaxValue))],
            DueTime = ParseTime(input.DueTime, "dueTime"),
            Kind = input.Kind ?? HrTaskKinds.Check,
            TargetAmount = input.TargetAmount,
            Unit = input.Unit,
            ProofRequired = input.ProofRequired ?? true,
            OverduePolicy = HrOverduePolicies.KeepOpen,
            IsActive = true,
            CreatedBy = panel.Actor.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ValidateTemplate(template);
        await EnsureTemplateTargetAsync(template, panel.Scope, ct);
        db.HrTaskTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        // today's instance right away (old: CreateTemplate → generator.execute(template_id))
        await GenerateSafelyAsync([template.Id], ct);
        return (await TemplateDtosAsync([template], ct))[0];
    }

    private static readonly string[] TemplateRebuilt = ["targetType", "targetEmployeeId", "targetPositionId", "kind", "targetAmount", "unit"];

    public async Task<HrTemplateDto> UpdateTemplateAsync(HrPanel panel, string id, JsonElement body, CancellationToken ct = default)
    {
        var template = await db.HrTaskTemplates.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw HrErrors.TemplateNotFound();
        EnsureTemplateScope(template, panel.Scope);
        var before = (template.TargetType, template.TargetEmployeeId, template.TargetPositionId, template.Kind, template.TargetAmount,
            template.Unit, template.Title, template.Description, template.DueTime, template.Recurrence,
            Week: string.Join(",", template.Weekdays), Month: string.Join(",", template.DaysOfMonth), template.ProofRequired, template.IsActive);

        if (Has(body, "targetType")) template.TargetType = Str(body, "targetType") ?? "";
        if (Has(body, "targetEmployeeId")) template.TargetEmployeeId = NullIfBlank(Str(body, "targetEmployeeId"));
        if (Has(body, "targetPositionId")) template.TargetPositionId = NullIfBlank(Str(body, "targetPositionId"));
        if (Has(body, "kind")) template.Kind = Str(body, "kind") ?? HrTaskKinds.Check;
        if (Has(body, "targetAmount")) template.TargetAmount = Dec(body, "targetAmount");
        if (Has(body, "unit")) template.Unit = Str(body, "unit");
        if (Has(body, "title") && Str(body, "title") is { } title) template.Title = title;
        if (Has(body, "description")) template.Description = Str(body, "description");
        if (Has(body, "dueTime")) template.DueTime = ParseTime(Str(body, "dueTime"), "dueTime");
        if (Has(body, "recurrence") && Str(body, "recurrence") is { } recurrence) template.Recurrence = recurrence;
        if (Ints(body, "weekdays") is { } weekdays) template.Weekdays = [.. weekdays.Select(x => (short)Math.Clamp(x, short.MinValue, short.MaxValue))];
        if (Ints(body, "daysOfMonth") is { } days) template.DaysOfMonth = [.. days.Select(x => (short)Math.Clamp(x, short.MinValue, short.MaxValue))];
        if (Bool(body, "proofRequired") is { } proof) template.ProofRequired = proof;
        if (Bool(body, "isActive") is { } active) template.IsActive = active;

        // switching to monthly with only daysOfMonth must not fail on the (now irrelevant) weekdays
        ValidateTemplate(template);

        var after = (template.TargetType, template.TargetEmployeeId, template.TargetPositionId, template.Kind, template.TargetAmount,
            template.Unit, template.Title, template.Description, template.DueTime, template.Recurrence,
            Week: string.Join(",", template.Weekdays), Month: string.Join(",", template.DaysOfMonth), template.ProofRequired, template.IsActive);
        if (after == before) return (await TemplateDtosAsync([template], ct))[0];

        if (TemplateRebuilt.Take(3).Any(f => Has(body, f))) await EnsureTemplateTargetAsync(template, panel.Scope, ct);
        var now = Now;
        template.UpdatedAt = now;
        if (template.DueTime != before.DueTime || template.Recurrence != before.Recurrence)
            await RescheduleOpenTasksAsync(template, now, ct);
        await db.SaveChangesAsync(ct);
        return (await TemplateDtosAsync([template], ct))[0];
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>A new due time moves the template's OPEN instances to it (their reminders restart).</summary>
    private async Task RescheduleOpenTasksAsync(HrTaskTemplate template, DateTimeOffset now, CancellationToken ct)
    {
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var open = await db.HrTasks
            .Where(t => t.TemplateId == template.Id && (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned))
            .ToListAsync(ct);
        foreach (var task in open)
        {
            var fresh = HrRecurrenceRules.DueAtFor(HrValues.LocalDate(task.DueAt, tz), template.DueTime, tz);
            if (fresh == task.DueAt) continue;
            if (task.UpdateDetails(now, null, false, null, fresh)) await ForgetRemindersAsync(task.Id, ct);
        }
    }

    public async Task<HrTemplateDto> ToggleTemplateAsync(HrPanel panel, string id, bool? isActive, CancellationToken ct = default)
    {
        var template = await db.HrTaskTemplates.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw HrErrors.TemplateNotFound();
        EnsureTemplateScope(template, panel.Scope);
        var target = isActive ?? !template.IsActive;
        if (target != template.IsActive)
        {
            template.IsActive = target;
            template.UpdatedAt = Now;
            await db.SaveChangesAsync(ct);
        }
        return (await TemplateDtosAsync([template], ct))[0];
    }

    /// <summary>Deletes the rule; its tasks stay with <c>template_id = null</c> (FK SET NULL, old §15.7).</summary>
    public async Task DeleteTemplateAsync(HrPanel panel, string id, CancellationToken ct = default)
    {
        var template = await db.HrTaskTemplates.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw HrErrors.TemplateNotFound();
        EnsureTemplateScope(template, panel.Scope);
        var tasks = await db.HrTasks.Where(t => t.TemplateId == id).ToListAsync(ct);
        foreach (var task in tasks) task.TemplateId = null;
        db.HrTaskTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
    }

    private static void EnsureBatchLimits(int employees, int items)
    {
        if (employees > HrLimits.MaxBatchEmployees) throw HrErrors.BatchTooLarge(HrLimits.MaxBatchEmployees, employees);
        if (items > HrLimits.MaxBatchItems) throw HrErrors.BatchTooLarge(HrLimits.MaxBatchItems, items);
        var total = employees * items;
        if (total > HrLimits.MaxBatchTotal) throw HrErrors.BatchTooLarge(HrLimits.MaxBatchTotal, total);
    }

    /// <summary>One EMPLOYEE-targeted template per employee × item. Inactive / out of scope → skipped.</summary>
    public async Task<HrBatchResultDto> CreateTemplateBatchAsync(HrPanel panel, HrTemplateBatchInput input, CancellationToken ct = default)
    {
        var employeeIds = (input.EmployeeIds ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var items = input.Items ?? [];
        EnsureBatchLimits(employeeIds.Count, items.Count);
        if (employeeIds.Count == 0 || items.Count == 0) return new HrBatchResultDto(0, [], null, []);

        var now = Now;
        var employees = await db.HrEmployees.AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var created = new List<HrTaskTemplate>();
        var skipped = new List<HrBatchSkippedDto>();
        foreach (var employeeId in employeeIds)
        {
            if (!employees.TryGetValue(employeeId, out var employee) || !panel.Scope.Allows(employeeId))
            { skipped.Add(new HrBatchSkippedDto(employeeId, "not_found")); continue; }
            if (!employee.IsActive) { skipped.Add(new HrBatchSkippedDto(employeeId, "inactive")); continue; }
            foreach (var item in items)
            {
                var template = new HrTaskTemplate
                {
                    Title = item.Title ?? "",
                    Description = item.Description,
                    TargetType = HrTargetTypes.Employee,
                    TargetEmployeeId = employee.Id,
                    Recurrence = item.Recurrence ?? "",
                    Weekdays = [.. (item.Weekdays ?? []).Select(x => (short)Math.Clamp(x, short.MinValue, short.MaxValue))],
                    DaysOfMonth = [.. (item.DaysOfMonth ?? []).Select(x => (short)Math.Clamp(x, short.MinValue, short.MaxValue))],
                    DueTime = ParseTime(item.DueTime, "dueTime"),
                    Kind = item.Kind ?? HrTaskKinds.Check,
                    TargetAmount = item.TargetAmount,
                    Unit = item.Unit,
                    ProofRequired = item.ProofRequired ?? true,
                    OverduePolicy = HrOverduePolicies.KeepOpen,
                    CreatedBy = panel.Actor.UserId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                ValidateTemplate(template);
                created.Add(template);
            }
        }
        if (created.Count == 0) return new HrBatchResultDto(0, skipped, null, []);
        db.HrTaskTemplates.AddRange(created);
        await db.SaveChangesAsync(ct);
        await GenerateSafelyAsync([.. created.Select(t => t.Id)], ct);
        var ids = created.Select(t => t.Id).ToList();
        // `templateIds` is the contract; `taskIds` repeats them — the old API returned the
        // template ids under task_ids for both batch endpoints (one response shape).
        return new HrBatchResultDto(created.Count, skipped, ids, ids);
    }

    /// <summary>Immediate generation after a rule is created — best effort (the nightly job repeats it).</summary>
    private async Task GenerateSafelyAsync(IReadOnlyList<string> templateIds, CancellationToken ct)
    {
        try
        {
            var now = Now;
            foreach (var id in templateIds) await GenerateAsync(null, id, now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "HR: shablondan darhol nusxa yaratilmadi");
        }
    }
}
