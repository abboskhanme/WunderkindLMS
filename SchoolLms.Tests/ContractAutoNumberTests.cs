using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Hammaga ketma-ket shartnoma raqami (client, 2026-10-29: "1a sinfidagi ro'yxatdagi birinchi o'quvchidan
//  (WK-1) 11b sinfidagi oxirgi o'quvchigacha (WK-467)"). It numbers EVERY active pupil, so each test runs
//  on its own fresh database — the shared one belongs to the other tests.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class ContractAutoNumberTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<AppDbContext> _contexts = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var db in _contexts) await db.DisposeAsync();
    }

    [Fact]
    public async Task Reja_sinf_tartibida_va_hech_narsa_yozmaydi()
    {
        var (db, user) = await NewDbAsync("plan");
        await SeedAsync(db);

        var plan = Value(await Controller(db, user).AutoNumber(new AutoNumberContractsRequest("WK-", 1)));

        Assert.False(plan.Applied);
        // 1-A, 1-B, 2-A, 11-B — classes by grade then name; inside a class by name; the archived pupil
        // and the pupil of an archived class are left out.
        Assert.Equal(
            ["Aliyev Ali", "Boboyev Bobur", "Valiyev Vali", "Doniyorov Doniyor", "Eshonov Eshon"],
            plan.Rows.Select(r => r.StudentName).ToArray());
        Assert.Equal(["WK-1", "WK-2", "WK-3", "WK-4", "WK-5"], plan.Rows.Select(r => r.Number!).ToArray());
        Assert.Equal(("WK-1", "WK-5"), (plan.FirstNumber, plan.LastNumber));
        Assert.Equal(1, plan.WithoutClass);
        Assert.Empty(await db.StudentContracts.ToListAsync());
    }

    [Fact]
    public async Task Qollaydi_raqamli_tegilmaydi_qoralamaga_raqam_qoyiladi_keyingisi_davom_etadi()
    {
        var (db, user) = await NewDbAsync("apply");
        var w = await SeedAsync(db);
        db.StudentContracts.AddRange(
            new StudentContract { StudentId = w.Vali, Number = "A-77", Source = StudentContractSource.Uploaded, CreatedBy = user, CreatedAt = AppClock.NowInstant },
            new StudentContract { StudentId = w.Bobur, Number = null, Source = StudentContractSource.Uploaded, CreatedBy = user, CreatedAt = AppClock.NowInstant });
        await db.SaveChangesAsync();

        var res = Value(await Controller(db, user).AutoNumber(new AutoNumberContractsRequest("WK-", 1, Apply: true)));

        Assert.True(res.Applied);
        Assert.Equal(4, res.Assigned);
        Assert.Equal(1, res.Skipped);
        db.ChangeTracker.Clear();
        var numbers = await db.StudentContracts.ToDictionaryAsync(c => c.StudentId, c => c.Number);
        Assert.Equal("WK-1", numbers[w.Ali]);
        Assert.Equal("WK-2", numbers[w.Bobur]);          // the draft got the number, no second row
        Assert.Equal("A-77", numbers[w.Vali]);           // untouched, takes no number
        Assert.Equal("WK-3", numbers[w.Doniyor]);
        Assert.Equal("WK-4", numbers[w.Eshon]);
        Assert.Equal(5, await db.StudentContracts.CountAsync());

        // Running it again changes nothing: everybody has a number now.
        var again = Value(await Controller(db, user).AutoNumber(new AutoNumberContractsRequest("WK-", 1, Apply: true)));
        Assert.Equal(0, again.Assigned);

        // A new pupil's automatic number continues the WK- sequence.
        var preview = await Controller(db, user).Preview(w.Ali);
        Assert.Equal("WK-5", preview.Value!.NextNumber);
    }

    [Fact]
    public async Task Band_raqam_bolsa_409_va_hech_narsa_yozilmaydi()
    {
        var (db, user) = await NewDbAsync("taken");
        var w = await SeedAsync(db);
        var outsider = GeneralSettingsFlagsTests.NewStudent("Sinfsiz Bola", "", "+998900000009");
        outsider.Id = Guid.NewGuid().ToString();
        db.Students.Add(outsider);
        await db.SaveChangesAsync();
        db.StudentContracts.Add(new StudentContract
        {
            StudentId = outsider.Id, Number = "WK-3", Source = StudentContractSource.Uploaded,
            CreatedBy = user, CreatedAt = AppClock.NowInstant,
        });
        await db.SaveChangesAsync();

        var res = await Controller(db, user).AutoNumber(new AutoNumberContractsRequest("WK-", 1, Apply: true));

        Assert.IsType<ConflictObjectResult>(res.Result);
        Assert.Equal(1, await db.StudentContracts.CountAsync());
        Assert.DoesNotContain(await db.StudentContracts.Select(c => c.StudentId).ToListAsync(), id => id == w.Ali);
    }

    [Fact]
    public async Task Notogri_prefiks_va_boshlangich_400()
    {
        var (db, user) = await NewDbAsync("bad");
        Assert.IsType<BadRequestObjectResult>((await Controller(db, user).AutoNumber(new("WK1", 1))).Result);
        Assert.IsType<BadRequestObjectResult>((await Controller(db, user).AutoNumber(new("WK-", 0))).Result);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private sealed record World(string Ali, string Bobur, string Vali, string Doniyor, string Eshon);

    private static async Task<World> SeedAsync(AppDbContext db)
    {
        db.Classes.AddRange(
            new SchoolClass { Name = "11-B", Grade = 11 },
            new SchoolClass { Name = "2-A", Grade = 2 },
            new SchoolClass { Name = "1-B", Grade = 1 },
            new SchoolClass { Name = "1-A", Grade = 1 },
            new SchoolClass { Name = "3-A", Grade = 3, IsArchived = true });

        var ali = Pupil("Aliyev Ali", "1-A");
        var bobur = Pupil("Boboyev Bobur", "1-A");
        var vali = Pupil("Valiyev Vali", "1-B");
        var doniyor = Pupil("Doniyorov Doniyor", "2-A");
        var eshon = Pupil("Eshonov Eshon", "11-B");
        var archived = Pupil("Arxivov Arxiv", "1-A");
        archived.IsArchived = true;
        var oldClass = Pupil("Eskiyev Eski", "3-A");
        // Added out of order on purpose — the order must come from class + name, not insertion.
        db.Students.AddRange(eshon, doniyor, vali, bobur, ali, archived, oldClass);
        await db.SaveChangesAsync();
        return new World(ali.Id, bobur.Id, vali.Id, doniyor.Id, eshon.Id);
    }

    private static Student Pupil(string fullName, string className)
    {
        var s = GeneralSettingsFlagsTests.NewStudent(fullName, className, "+998901112233");
        s.Id = Guid.NewGuid().ToString();
        return s;
    }

    private async Task<(AppDbContext Db, string UserId)> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("autonum_" + prefix);
        var db = PostgresFixture.NewContext(database.OwnerConnectionString);
        _contexts.Add(db);
        var user = new AppUser { FullName = "Admin " + prefix, Role = Roles.Admin, Email = $"admin.{prefix}.{Guid.NewGuid():N}" };
        user.SetInitialPassword("Test-" + prefix);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (db, user.Id);
    }

    private static StudentContractsController Controller(AppDbContext db, string userId)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test")),
        };
        return new StudentContractsController(
            db, new ContractService(null!, db), new AuditService(db, new HttpContextAccessor { HttpContext = http }))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static AutoNumberContractsResultDto Value(ActionResult<AutoNumberContractsResultDto> res) =>
        res.Value ?? (AutoNumberContractsResultDto)((ObjectResult)res.Result!).Value!;
}
