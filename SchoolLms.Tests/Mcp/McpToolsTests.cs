using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using Npgsql;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Mcp;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Mcp;

/// <summary>
/// MCP protocol + tools (docs/modules/mcp-readonly.md "Tests"): initialize, tools/list,
/// tools/call for every tool family (happy path + permission denied), audit rows, the
/// read-only guarantee (app_ro cannot write; tools only get the read-only context) and the
/// secret scan over every tool's output.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class McpToolsTests(ApiFixture fixture)
{
    /// <summary>Tool → sample arguments that make it succeed on a small seeded school.</summary>
    private static Dictionary<string, object> SampleCalls(string cls, string studentId) => new()
    {
        ["school_overview"] = new { },
        ["students_search"] = new { search = "Mcpov" },
        ["student_profile"] = new { studentId },
        ["classes_list"] = new { },
        ["track_groups_list"] = new { },
        ["class_roster"] = new { className = cls },
        ["class_performance"] = new { className = cls },
        ["timetable"] = new { className = cls },
        ["attendance_daily"] = new { },
        ["attendance_class_day"] = new { className = cls },
        ["attendance_analytics"] = new { },
        ["boarding_attendance"] = new { session = "evening" },
        ["journal_marks"] = new { className = cls, subject = "McpFan", quarter = 1 },
        ["seasonal_marks"] = new { },
        ["block_test_results"] = new { },
        ["finance_debtors"] = new { },
        ["finance_arrears_pivot"] = new { fromMonth = DateTime.Today.AddMonths(-2).ToString("yyyy-MM"), toMonth = DateTime.Today.ToString("yyyy-MM") },
        ["finance_transactions"] = new { },
        ["finance_invoices"] = new { },
        ["finance_summary"] = new { },
        ["finance_salary_payments"] = new { },
        ["finance_discounts"] = new { },
        ["finance_expenses"] = new { },
        ["employees_list"] = new { },
        ["leads_list"] = new { },
        ["leads_funnel"] = new { },
        ["survey_submissions"] = new { },
        ["admission_candidates"] = new { },
        ["discipline_incidents"] = new { },
        ["discipline_scores"] = new { },
        ["messages_history"] = new { },
        ["certificates_list"] = new { },
        ["contracts_list"] = new { },
    };

    /// <summary>
    /// Removes what <see cref="SeedSchoolAsync"/> added. The shared test DB is close to the
    /// arrears-pivot 600-pupil cap (FinanceReportQueries.MaxArrearsStudents) that other tests
    /// hit without a class filter — so MCP tests leave the pupil count exactly as they found it.
    /// </summary>
    private Task CleanupAsync(string cls) => fixture.Api.WithDbAsync(async db =>
    {
        var ids = await db.Students.Where(s => s.ClassName == cls).Select(s => s.Id).ToListAsync();
        var classIds = await db.Classes.Where(c => c.Name == cls).Select(c => c.Id).ToListAsync();
        await db.JournalEntries.Where(e => ids.Contains(e.StudentId)).ExecuteDeleteAsync();
        await db.DisciplinePoints.Where(p => ids.Contains(p.StudentId)).ExecuteDeleteAsync();
        await db.Assignments.Where(a => classIds.Contains(a.ClassId)).ExecuteDeleteAsync();
        await db.Students.Where(s => s.ClassName == cls).ExecuteDeleteAsync();
        await db.Classes.Where(c => c.Name == cls).ExecuteDeleteAsync();
    });

    private async Task<(string Class, string StudentId)> SeedSchoolAsync()
    {
        var cls = "M" + Guid.NewGuid().ToString("N")[..5];
        var st = new Student { FullName = "Mcpov Ali " + cls, ClassName = cls, ParentPhone = "+998900000000" };
        await fixture.Api.WithDbAsync(async db =>
        {
            var klass = new SchoolClass { Name = cls, Grade = 7 };
            db.Classes.Add(klass);
            db.Students.Add(st);
            var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Name == "McpFan");
            if (subject is null) db.Subjects.Add(subject = new Subject { Name = "McpFan" });
            // Data-dependent paths (split-query Includes) only touch their tables when rows exist:
            // an assignment with a quiz question, a journal mark and a discipline point make the
            // pupil card read assignments/test_questions/journal/discipline under app_ro.
            db.Assignments.Add(new Assignment
            {
                ClassId = klass.Id, ClassIds = [klass.Id], SubjectId = subject.Id, Quarter = 1, Title = "Mcp quiz",
                Questions = [new TestQuestion { Text = "2+2?", Options = ["3", "4"], CorrectIndex = 1 }],
            });
            db.JournalEntries.Add(new JournalEntry
            {
                ClassId = klass.Id, SubjectId = subject.Id, StudentId = st.Id, Quarter = 1,
                Date = DateTime.Today.ToString("yyyy-MM-dd"), Period = 1, Grade = 5,
            });
            db.DisciplinePoints.Add(new DisciplinePoint
            {
                StudentId = st.Id, ReasonName = "Test", Points = -5, CreatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            });
            await db.SaveChangesAsync();
        });
        return (cls, st.Id);
    }

    [Fact]
    public async Task Initialize_va_tools_list_hammasi_faqat_oqish()
    {
        var flow = new McpFlow(fixture.Api);
        var (_, tokens) = await flow.ConnectNewAsync(Roles.Admin);
        var init = await flow.InitializeAsync(tokens.Access);
        Assert.NotNull(init["capabilities"]!["tools"]);
        Assert.Contains("FAQAT O'QISH", init["instructions"]!.GetValue<string>());

        var list = await flow.RpcAsync(tokens.Access, "tools/list");
        var tools = list["tools"]!.AsArray();
        var names = tools.Select(t => t!["name"]!.GetValue<string>()).ToHashSet();
        Assert.Equal(SampleCalls("x", "y").Keys.ToHashSet(), names);
        foreach (var t in tools)
        {
            var ann = t!["annotations"]!;
            Assert.True(ann["readOnlyHint"]!.GetValue<bool>(), $"{t["name"]} readOnlyHint");
            Assert.False(ann["destructiveHint"]!.GetValue<bool>(), $"{t["name"]} destructiveHint");
            Assert.False(string.IsNullOrWhiteSpace(t["description"]?.GetValue<string>()));
        }
    }

    [Fact]
    public async Task Admin_har_bir_vositani_chaqiradi_va_sirlar_chiqmaydi()
    {
        var (cls, studentId) = await SeedSchoolAsync();
        try
        {
            var flow = new McpFlow(fixture.Api);
            var (user, tokens) = await flow.ConnectNewAsync(Roles.SuperAdmin);

            // Material that must never leak: every user's password hash and initial password.
            List<string> secrets = [];
            await fixture.Api.WithDbAsync(async db =>
            {
                var u = await db.Users.SingleAsync(x => x.Id == user.Id);
                secrets.Add(u.PasswordHash);
                if (u.InitialPassword is { } ip) secrets.Add(ip);
                secrets.AddRange(await db.McpTokens.Select(t => t.TokenHash).Take(50).ToListAsync());
            });
            secrets.Add(tokens.Access);
            secrets.Add(tokens.Refresh);

            foreach (var (tool, args) in SampleCalls(cls, studentId))
            {
                var (isError, text) = await flow.CallAsync(tokens.Access, tool, args);
                Assert.False(isError, $"{tool}: {text}");
                var node = JsonNode.Parse(text);
                Assert.NotNull(node);
                foreach (var name in PropertyNames(node))
                    Assert.False(McpToolContext.IsSecretName(name), $"{tool} output has secret-looking field '{name}'");
                foreach (var s in secrets)
                    Assert.DoesNotContain(s, text);
            }

            var (_, search) = await flow.CallAsync(tokens.Access, "students_search", new { search = "Mcpov Ali " + cls });
            var body = JsonNode.Parse(search)!;
            Assert.Equal(1, body["total"]!.GetValue<int>());
            Assert.Equal(studentId, body["items"]![0]!["id"]!.GetValue<string>());
            Assert.NotNull(body["items"]![0]!["balance"]); // admin sees money

            // Every call audited, with row counts.
            await fixture.Api.WithDbAsync(async db =>
            {
                var audit = await db.McpAudit.Where(a => a.UserId == user.Id).ToListAsync();
                Assert.True(audit.Count >= SampleCalls(cls, studentId).Count);
                Assert.All(audit, a => Assert.Equal("ok", a.Outcome));
                Assert.Contains(audit, a => a.Tool == "students_search" && a.RowCount == 1);
            });
        }
        finally { await CleanupAsync(cls); }
    }

    [Fact]
    public async Task Xodim_faqat_oz_bolimlarini_koradi()
    {
        var (cls, studentId) = await SeedSchoolAsync();
        try
        {
            var flow = new McpFlow(fixture.Api);
            // students (view only) + aiAccess — no finance, no attendance, no leads.
            var (user, tokens) = await flow.ConnectNewAsync(Roles.Staff, McpAccess.PermissionKey, "students:view");

            var (ok, text) = await flow.CallAsync(tokens.Access, "students_search", new { search = "Mcpov Ali " + cls });
            Assert.False(ok, text);
            var row = JsonNode.Parse(text)!["items"]![0]!;
            Assert.Null(row["balance"]); // money hidden without finance
            var (profileErr, profile) = await flow.CallAsync(tokens.Access, "student_profile", new { studentId });
            Assert.False(profileErr, profile);
            Assert.Null(JsonNode.Parse(profile)!["balance"]);

            foreach (var tool in new[] { "finance_debtors", "finance_transactions", "finance_summary", "attendance_daily",
                         "leads_list", "discipline_scores", "journal_marks", "timetable", "school_overview", "messages_history" })
            {
                var args = SampleCalls(cls, studentId)[tool];
                var (isError, msg) = await flow.CallAsync(tokens.Access, tool, args);
                Assert.True(isError, $"{tool} should be denied: {msg}");
                Assert.Contains("Ruxsat yo'q", msg);
            }
            // Debt filter on pupils needs finance too.
            var (debtErr, _) = await flow.CallAsync(tokens.Access, "students_search", new { debtorsOnly = true });
            Assert.True(debtErr);

            await fixture.Api.WithDbAsync(async db =>
                Assert.True(await db.McpAudit.CountAsync(a => a.UserId == user.Id && a.Outcome == "denied") >= 10));
        }
        finally { await CleanupAsync(cls); }
    }

    [Fact]
    public async Task Moliya_faqat_korish_xodimi_moliyani_oqiydi()
    {
        var flow = new McpFlow(fixture.Api);
        var (_, tokens) = await flow.ConnectNewAsync(Roles.Staff, McpAccess.PermissionKey, "finance:view");
        foreach (var tool in new[] { "finance_debtors", "finance_invoices", "finance_expenses", "finance_discounts", "finance_salary_payments" })
        {
            var (isError, text) = await flow.CallAsync(tokens.Access, tool, new { });
            Assert.False(isError, $"{tool}: {text}");
        }
        var (studentsErr, _) = await flow.CallAsync(tokens.Access, "students_search", new { });
        Assert.True(studentsErr);
    }

    [Fact]
    public async Task Sahifa_va_sana_chegaralari()
    {
        var flow = new McpFlow(fixture.Api);
        var (_, tokens) = await flow.ConnectNewAsync(Roles.Admin);
        var (_, page) = await flow.CallAsync(tokens.Access, "students_search", new { pageSize = 100000 });
        Assert.Equal(McpToolContext.MaxPageSize, JsonNode.Parse(page)!["pageSize"]!.GetValue<int>());

        var (err, msg) = await flow.CallAsync(tokens.Access, "finance_transactions", new { from = "2020-01-01", to = "2026-01-01" });
        Assert.True(err);
        Assert.Contains("366", msg);
        var (err2, _) = await flow.CallAsync(tokens.Access, "attendance_analytics", new { from = "2026-01-01", to = "2026-06-01" });
        Assert.True(err2);
    }

    // ------------------------------------------------------------------
    //  Read-only guarantee
    // ------------------------------------------------------------------

    [Fact]
    public async Task App_ro_roli_yoza_olmaydi()
    {
        var db = fixture.Database;
        Assert.False(string.IsNullOrEmpty(db.AppRoConnectionString));
        var student = new Student { FullName = "Ro Test", ClassName = "RoTmp" + Guid.NewGuid().ToString("N")[..6] };
        await fixture.Api.WithDbAsync(async ctx => { ctx.Students.Add(student); await ctx.SaveChangesAsync(); });
        var sid = student.Id;
        try
        {

            await using var ro = new NpgsqlConnection(db.AppRoConnectionString);
            await ro.OpenAsync();
            Assert.Equal(1L, await Scalar(ro, $"select count(*) from students where id = '{sid}'"));

            foreach (var sql in new[]
                     {
                         "insert into subjects (id, name) values ('x', 'x')",
                         $"update students set full_name = 'hacked' where id = '{sid}'",
                         $"delete from students where id = '{sid}'",
                         "truncate mcp_audit",
                         "create table ro_evil (id int)",
                     })
            {
                var ex = await Assert.ThrowsAsync<PostgresException>(() => Exec(ro, sql));
                Assert.Contains(ex.SqlState, new[] { "25006", "42501" }); // read-only transaction / permission denied
            }
            // Even after switching the session to read-write, privileges still refuse.
            await Exec(ro, "set session characteristics as transaction read write");
            var ex2 = await Assert.ThrowsAsync<PostgresException>(() => Exec(ro, $"delete from students where id = '{sid}'"));
            Assert.Equal("42501", ex2.SqlState);

            // Hashed OAuth secrets are not even readable for app_ro.
            var ex3 = await Assert.ThrowsAsync<PostgresException>(() => Scalar(ro, "select count(*) from mcp_tokens"));
            Assert.Equal("42501", ex3.SqlState);
            await Assert.ThrowsAsync<PostgresException>(() => Scalar(ro, "select count(*) from mcp_auth_codes"));

            // The role itself: not superuser, owns nothing, member of no writing role.
            Assert.Equal(0L, await Scalar(ro, """
                select count(*) from pg_class c join pg_roles r on r.oid = c.relowner where r.rolname = 'app_ro'
                """));
            Assert.Equal(false, await Scalar(ro, "select rolsuper from pg_roles where rolname = 'app_ro'"));
            Assert.Equal(false, await Scalar(ro, "select pg_has_role('app_ro', 'app_rw', 'MEMBER')"));
        }
        finally { await CleanupAsync(student.ClassName); }
    }

    [Fact]
    public async Task ReadOnlyDatabase_app_ro_bilan_ulanadi_va_SaveChanges_rad_etadi()
    {
        await using var ctx = new ReadOnlyDatabase(fixture.Database.AppRoConnectionString).CreateContext();
        Assert.True(await ctx.Students.CountAsync() >= 0);
        Assert.Equal("app_ro", await ctx.Database.SqlQueryRaw<string>("select current_user as \"Value\"").SingleAsync());
        Assert.Equal(QueryTrackingBehavior.NoTracking, ctx.ChangeTracker.QueryTrackingBehavior);
        ctx.Subjects.Add(new Subject { Name = "never" });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        Assert.Equal(ReadOnlyDatabase.RefuseWrites.Message, ex.Message);
        Assert.Throws<InvalidOperationException>(() => ctx.SaveChanges());

        // The context the tools get is exactly this one.
        using var scope = fixture.Api.Services.CreateScope();
        var tc = scope.ServiceProvider.GetRequiredService<McpToolContext>();
        Assert.Equal("app_ro", await tc.Db.Database.SqlQueryRaw<string>("select current_user as \"Value\"").SingleAsync());
    }

    [Fact]
    public void Vositalar_faqat_readonly_kontekstni_oladi()
    {
        Assert.NotEmpty(McpSetup.ToolTypes);
        foreach (var type in McpSetup.ToolTypes)
        {
            Assert.NotNull(type.GetCustomAttribute<McpServerToolTypeAttribute>());
            // Constructor: exactly one dependency — McpToolContext.
            var ctor = Assert.Single(type.GetConstructors());
            Assert.Equal([typeof(McpToolContext)], ctor.GetParameters().Select(p => p.ParameterType));
            // No field of a writable context / service provider.
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                Assert.True(f.FieldType == typeof(McpToolContext), $"{type.Name}.{f.Name}: {f.FieldType}");

            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null).ToList();
            Assert.NotEmpty(methods);
            foreach (var m in methods)
            {
                var attr = m.GetCustomAttribute<McpServerToolAttribute>()!;
                Assert.True(attr.ReadOnly, $"{attr.Name} ReadOnly");
                Assert.False(attr.Destructive, $"{attr.Name} Destructive");
                Assert.True(attr.Idempotent, $"{attr.Name} Idempotent");
                Assert.False(attr.OpenWorld, $"{attr.Name} OpenWorld");
                foreach (var p in m.GetParameters())
                    Assert.False(typeof(IAppDbContext).IsAssignableFrom(p.ParameterType) || p.ParameterType == typeof(IServiceProvider),
                        $"{attr.Name}: parameter {p.Name} is {p.ParameterType.Name}");
                // Tool names never suggest a write.
                Assert.DoesNotMatch("(?i)(create|update|delete|remove|set_|send|import|approve|write|add_|revoke)", attr.Name!);
            }
        }
        // McpToolContext gets its database ONLY from ReadOnlyDatabase (app_ro) — never the
        // DI-scoped read/write AppDbContext, never a service provider.
        Assert.Equal([typeof(Microsoft.AspNetCore.Http.IHttpContextAccessor), typeof(ReadOnlyDatabase),
                typeof(Microsoft.Extensions.Caching.Memory.IMemoryCache)],
            typeof(McpToolContext).GetConstructors().Single().GetParameters().Select(p => p.ParameterType));
        Assert.DoesNotContain(typeof(IServiceProvider), typeof(McpToolContext).GetProperties().Select(p => p.PropertyType));
    }

    [Fact]
    public void Sirli_maydon_nomlari_aniqlanadi()
    {
        foreach (var n in new[] { "passwordHash", "PasswordHash", "initialPassword", "login", "email", "token", "botToken",
                     "ingest_token", "refreshToken", "apiKey", "clientSecret", "tokenHint", "parentPassportUrl" })
            Assert.True(McpToolContext.IsSecretName(n), n);
        foreach (var n in new[] { "fullName", "className", "balance", "phone", "parentPhone", "amount" })
            Assert.False(McpToolContext.IsSecretName(n), n);
    }

    private static IEnumerable<string> PropertyNames(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (k, v) in o)
                {
                    yield return k;
                    foreach (var n in PropertyNames(v)) yield return n;
                }
                break;
            case JsonArray a:
                foreach (var i in a)
                    foreach (var n in PropertyNames(i)) yield return n;
                break;
        }
    }

    private static async Task Exec(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> Scalar(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        return await cmd.ExecuteScalarAsync();
    }
}


