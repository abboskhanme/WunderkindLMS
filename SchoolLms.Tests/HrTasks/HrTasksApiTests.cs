using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;
using static SchoolLms.Tests.HrTasks.HrTasksKit;

namespace SchoolLms.Tests.HrTasks;

// ===========================================================================
//  HR topshiriqlari — HTTP: RBAC on every endpoint, manager scope (404 outside
//  the subtree), cycle rejection, employee creation (staff user + one-time
//  password), the task lifecycle through /api/hr/me (proof required, KPI
//  auto-done, reopen), upload limits, batch limits, immediate Telegram
//  notifications (a fake sender on a derived host), no-store on every response.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class HrTasksApiTests(ApiFixture fixture)
{
    private readonly HrTasksKit kit = new(fixture);

    // =====================================================================
    //  1. RBAC — every panel endpoint
    // =====================================================================

    public static TheoryData<string, string> PanelEndpoints() => new()
    {
        { "GET", "access" }, { "GET", "users" },
        { "GET", "positions" }, { "POST", "positions" }, { "PATCH", "positions/{x}" }, { "POST", "positions/{x}/deactivate" },
        { "GET", "employees" }, { "POST", "employees" }, { "GET", "employees/tree" }, { "GET", "employees/{x}" },
        { "PATCH", "employees/{x}" }, { "PUT", "employees/{x}/managers" }, { "POST", "employees/{x}/deactivate" },
        { "POST", "employees/{x}/invite" },
        { "GET", "templates" }, { "POST", "templates" }, { "POST", "templates/batch" }, { "PATCH", "templates/{x}" },
        { "POST", "templates/{x}/toggle" }, { "DELETE", "templates/{x}" },
        { "GET", "tasks" }, { "POST", "tasks" }, { "POST", "tasks/batch" }, { "GET", "tasks/{x}" }, { "PATCH", "tasks/{x}" },
        { "POST", "tasks/{x}/return" }, { "POST", "tasks/{x}/cancel" },
        { "POST", "attachments" }, { "DELETE", "attachments/{x}" },
        { "GET", "reports/summary" }, { "GET", "reports/employees" }, { "GET", "reports/export.csv" },
        { "GET", "reports/export.xlsx" },
        { "GET", "settings" }, { "PUT", "settings" }, { "GET", "dashboard" },
    };

    public static TheoryData<string, string> MeEndpoints() => new()
    {
        { "GET", "" }, { "GET", "tasks" }, { "GET", "tasks?date=2026-09-28" }, { "GET", "day" }, { "GET", "tasks/{x}" },
        { "POST", "tasks" }, { "POST", "tasks/{x}/complete" }, { "POST", "tasks/{x}/reopen" },
        { "POST", "tasks/{x}/accept" }, { "POST", "tasks/{x}/progress" }, { "POST", "proofs" }, { "GET", "summary" },
    };

    /// <summary>JSON everywhere; the upload endpoints only match <c>multipart/form-data</c>.</summary>
    private static Task<HttpResponseMessage> CallAsync(HttpClient client, string method, string path, string url) =>
        path is "attachments" or "proofs"
            ? client.PostAsync(url, Multipart([1], "a.png", "image/png", Guid.NewGuid().ToString()))
            : SendAsync(client, method, url);

    [Theory]
    [MemberData(nameof(PanelEndpoints))]
    public async Task Panel_anonim_401_oqituvchi_kassir_va_ruxsatsiz_xodim_403(string method, string path)
    {
        var url = $"{Panel}/{path.Replace("{x}", Guid.NewGuid().ToString())}";
        Task<HttpResponseMessage> SendAsync(HttpClient client, string m, string u) => CallAsync(client, m, path, u);

        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(anonymous, method, url)).StatusCode);

        // a teacher WITH an HR row is still not a panel user
        var teacher = await kit.PersonAsync(Roles.Teacher, HrRoles.Manager);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(teacher.Client, method, url)).StatusCode);

        using var bare = await fixture.Api.ClientAsAsync(Roles.Staff);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(bare, method, url)).StatusCode);
        using var other = await fixture.Api.ClientAsAsync(Roles.Staff, "lessonAnalysis", "staff");
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(other, method, url)).StatusCode);
        using var cashier = await fixture.Api.ClientAsAsync(Roles.Cashier);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(cashier, method, url)).StatusCode);

        if (method != "GET")
        {
            // "faqat ko'rish" reads, never writes
            using var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, HrTaskAccess.PermissionKey + Roles.ViewSuffix);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(viewer, method, url)).StatusCode);
        }
    }

    [Theory]
    [MemberData(nameof(MeEndpoints))]
    public async Task Me_anonim_401_hr_qatorisiz_403_hr_not_employee(string method, string path)
    {
        var url = $"{Me}/{path.Replace("{x}", Guid.NewGuid().ToString())}".TrimEnd('/');

        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await CallAsync(anonymous, method, path, url)).StatusCode);

        foreach (var role in new[] { Roles.Admin, Roles.Teacher, Roles.Staff })
        {
            var noRow = await kit.PersonAsync(role, hrRole: null, permissions: [HrTaskAccess.PermissionKey]);
            var response = path == "proofs"
                ? await noRow.Client.PostAsync(url, Multipart(new byte[] { 1 }, "a.png", "image/png", Guid.NewGuid().ToString()))
                : await SendAsync(noRow.Client, method, url);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("hr_not_employee", await CodeAsync(response));
        }

        // a fired employee (inactive row) is not an employee either
        var fired = await kit.PersonAsync(Roles.Staff, active: false);
        Assert.Equal("hr_not_employee", await CodeAsync(await SendAsync(fired.Client, "GET", Me)));
    }

    [Fact]
    public async Task Admin_superadmin_va_direktor_hammasini_oqiydi()
    {
        var director = await kit.PersonAsync(Roles.Staff, HrRoles.Director, [HrTaskAccess.PermissionKey]);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var super = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        foreach (var client in new[] { admin, super, director.Client })
            foreach (var path in new[]
                     {
                         "access", "users?q=Test", "positions", "employees?withTotal=true", "employees/tree", "templates",
                         "tasks?status=open&status=returned&sort=-created_at", "reports/summary",
                         "reports/employees?dateFrom=2026-09-01&dateTo=2026-09-30", "reports/export.csv",
                         "reports/export.xlsx", "settings", "dashboard",
                     })
            {
                var response = await client.GetAsync($"{Panel}/{path}");
                Assert.True(response.StatusCode == HttpStatusCode.OK,
                    $"GET {path} -> {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
            }
        var access = await ReadAsync(await director.Client.GetAsync($"{Panel}/access"));
        Assert.True(access.GetProperty("isDirector").GetBoolean());
        Assert.Equal("all", access.GetProperty("scope").GetString());
    }

    [Fact]
    public async Task Sozlamalar_va_lavozim_yozish_faqat_direktorga()
    {
        var manager = await kit.PersonAsync(Roles.Staff, HrRoles.Manager, [HrTaskAccess.PermissionKey]);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.Client.GetAsync($"{Panel}/settings")).StatusCode);
        var post = await manager.Client.PostAsJsonAsync($"{Panel}/positions", new { name = "Manager lavozimi" });
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal("forbidden", await CodeAsync(post));
        Assert.Equal(HttpStatusCode.OK, (await manager.Client.GetAsync($"{Panel}/positions")).StatusCode); // reads are open

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var name = "Buxgalter " + Guid.NewGuid().ToString("N")[..6];
        var created = await admin.PostAsJsonAsync($"{Panel}/positions", new { name = $"  {name}  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var position = await ReadAsync(created);
        Assert.Equal(name, position.GetProperty("name").GetString());
        var duplicate = await admin.PostAsJsonAsync($"{Panel}/positions", new { name });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // settings round-trip (restored afterwards — the row is shared)
        var before = await ReadAsync(await admin.GetAsync($"{Panel}/settings"));
        Assert.Equal("19:00", before.GetProperty("digestTime").GetString());
        var put = await admin.PutAsJsonAsync($"{Panel}/settings", new
        {
            timezone = "Asia/Tashkent", digestTime = "19:30", digestWeekdays = new[] { 1, 2, 3, 4, 5 },
            morningListEnabled = true, morningTime = "07:30", generationTime = "00:10", closeOverdueTime = "23:50",
            reminderEnabled = true, reminderOffsets = new[] { 30, 120 }, remindEndOfDay = false,
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var saved = await ReadAsync(put);
        Assert.Equal("19:30", saved.GetProperty("digestTime").GetString());
        Assert.Equal([120, 30], saved.GetProperty("reminderOffsets").EnumerateArray().Select(x => x.GetInt32()));
        var bad = await admin.PutAsJsonAsync($"{Panel}/settings", new
        {
            digestTime = "19:00", morningTime = "07:30", generationTime = "00:10", closeOverdueTime = "23:50", reminderOffsets = new[] { 1 },
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        await admin.PutAsJsonAsync($"{Panel}/settings", new
        {
            timezone = "Asia/Tashkent", digestTime = "19:00", digestWeekdays = new[] { 1, 2, 3, 4, 5, 6 },
            morningListEnabled = true, morningTime = "07:30", generationTime = "00:10", closeOverdueTime = "23:50",
            reminderEnabled = true, reminderOffsets = new[] { 120, 60, 30 }, remindEndOfDay = false,
        });
    }

    // =====================================================================
    //  2. Scope — a manager sees self + the whole subtree; outside = 404
    // =====================================================================

    [Fact]
    public async Task Manager_faqat_oz_shoxini_koradi_tashqarisi_404()
    {
        var manager = await kit.PersonAsync(Roles.Staff, HrRoles.Manager, [HrTaskAccess.PermissionKey], name: "Manager Shox");
        var sub = await kit.PersonAsync(Roles.Staff, name: "Bo'ysunuvchi Bir");
        var subSub = await kit.PersonAsync(Roles.Teacher, name: "Ichki Ikki");
        var outsider = await kit.PersonAsync(Roles.Staff, name: "Begona Uch");
        await kit.ReportsToAsync(sub.Employee!.Id, manager.Employee!.Id);
        await kit.ReportsToAsync(subSub.Employee!.Id, sub.Employee.Id);

        var due = DateTimeOffset.UtcNow.AddDays(2);
        var inside = await kit.TaskAsync(subSub.Employee.Id, due);
        var outside = await kit.TaskAsync(outsider.Employee!.Id, due);

        var list = await ReadAsync(await manager.Client.GetAsync($"{Panel}/employees?limit=200"));
        var ids = list.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();
        Assert.Contains(manager.Employee.Id, ids);
        Assert.Contains(sub.Employee.Id, ids);
        Assert.Contains(subSub.Employee.Id, ids);
        Assert.DoesNotContain(outsider.Employee.Id, ids);

        var c = manager.Client;
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"{Panel}/employees/{subSub.Employee.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"{Panel}/tasks/{inside.Id}")).StatusCode);
        foreach (var response in new[]
                 {
                     await c.GetAsync($"{Panel}/employees/{outsider.Employee.Id}"),
                     await c.GetAsync($"{Panel}/tasks/{outside.Id}"),
                     await c.PatchAsJsonAsync($"{Panel}/employees/{outsider.Employee.Id}", new { note = "x" }),
                     await c.PostAsync($"{Panel}/employees/{outsider.Employee.Id}/deactivate", null),
                     await c.PostAsJsonAsync($"{Panel}/tasks/{outside.Id}/cancel", new { comment = (string?)null }),
                     await c.PostAsJsonAsync($"{Panel}/tasks", new { employeeId = outsider.Employee.Id, title = "Begonaga", dueAt = due.ToString("o") }),
                 })
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var tasks = await ReadAsync(await c.GetAsync($"{Panel}/tasks?limit=200&overdue=false"));
        var taskIds = tasks.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetString()).ToList();
        Assert.Contains(inside.Id, taskIds);
        Assert.DoesNotContain(outside.Id, taskIds);

        // the whole subtree in the tree view, the manager's own node as the root
        var tree = await ReadAsync(await c.GetAsync($"{Panel}/employees/tree"));
        var root = Assert.Single(tree.GetProperty("roots").EnumerateArray());
        Assert.Equal(manager.Employee.Id, root.GetProperty("id").GetString());
        Assert.Equal(subSub.Employee.Id, root.GetProperty("children")[0].GetProperty("children")[0].GetProperty("id").GetString());

        // a manager may create an employee — it lands under the manager; cannot grant a scope role
        var created = await c.PostAsJsonAsync($"{Panel}/employees", new { fullName = "Yangi Xodim", phone = NextPhone() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var newEmployee = await ReadAsync(created);
        Assert.Equal([manager.Employee.Id], newEmployee.GetProperty("managerIds").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"{Panel}/employees/{newEmployee.GetProperty("id").GetString()}")).StatusCode);
        var escalate = await c.PatchAsJsonAsync($"{Panel}/employees/{sub.Employee.Id}", new { hrRole = HrRoles.Director });
        Assert.Equal(HttpStatusCode.Forbidden, escalate.StatusCode);
        Assert.Equal("hr_panel_access_forbidden", await CodeAsync(escalate));

        // a manager without an HR row sees nobody (fail-closed), still gets 200
        using var rowless = await fixture.Api.ClientAsAsync(Roles.Staff, HrTaskAccess.PermissionKey);
        var none = await ReadAsync(await rowless.GetAsync($"{Panel}/employees"));
        Assert.Empty(none.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Rahbarlikda_halqa_409()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var a = await kit.PersonAsync();
        var b = await kit.PersonAsync();
        var c = await kit.PersonAsync();
        await kit.ReportsToAsync(b.Employee!.Id, a.Employee!.Id); // b → a
        await kit.ReportsToAsync(c.Employee!.Id, b.Employee.Id);  // c → b

        var cycle = await admin.PutAsJsonAsync($"{Panel}/employees/{a.Employee.Id}/managers", new { managerIds = new[] { c.Employee.Id } });
        Assert.Equal(HttpStatusCode.Conflict, cycle.StatusCode);
        Assert.Equal("hr_manager_cycle", await CodeAsync(cycle));
        var self = await admin.PutAsJsonAsync($"{Panel}/employees/{a.Employee.Id}/managers", new { managerIds = new[] { a.Employee.Id } });
        Assert.Equal("hr_manager_cycle", await CodeAsync(self));
        var unknown = await admin.PutAsJsonAsync($"{Panel}/employees/{a.Employee.Id}/managers", new { managerIds = new[] { Guid.NewGuid().ToString() } });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("hr_manager_not_found", await CodeAsync(unknown));

        // a valid replacement: c now reports to a directly (the list is replaced)
        var ok = await admin.PutAsJsonAsync($"{Panel}/employees/{c.Employee.Id}/managers", new { managerIds = new[] { a.Employee.Id } });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal([a.Employee.Id], (await ReadAsync(ok)).GetProperty("managerIds").EnumerateArray().Select(x => x.GetString()));
    }

    // =====================================================================
    //  3. Employees: a staff user created from name + phone
    // =====================================================================

    [Fact]
    public async Task Ism_va_telefon_bilan_staff_foydalanuvchi_yaratiladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var phone = NextPhone();
        var response = await admin.PostAsJsonAsync($"{Panel}/employees",
            new { fullName = "Hodimov  Hodim", phone = phone[4..], branch = "school", hrRole = "manager" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("Hodimov Hodim", body.GetProperty("fullName").GetString());
        Assert.Equal(phone, body.GetProperty("phone").GetString());
        Assert.Equal(Roles.Staff, body.GetProperty("role").GetString());
        Assert.Equal(HrRoles.Manager, body.GetProperty("hrRole").GetString());
        Assert.Equal(phone.TrimStart('+'), body.GetProperty("credentials").GetProperty("login").GetString());
        var password = body.GetProperty("credentials").GetProperty("password").GetString()!;
        Assert.Equal(8, password.Length);
        Assert.Null(body.GetProperty("panelRole").GetString()); // no hrTasks key — the permission is granted elsewhere
        Assert.Equal(JsonValueKind.Null, body.GetProperty("telegramUserId").ValueKind);

        // the new account logs in with login = phone digits
        using var anonymous = fixture.Api.AnonymousClient();
        var login = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = phone.TrimStart('+'), password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // the same phone again → 409; an existing user → no credentials
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{Panel}/employees", new { fullName = "Boshqa", phone })).StatusCode);
        var (teacherUser, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var picked = await admin.PostAsJsonAsync($"{Panel}/employees", new { userId = teacherUser.Id, phone = NextPhone() });
        Assert.Equal(HttpStatusCode.Created, picked.StatusCode);
        var pickedBody = await ReadAsync(picked);
        Assert.Equal(teacherUser.FullName, pickedBody.GetProperty("fullName").GetString());
        Assert.Equal(Roles.Teacher, pickedBody.GetProperty("role").GetString());
        Assert.False(pickedBody.TryGetProperty("credentials", out var cred) && cred.ValueKind != JsonValueKind.Null);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync($"{Panel}/employees", new { userId = teacherUser.Id, phone = NextPhone() })).StatusCode);

        // candidates list marks HR rows
        var candidates = await ReadAsync(await admin.GetAsync($"{Panel}/users?q={Uri.EscapeDataString(teacherUser.FullName)}"));
        var candidate = candidates.EnumerateArray().Single(u => u.GetProperty("id").GetString() == teacherUser.Id);
        Assert.Equal(pickedBody.GetProperty("id").GetString(), candidate.GetProperty("employeeId").GetString());
    }

    [Fact]
    public async Task Ishdan_boshatish_ochiq_topshiriqlarni_bekor_qiladi_akkauntga_tegmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var e = await kit.PersonAsync();
        var open = await kit.TaskAsync(e.Employee!.Id, DateTimeOffset.UtcNow.AddDays(1));
        var done = await kit.TaskAsync(e.Employee.Id, DateTimeOffset.UtcNow.AddDays(-1), HrTaskStatuses.Done,
            completedAt: DateTimeOffset.UtcNow.AddDays(-1), comment: "ok");
        var response = await admin.PostAsync($"{Panel}/employees/{e.Employee.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await ReadAsync(response)).GetProperty("isActive").GetBoolean());
        await fixture.Api.WithDbAsync(async db =>
        {
            Assert.Equal(HrTaskStatuses.Cancelled, (await db.HrTasks.SingleAsync(t => t.Id == open.Id)).Status);
            Assert.Equal(HrTaskStatuses.Done, (await db.HrTasks.SingleAsync(t => t.Id == done.Id)).Status);
            Assert.True(await db.Users.AnyAsync(u => u.Id == e.User.Id)); // the account stays
        });
        // a fired employee gets no new task
        var task = await admin.PostAsJsonAsync($"{Panel}/tasks",
            new { employeeId = e.Employee.Id, title = "Yangi", dueAt = DateTimeOffset.UtcNow.AddDays(1).ToString("o") });
        Assert.Equal(HttpStatusCode.Conflict, task.StatusCode);
        Assert.Equal("hr_employee_inactive", await CodeAsync(task));

        // deleting the account of an HR employee is refused readably (task history), not a 500
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/staff/{e.User.Id}")).StatusCode);
    }

    // =====================================================================
    //  4. Lifecycle through /api/hr/me
    // =====================================================================

    [Fact]
    public async Task Hayot_sikli_isbot_talabi_qaytarish_qayta_ochish()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var e = await kit.PersonAsync(Roles.Teacher, name: "Sikl Xodimi");
        var due = DateTimeOffset.UtcNow.AddHours(5);
        var created = await admin.PostAsJsonAsync($"{Panel}/tasks", new
        {
            employeeId = e.Employee!.Id, title = "Hisobot tayyorlash", description = "Oylik", dueAt = due.ToString("o"),
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var task = await ReadAsync(created);
        var id = task.GetProperty("id").GetString();
        Assert.Equal("director", task.GetProperty("source").GetString());
        Assert.True(task.GetProperty("needsAcceptance").GetBoolean());
        Assert.Equal("keep_open", task.GetProperty("overduePolicy").GetString());

        // it is in today's list, not overdue
        var me = await ReadAsync(await e.Client.GetAsync(Me));
        Assert.Equal(e.Employee.Id, me.GetProperty("employeeId").GetString());
        var day = await ReadAsync(await e.Client.GetAsync($"{Me}/tasks?date={HrValues.LocalDate(due, Tz):yyyy-MM-dd}"));
        var item = day.GetProperty("items").EnumerateArray().Single(t => t.GetProperty("id").GetString() == id);
        Assert.False(item.GetProperty("isOverdue").GetBoolean());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("currentAmount").ValueKind); // check task

        // "ko'rdim"
        var accepted = await ReadAsync(await e.Client.PostAsync($"{Me}/tasks/{id}/accept", null));
        Assert.False(accepted.GetProperty("needsAcceptance").GetBoolean());

        // no proof, no comment → 409 proof_required (no hr_ prefix on /me)
        var noProof = await e.Client.PostAsJsonAsync($"{Me}/tasks/{id}/complete", new { comment = "  ", proofIds = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, noProof.StatusCode);
        Assert.Equal("proof_required", await CodeAsync(noProof));
        var done = await e.Client.PostAsJsonAsync($"{Me}/tasks/{id}/complete", new { comment = "Tayyor" });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.Equal("done", (await ReadAsync(done)).GetProperty("status").GetString());

        // reopen — same local day, the digest has not run → open again
        var reopened = await e.Client.PostAsync($"{Me}/tasks/{id}/reopen", null);
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        Assert.Equal("open", (await ReadAsync(reopened)).GetProperty("status").GetString());
        await e.Client.PostAsJsonAsync($"{Me}/tasks/{id}/complete", new { comment = "Yana tayyor" });

        // the director returns it; the employee completes again
        var returned = await admin.PostAsJsonAsync($"{Panel}/tasks/{id}/return", new { comment = "Rasm yo'q" });
        Assert.Equal("returned", (await ReadAsync(returned)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.PostAsJsonAsync($"{Panel}/tasks/{id}/return", new { comment = "" })).StatusCode);
        var again = await ReadAsync(await e.Client.PostAsJsonAsync($"{Me}/tasks/{id}/complete", new { comment = "Rasm qo'shdim" }));
        Assert.Equal("done", again.GetProperty("status").GetString());
        Assert.Equal("Rasm yo'q", again.GetProperty("directorComment").GetString());

        // a done task cannot be cancelled
        var cancel = await admin.PostAsync($"{Panel}/tasks/{id}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.Equal("hr_task_not_editable", await CodeAsync(cancel));

        // detail (panel) — flat task + proofs/progress/attachments
        var detail = await ReadAsync(await admin.GetAsync($"{Panel}/tasks/{id}"));
        foreach (var key in new[] { "id", "employeeId", "periodStart", "dueAt", "isLate", "proofs", "progress", "attachments",
                     "employeeName", "proofCount", "acceptedAt", "needsAcceptance", "currentAmount" })
            Assert.True(detail.TryGetProperty(key, out _), key);
        Assert.Equal("Sikl Xodimi", detail.GetProperty("employeeName").GetString());

        // another employee's task → 403 task_not_owned
        var stranger = await kit.PersonAsync();
        var foreign = await stranger.Client.PostAsJsonAsync($"{Me}/tasks/{id}/complete", new { comment = "meniki" });
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal("task_not_owned", await CodeAsync(foreign));
    }

    [Fact]
    public async Task KPI_qadami_maqsadga_yetganda_avtomatik_bajariladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var e = await kit.PersonAsync();
        // "Natijam" counts tasks due up to the end of TODAY in Tashkent — near midnight "+30 min" is
        // tomorrow and the KPI row is (rightly) not there. Keep the due time inside today.
        var tz = HrValues.Zone("Asia/Tashkent");
        var now = DateTimeOffset.UtcNow;
        var localNow = TimeZoneInfo.ConvertTime(now, tz);
        var endOfToday = new DateTimeOffset(localNow.Date.AddDays(1), localNow.Offset);
        var dueAt = now.AddMinutes(30) < endOfToday ? now.AddMinutes(30) : endOfToday.AddSeconds(-30);
        var created = await ReadAsync(await admin.PostAsJsonAsync($"{Panel}/tasks", new
        {
            employeeId = e.Employee!.Id, title = "Leadlar", kind = "numeric", targetAmount = "10", unit = "lead",
            dueAt = dueAt.ToString("o"),
        }));
        var id = created.GetProperty("id").GetString();
        var step = await ReadAsync(await e.Client.PostAsJsonAsync($"{Me}/tasks/{id}/progress", new { amount = "4.50", note = "ertalab" }));
        Assert.Equal("open", step.GetProperty("status").GetString());
        Assert.Equal(4.5m, step.GetProperty("currentAmount").GetDecimal());
        var zero = await e.Client.PostAsJsonAsync($"{Me}/tasks/{id}/progress", new { amount = "0" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, zero.StatusCode);
        Assert.Equal("invalid_amount", await CodeAsync(zero));
        var last = await ReadAsync(await e.Client.PostAsJsonAsync($"{Me}/tasks/{id}/progress", new { amount = 5.5 }));
        Assert.Equal("done", last.GetProperty("status").GetString());
        Assert.Equal(10m, last.GetProperty("currentAmount").GetDecimal());

        var detail = await ReadAsync(await admin.GetAsync($"{Panel}/tasks/{id}"));
        Assert.Equal(2, detail.GetProperty("progress").GetArrayLength());
        var summary = await ReadAsync(await e.Client.GetAsync($"{Me}/summary"));
        Assert.Contains(summary.GetProperty("kpi").EnumerateArray(), k => k.GetProperty("taskId").GetString() == id);
    }

    [Fact]
    public async Task Isbot_yuklash_chegaralari()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var e = await kit.PersonAsync();
        var task = await kit.TaskAsync(e.Employee!.Id, DateTimeOffset.UtcNow.AddDays(1));

        var html = await e.Client.PostAsync($"{Me}/proofs", Multipart([1, 2, 3], "x.html", "text/html", task.Id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, html.StatusCode);
        Assert.Equal("unsupported_proof_type", await CodeAsync(html));
        var svg = await e.Client.PostAsync($"{Me}/proofs", Multipart([1, 2, 3], "rasm.svg", "image/png", task.Id));
        Assert.Equal("unsupported_proof_type", await CodeAsync(svg));
        var big = await e.Client.PostAsync($"{Me}/proofs", Multipart(new byte[HrLimits.MaxProofBytes + 1], "big.pdf", "application/pdf", task.Id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, big.StatusCode);
        Assert.Equal("proof_too_large", await CodeAsync(big));

        // audio (a voice note on completion) and any document are fine; task_id works as the field name too
        var voice = await e.Client.PostAsync($"{Me}/proofs", Multipart([1, 2, 3], "ovoz.webm", "audio/webm", task.Id, snake: true));
        Assert.Equal(HttpStatusCode.Created, voice.StatusCode);
        var uploaded = await ReadAsync(voice);
        Assert.StartsWith("/uploads/hr/proofs/", uploaded.GetProperty("url").GetString());
        foreach (var name in new[] { "a.zip", "b.xlsx", "c.jpg", "d.pdf" })
            Assert.Equal(HttpStatusCode.Created,
                (await e.Client.PostAsync($"{Me}/proofs", Multipart([9], name, "application/octet-stream", task.Id))).StatusCode);
        var sixth = await e.Client.PostAsync($"{Me}/proofs", Multipart([9], "f.png", "image/png", task.Id));
        Assert.Equal(HttpStatusCode.Conflict, sixth.StatusCode);
        Assert.Equal("proof_limit_exceeded", await CodeAsync(sixth));

        // a file alone is enough proof
        var done = await ReadAsync(await e.Client.PostAsJsonAsync($"{Me}/tasks/{task.Id}/complete",
            new { comment = (string?)null, proofIds = Array.Empty<string>() }));
        Assert.Equal("done", done.GetProperty("status").GetString());
        Assert.Equal(5, done.GetProperty("proofs").GetArrayLength());
        var detail = await ReadAsync(await admin.GetAsync($"{Panel}/tasks/{task.Id}"));
        var voiceProof = detail.GetProperty("proofs").EnumerateArray().Single(p => p.GetProperty("originalName").GetString() == "ovoz.webm");
        Assert.Equal("file", voiceProof.GetProperty("kind").GetString());
        Assert.Equal("audio/webm", voiceProof.GetProperty("contentType").GetString());
        Assert.Equal(5, detail.GetProperty("proofCount").GetInt32());

        // someone else's task
        var stranger = await kit.PersonAsync();
        var foreign = await stranger.Client.PostAsync($"{Me}/proofs", Multipart([1], "a.png", "image/png", task.Id));
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }

    private static MultipartFormDataContent Multipart(byte[] bytes, string name, string type, string taskId, bool snake = false)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", name);
        form.Add(new StringContent(taskId), snake ? "task_id" : "taskId");
        return form;
    }

    // =====================================================================
    //  5. Batch limits + partial success, templates
    // =====================================================================

    [Fact]
    public async Task Batch_chegaralari_va_qisman_muvaffaqiyat()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var due = DateTimeOffset.UtcNow.AddDays(2).ToString("o");
        object Items(int n) => Enumerable.Range(1, n).Select(i => new { title = $"Band {i}", dueAt = due }).ToArray();
        string[] Ids(int n) => [.. Enumerable.Range(0, n).Select(_ => Guid.NewGuid().ToString())];

        foreach (var (employees, items) in new[] { (101, 1), (1, 21), (30, 17) })
        {
            var tooBig = await admin.PostAsJsonAsync($"{Panel}/tasks/batch", new { employeeIds = Ids(employees), items = Items(items) });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, tooBig.StatusCode);
            Assert.Equal("hr_batch_too_large", await CodeAsync(tooBig));
            var templates = await admin.PostAsJsonAsync($"{Panel}/templates/batch",
                new { employeeIds = Ids(employees), items = Enumerable.Range(1, items).Select(i => new { title = $"Qoida {i}", recurrence = "daily", weekdays = new[] { 1 } }).ToArray() });
            Assert.Equal("hr_batch_too_large", await CodeAsync(templates));
        }

        var a = await kit.PersonAsync();
        var b = await kit.PersonAsync();
        var fired = await kit.PersonAsync(active: false);
        var unknown = Guid.NewGuid().ToString();
        var response = await admin.PostAsJsonAsync($"{Panel}/tasks/batch",
            new { employeeIds = new[] { a.Employee!.Id, b.Employee!.Id, fired.Employee!.Id, unknown }, items = Items(3) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await ReadAsync(response);
        Assert.Equal(6, result.GetProperty("created").GetInt32());
        Assert.Equal(6, result.GetProperty("taskIds").GetArrayLength());
        var skipped = result.GetProperty("skipped").EnumerateArray()
            .ToDictionary(s => s.GetProperty("employeeId").GetString()!, s => s.GetProperty("reason").GetString());
        Assert.Equal("inactive", skipped[fired.Employee.Id]);
        Assert.Equal("not_found", skipped[unknown]);
    }

    [Fact]
    public async Task Shablon_yaratish_bugungi_nusxa_va_ochirishda_tarix_qoladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var e = await kit.PersonAsync();
        var invalid = await admin.PostAsJsonAsync($"{Panel}/templates", new
        {
            title = "Kunlik", targetType = "employee", targetEmployeeId = e.Employee!.Id, recurrence = "daily", weekdays = Array.Empty<int>(),
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal("hr_invalid_recurrence", await CodeAsync(invalid));
        var badKpi = await admin.PostAsJsonAsync($"{Panel}/templates", new
        {
            title = "KPI", targetType = "employee", targetEmployeeId = e.Employee.Id, recurrence = "daily",
            weekdays = new[] { 1, 2, 3, 4, 5, 6, 7 }, kind = "numeric",
        });
        Assert.Equal("hr_invalid_amount", await CodeAsync(badKpi));

        var created = await admin.PostAsJsonAsync($"{Panel}/templates", new
        {
            title = "Har kuni hisobot", targetType = "employee", targetEmployeeId = e.Employee.Id, recurrence = "daily",
            weekdays = new[] { 7, 1, 2, 3, 4, 5, 6, 1 }, dueTime = "23:58",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var template = await ReadAsync(created);
        var templateId = template.GetProperty("id").GetString();
        Assert.Equal("23:58", template.GetProperty("dueTime").GetString());
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], template.GetProperty("weekdays").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal("keep_open", template.GetProperty("overduePolicy").GetString());

        // today's instance was generated at once (unless 23:58 local already passed — then none)
        var tasks = await ReadAsync(await admin.GetAsync($"{Panel}/tasks?employeeId={e.Employee.Id}&limit=50"));
        var generated = tasks.GetProperty("items").EnumerateArray().Where(t => t.GetProperty("templateId").GetString() == templateId).ToList();
        Assert.True(generated.Count <= 1);
        Assert.All(generated, t => Assert.Equal("template", t.GetProperty("source").GetString()));

        var toggled = await ReadAsync(await admin.PostAsync($"{Panel}/templates/{templateId}/toggle", null));
        Assert.False(toggled.GetProperty("isActive").GetBoolean());
        var patched = await admin.PatchAsJsonAsync($"{Panel}/templates/{templateId}", new { recurrence = "monthly", daysOfMonth = new[] { 31 } });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        var p = await ReadAsync(patched);
        Assert.Empty(p.GetProperty("weekdays").EnumerateArray());
        Assert.Equal([31], p.GetProperty("daysOfMonth").EnumerateArray().Select(x => x.GetInt32()));

        // a history task of the template survives its deletion with templateId = null
        var history = await kit.TaskAsync(e.Employee.Id, DateTimeOffset.UtcNow.AddDays(-3), HrTaskStatuses.Done,
            source: HrTaskSources.Template, completedAt: DateTimeOffset.UtcNow.AddDays(-3), comment: "ok");
        await fixture.Api.WithDbAsync(async db =>
        {
            (await db.HrTasks.SingleAsync(t => t.Id == history.Id)).TemplateId = templateId;
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Panel}/templates/{templateId}")).StatusCode);
        await fixture.Api.WithDbAsync(async db =>
        {
            var kept = await db.HrTasks.SingleAsync(t => t.Id == history.Id);
            Assert.Null(kept.TemplateId);
        });
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{Panel}/templates/{templateId}")).StatusCode);
    }

    // =====================================================================
    //  6. Telegram — immediate messages (fake sender, never the network)
    // =====================================================================

    [Fact]
    public async Task Yangi_va_qaytarilgan_topshiriq_xodimga_darhol_boradi()
    {
        var (adminUser, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        using var admin = kit.HostClient(adminUser);
        var e = await kit.PersonAsync(telegram: true);
        var chat = e.Chat!.Value;

        var created = await admin.PostAsJsonAsync($"{Panel}/tasks", new
        {
            employeeId = e.Employee!.Id, title = "Shartnoma <tayyorlash>", dueAt = "2031-03-10T18:00:00",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadAsync(created)).GetProperty("id").GetString();
        var message = Assert.Single(Sender.To(chat));
        Assert.Equal("🆕 <b>Sizga yangi topshiriq</b>\n\nShartnoma &lt;tayyorlash&gt;\n⏰ Muddat: 10.03.2031 18:00\n\n"
                     + "Bajargach «Topshiriqlarim» bo'limidan belgilang.", message);

        await fixture.Api.WithDbAsync(async db =>
        {
            var t = await db.HrTasks.SingleAsync(x => x.Id == id);
            t.Status = HrTaskStatuses.Done;
            t.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        });
        await admin.PostAsJsonAsync($"{Panel}/tasks/{id}/return", new { comment = "Imzo yo'q" });
        Assert.Equal(2, Sender.To(chat).Count);
        Assert.StartsWith("↩️ <b>Topshiriq qaytarildi</b>", Sender.To(chat)[1]);

        // a batch → ONE message per employee
        var batch = await admin.PostAsJsonAsync($"{Panel}/tasks/batch", new
        {
            employeeIds = new[] { e.Employee.Id },
            items = new[] { new { title = "Bir", dueAt = "2031-03-11T10:00:00" }, new { title = "Ikki", dueAt = "2031-03-11T10:00:00" },
                            new { title = "Uch", dueAt = "2031-03-11T10:00:00" } },
        });
        Assert.Equal(HttpStatusCode.Created, batch.StatusCode);
        Assert.Equal(3, Sender.To(chat).Count);
        Assert.StartsWith("🆕 <b>Sizga 3 ta yangi topshiriq berildi</b>\n\n1. Bir\n2. Ikki\n3. Uch", Sender.To(chat)[2]);

        // an unlinked employee — nothing is sent, the request still succeeds
        var unlinked = await kit.PersonAsync();
        var before = Sender.Messages.Count;
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"{Panel}/tasks",
            new { employeeId = unlinked.Employee!.Id, title = "Jim", dueAt = "2031-03-10T18:00:00" })).StatusCode);
        Assert.Equal(before, Sender.Messages.Count);
    }

    [Fact]
    public async Task Taklif_bir_martalik_bog_lash_kodi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var e = await kit.PersonAsync();
        var invite = await admin.PostAsync($"{Panel}/employees/{e.Employee!.Id}/invite", null);
        Assert.Equal(HttpStatusCode.OK, invite.StatusCode);
        var body = await ReadAsync(invite);
        Assert.Matches("^[A-Z0-9]{4}-[A-Z0-9]{4}$", body.GetProperty("inviteCode").GetString()!); // our link-code format
        Assert.Equal(e.Employee.Id, body.GetProperty("employeeId").GetString());
        var linked = await kit.PersonAsync(telegram: true);
        var again = await admin.PostAsync($"{Panel}/employees/{linked.Employee!.Id}/invite", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        // the card shows the linked Telegram id
        var card = await ReadAsync(await admin.GetAsync($"{Panel}/employees/{linked.Employee.Id}"));
        Assert.Equal(linked.Chat, card.GetProperty("telegramUserId").GetInt64());
    }

    [Fact]
    public async Task Mening_topshiriqlarim_sahifa_shaklida()
    {
        var e = await kit.PersonAsync(Roles.Staff, HrRoles.Manager, [HrTaskAccess.PermissionKey]);
        var mine = await kit.TaskAsync(e.Employee!.Id, DateTimeOffset.UtcNow.AddDays(1), title: "Meniki");
        var other = await kit.PersonAsync();
        await kit.TaskAsync(other.Employee!.Id, DateTimeOffset.UtcNow.AddDays(1), title: "Begona");
        var page = await ReadAsync(await e.Client.GetAsync($"{Me}/tasks?limit=20&withTotal=true&employeeId={other.Employee.Id}"));
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(mine.Id, Assert.Single(items).GetProperty("id").GetString()); // employeeId in the query is ignored
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.False(page.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);

        var self = await e.Client.PostAsJsonAsync($"{Me}/tasks", new { title = "O'zimga", description = (string?)null, dueAt = "2031-01-05T18:00:00+05:00" });
        Assert.Equal(HttpStatusCode.Created, self.StatusCode);
        var selfTask = await ReadAsync(self);
        Assert.Equal("self", selfTask.GetProperty("source").GetString());
        Assert.False(selfTask.GetProperty("needsAcceptance").GetBoolean());
    }
}
