using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SchoolLms.Application.HrTasks;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;
using static SchoolLms.Tests.HrTasks.HrTasksKit;

namespace SchoolLms.Tests.HrTasks;

/// <summary>
/// Every LMS employee is an HR employee (client, 2026-09-28): teachers and staff land in the HR
/// list so they can get tasks and be placed in the subordination tree.
///
/// <para>
/// The sync reads the WHOLE users table, so its tests run on their OWN database — on the shared
/// one it would turn every other test's user into an HR employee.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class HrPeopleSyncTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Faol_oqituvchi_va_xodimlar_royxatga_tushadi_qolganlar_yoq()
    {
        await using var db = await NewDbAsync();
        var teacher = User("O'qituvchi Bir", Roles.Teacher);
        var leaver = User("Ketgan O'qituvchi", Roles.Teacher);
        var nurse = User("Hamshira Opa", Roles.Staff, position: "Hamshira", phone: "+998 90 111 22 33");
        var twin = User("Yana Shu Raqam", Roles.Staff, phone: "+998901112233");
        var system = User("Tizim administratori", Roles.SuperAdmin);
        var pupil = User("O'quvchi Bola", Roles.Student);
        var already = User("Eski Xodim", Roles.Staff);
        var phoneless = User("Telefonsiz Eski", Roles.Staff, position: "Buxgalter", phone: "+998 93 444 55 66");
        db.Users.AddRange(teacher, leaver, nurse, twin, system, pupil, already, phoneless);
        db.Teachers.AddRange(
            new Teacher { FullName = teacher.FullName, UserId = teacher.Id, Phone = "" },
            new Teacher { FullName = leaver.FullName, UserId = leaver.Id, Phone = "+998907770000", IsArchived = true });
        // Imported from the old HR: a short name and its own phone / no phone at all.
        db.HrEmployees.AddRange(
            new HrEmployee { UserId = already.Id, FullName = "Eski", Phone = "+998905550000" },
            new HrEmployee { UserId = phoneless.Id, FullName = phoneless.FullName });
        await db.SaveChangesAsync();

        var sync = await Service(db).SyncLmsPeopleAsync();

        Assert.Equal(new HrPeopleSyncResult(Added: 3, Updated: 2), sync);
        var rows = await db.HrEmployees.AsNoTracking().ToListAsync();
        var positions = await db.HrPositions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name);
        HrEmployee Row(AppUser u) => rows.Single(e => e.UserId == u.Id);

        // A teacher without a phone is still added — the phone is only a label.
        Assert.Equal(HrTasksService.TeacherPositionName, positions[Row(teacher).PositionId!]);
        Assert.Null(Row(teacher).Phone);
        Assert.Equal(HrRoles.Employee, Row(teacher).HrRole);
        Assert.True(Row(teacher).IsActive);

        // Staff keep their LMS "Lavozim"; the second holder of a number gets none, not an error.
        Assert.Equal("Hamshira", positions[Row(nurse).PositionId!]);
        Assert.Equal("+998901112233", Row(nurse).Phone);
        Assert.Null(Row(twin).Phone);
        Assert.Equal(HrTasksService.DefaultPositionName, positions[Row(twin).PositionId!]);

        Assert.DoesNotContain(rows, e => e.UserId == leaver.Id || e.UserId == system.Id || e.UserId == pupil.Id);
        // People are managed in Boshqaruv: the LMS name wins, an HR phone is kept, a missing one filled.
        Assert.Equal("Eski Xodim", Row(already).FullName);
        Assert.Equal("+998905550000", Row(already).Phone);
        Assert.Equal("+998934445566", Row(phoneless).Phone);
        // Positions are Boshqaruv's too: its "Lavozim" wins, an empty one leaves HR's alone.
        Assert.Equal("Buxgalter", positions[Row(phoneless).PositionId!]);
        Assert.Null(Row(already).PositionId);
        Assert.Empty(await db.HrEmployeeManagers.ToListAsync()); // nobody is placed in the tree

        Assert.Equal(new HrPeopleSyncResult(0, 0), await Service(db).SyncLmsPeopleAsync()); // idempotent
    }

    [Fact]
    public async Task Tarixsiz_HR_qatori_oqituvchini_ochirishga_xalaqit_bermaydi()
    {
        var (teacherId, userId) = await SeedTeacherWithHrRowAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.DeleteAsync($"/api/admin/teachers/{teacherId}");

        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        await fixture.Api.WithDbAsync(async db =>
        {
            Assert.False(await db.HrEmployees.AnyAsync(e => e.UserId == userId));
            Assert.False(await db.Users.AnyAsync(u => u.Id == userId));
        });
    }

    [Fact]
    public async Task Topshirigi_bor_HR_xodimi_ochirilmaydi_409()
    {
        var (teacherId, userId) = await SeedTeacherWithHrRowAsync();
        await fixture.Api.WithDbAsync(async db =>
        {
            var employee = await db.HrEmployees.SingleAsync(e => e.UserId == userId);
            db.HrTasks.Add(new HrTask
            {
                EmployeeId = employee.Id, Title = "Tarix", Source = HrTaskSources.Director, Kind = HrTaskKinds.Check,
                DueAt = DateTimeOffset.UtcNow, PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
                Status = HrTaskStatuses.Open, OverduePolicy = HrOverduePolicies.KeepOpen,
            });
            await db.SaveChangesAsync();
        });
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.DeleteAsync($"/api/admin/teachers/{teacherId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await fixture.Api.WithDbAsync(async db => Assert.True(await db.HrEmployees.AnyAsync(e => e.UserId == userId)));
    }

    [Fact]
    public async Task Telefonsiz_mavjud_foydalanuvchidan_xodim_ochiladi()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var created = await admin.PostAsJsonAsync($"{Panel}/employees", new { userId = user.Id });

        var text = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, $"{(int)created.StatusCode}: {text}");
        using var body = JsonDocument.Parse(text);
        Assert.Equal("", body.RootElement.GetProperty("phone").GetString());

        // A brand-new user still needs a phone — it is the login.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, // HrErrors.Validation → 422
            (await admin.PostAsJsonAsync($"{Panel}/employees", new { fullName = "Telefonsiz Yangi" })).StatusCode);
    }

    // ---------------------------------------------------------------- yordamchilar

    private async Task<(string TeacherId, string UserId)> SeedTeacherWithHrRowAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var teacher = new Teacher { FullName = user.FullName, UserId = user.Id };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Teachers.Add(teacher);
            db.HrEmployees.Add(new HrEmployee { UserId = user.Id, FullName = user.FullName });
            await db.SaveChangesAsync();
        });
        return (teacher.Id, user.Id);
    }

    private static AppUser User(string name, string role, string position = "", string phone = "") => new()
    {
        FullName = name,
        Role = role,
        Email = $"sync-{Guid.NewGuid():N}",
        PasswordHash = "x",
        Position = position,
        Phone = phone,
    };

    private static HrTasksService Service(AppDbContext db) =>
        new(db, new FakeHrSender(), new MemoryHrFileStore(), NullLogger<HrTasksService>.Instance);

    private async Task<AppDbContext> NewDbAsync()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("hr_people_sync");
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }
}