/// <summary>Security review fixes (M2, M3, M6, L2) — tools and the read-only role.</summary>
[Collection(SchoolLmsCollection.Name)]
public class McpToolsHardeningTests(ApiFixture fixture)
{
    [Fact]
    public async Task Kunduzgi_davomat_ruxsati_yotoqxonani_ochmaydi()
    {
        var flow = new McpFlow(fixture.Api);
        var (_, tokens) = await flow.ConnectNewAsync(Roles.Staff, McpAccess.PermissionKey, "attendance");
        var (e1, m1) = await flow.CallAsync(tokens.Access, "boarding_attendance", new { session = "evening" });
        Assert.True(e1, m1);
        var (e2, _) = await flow.CallAsync(tokens.Access, "boarding_attendance", new { session = "dorm" });
        Assert.True(e2);
        var (e3, m3) = await flow.CallAsync(tokens.Access, "attendance_daily");
        Assert.False(e3, m3);

        var (_, dormOnly) = await flow.ConnectNewAsync(Roles.Staff, McpAccess.PermissionKey, "attendanceDorm");
        Assert.False((await flow.CallAsync(dormOnly.Access, "boarding_attendance", new { session = "dorm" })).IsError);
        Assert.True((await flow.CallAsync(dormOnly.Access, "boarding_attendance", new { session = "evening" })).IsError);
    }

    [Fact]
    public async Task Sahifa_ruxsatlari_boyicha_vositalar()
    {
        var flow = new McpFlow(fixture.Api);
        // Page-grant role: only "Qarzdorlar bilan ishlash" (view) + the derived section key.
        var (_, tokens) = await flow.ConnectNewAsync(Roles.Staff, McpAccess.PermissionKey,
            "/admin/finance/debtors|view", "finance:view");
        Assert.False((await flow.CallAsync(tokens.Access, "finance_debtors")).IsError);
        foreach (var tool in new[] { "finance_salary_payments", "finance_summary", "finance_transactions", "finance_invoices",
                     "finance_expenses", "students_search", "attendance_daily" })
        {
            var (isError, msg) = await flow.CallAsync(tokens.Access, tool);
            Assert.True(isError, $"{tool}: {msg}");
            Assert.Contains("Ruxsat yo'q", msg);
        }

        // Salary page only.
        var (_, salary) = await flow.ConnectNewAsync(Roles.Staff, McpAccess.PermissionKey,
            "/admin/teachers/salary|edit", "finance");
        Assert.False((await flow.CallAsync(salary.Access, "finance_salary_payments")).IsError);
        Assert.True((await flow.CallAsync(salary.Access, "finance_debtors")).IsError);

        // Retired page grant opens its successor (lib/access.ts RETIRED_PAGES).
        var (_, retired) = await flow.ConnectNewAsync(Roles.Staff, McpAccess.PermissionKey, "/admin/teachers|view", "teachers:view");
        Assert.False((await flow.CallAsync(retired.Access, "employees_list")).IsError);
    }

    [Fact]
    public void Sahifa_kalitlari_navigatsiyada_bor()
    {
        // Every page an area names must exist in the client menu, otherwise page-grant roles
        // would be silently denied.
        var nav = File.ReadAllText(Path.Combine(FindRepoRoot(), "schoollms.client", "src", "config", "navigation.ts"));
        var areas = typeof(McpAreas).GetFields().Select(f => (McpArea)f.GetValue(null)!).ToList();
        Assert.NotEmpty(areas);
        foreach (var page in areas.SelectMany(a => a.Pages).Distinct())
            Assert.True(nav.Contains($"to: '{page}'"), $"page '{page}' not found in navigation.ts");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SchoolLms.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public async Task App_ro_maxfiy_ustun_va_jadvallarni_oqiy_olmaydi()
    {
        await using var ro = new NpgsqlConnection(fixture.Database.AppRoConnectionString);
        await ro.OpenAsync();
        // Allowed: non-secret columns.
        await Scalar(ro, "select count(full_name) from users");
        await Scalar(ro, "select count(group_lessons_enabled) from school_meta");
        await Scalar(ro, "select count(full_name) from guardians");
        foreach (var sql in new[]
                 {
                     "select password_hash from users limit 1", "select initial_password from users limit 1",
                     "select email from users limit 1", "select * from users limit 1",
                     "select telegram_bot_token from school_meta", "select fcm_service_account_json from school_meta",
                     "select turnstile_password from school_meta", "select gps_ingest_token from school_meta",
                     "select passport_url from guardians", "select count(*) from device_tokens",
                     "select count(*) from telegram_link_codes", "select count(*) from exam_invitations",
                     "select count(*) from questions", "select count(*) from question_options",
                     "select count(*) from audit_logs", "select count(*) from chat_messages",
                     "select count(*) from cameras", "select count(*) from mcp_tokens", "select count(*) from mcp_auth_codes",
                 })
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => Scalar(ro, sql));
            Assert.Equal("42501", ex.SqlState);
        }

        // A NEW table is closed by default (no default privileges for app_ro).
        var table = "ro_new_" + Guid.NewGuid().ToString("N")[..8];
        await using (var owner = new NpgsqlConnection(fixture.Database.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await using var cmd = new NpgsqlCommand($"create table {table} (id int)", owner);
            await cmd.ExecuteNonQueryAsync();
        }
        var closed = await Assert.ThrowsAsync<PostgresException>(() => Scalar(ro, $"select count(*) from {table}"));
        Assert.Equal("42501", closed.SqlState);
        await using (var owner = new NpgsqlConnection(fixture.Database.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await using var cmd = new NpgsqlCommand($"drop table {table}", owner);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Faqat_SELECT_buyruqlari_va_ozini_tekshirish()
    {
        Assert.True(ReadOnlyDatabase.SelectOnly.IsSelect("SELECT 1"));
        Assert.True(ReadOnlyDatabase.SelectOnly.IsSelect("-- tag\n  select * from x"));
        Assert.True(ReadOnlyDatabase.SelectOnly.IsSelect("WITH a AS (SELECT 1) SELECT * FROM a"));
        Assert.False(ReadOnlyDatabase.SelectOnly.IsSelect("WITH a AS (DELETE FROM x RETURNING 1) SELECT * FROM a"));
        Assert.False(ReadOnlyDatabase.SelectOnly.IsSelect("DELETE FROM students"));
        Assert.False(ReadOnlyDatabase.SelectOnly.IsSelect("UPDATE students SET x = 1"));
        Assert.False(ReadOnlyDatabase.SelectOnly.IsSelect("set role app_rw"));

        await using var ctx = new ReadOnlyDatabase(fixture.Database.AppRoConnectionString).CreateContext();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.Database.ExecuteSqlRawAsync("delete from students where id = 'x'"));
        Assert.Equal(ReadOnlyDatabase.SelectOnly.Message, ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.Students.Where(s => s.Id == "x").ExecuteDeleteAsync());

        var hardened = McpReadOnlyConnection.Harden(fixture.Database.AppRoConnectionString);
        var b = new NpgsqlConnectionStringBuilder(hardened);
        Assert.True(b.MaxPoolSize <= 8);
        Assert.False(b.IncludeErrorDetail);
        Assert.Contains("default_transaction_read_only=on", b.Options);
        Assert.Null(McpReadOnlyConnection.SelfTest(hardened));
        Assert.NotNull(McpReadOnlyConnection.SelfTest(fixture.Database.AppRwConnectionString));
        Assert.NotNull(McpReadOnlyConnection.SelfTest(fixture.Database.OwnerConnectionString));
    }

    [Fact]
    public void Matn_tozalash()
    {
        Assert.Equal("Claude AI", McpText.Sanitize("  Cla​ude‮ \n AI\u0000 ", 60));
        Assert.Equal(60, McpText.Sanitize(new string('a', 100), 60).Length);
        foreach (var n in new[] { "fcmToken", "vapidKey", "pinfl", "jshshir", "otpCode", "passwordSalt", "sessionId", "userEmail", "lastLoginAt" })
            Assert.True(McpToolContext.IsSecretName(n), n);
        Assert.False(McpToolContext.IsSecretName("session")); // boarding session name is data
    }

    private static async Task<object?> Scalar(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        return await cmd.ExecuteScalarAsync();
    }
}
