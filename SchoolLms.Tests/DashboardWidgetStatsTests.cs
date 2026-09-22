using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Bosh sahifaning "Vidjetlar" panelidagi yangi hisoblar (2026-09-22):
/// aktiv, sinfdan chiqarilgan, kutayotgan, shu oyda birinchi to'lov, jins va
/// sinflar kesimi.
///
/// <para>
/// Eng oson xato — sinfdan KO'CHIRILGAN bolani "sinfdan chiqarilgan" deb
/// sanash: uning ham yopilgan a'zoligi bor. Shu sabab seed'da ko'chirilgan
/// bola ALOHIDA bor va u sanalmasligi tekshiriladi.
/// </para>
/// <para>O'z bazasi: raqamlar butun maktab bo'yicha, umumiy bazada qo'shni
/// testlarning o'quvchilari ularni siljitardi.</para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class DashboardWidgetStatsTests(ApiFixture fixture) : IAsyncLifetime
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
    public async Task Yangi_hisoblar_seedga_mos_keladi()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("dash_widgets");
        _connectionStrings.Add(database.OwnerConnectionString);
        await using var db = PostgresFixture.NewContext(database.OwnerConnectionString);

        var classA = new SchoolClass { Name = "5-A", Grade = 5, Capacity = 25 };
        var classB = new SchoolClass { Name = "6-A", Grade = 6 };
        db.Classes.AddRange(classA, classB);

        var inClass = NewStudent("Ali Aliyev", "5-A", "male");
        var girl = NewStudent("Zuhra Karimova", "5-A", "female");
        var moved = NewStudent("Botir Botirov", "6-A", "male");     // 5-A → 6-A: ko'chirildi
        var removed = NewStudent("Dilnoza Dilova", "", "female");   // 5-A dan chiqarildi
        var waiting = NewStudent("Eldor Eldorov", "", "male");      // qabul qilingan, sinf yo'q
        waiting.TargetGrade = 1;
        var bare = NewStudent("Farruh Farruxov", "", "male");       // sinfsiz, tarixsiz
        var archived = NewStudent("Gulnora Gulova", "", "female");
        archived.IsArchived = true;
        db.Students.AddRange(inClass, girl, moved, removed, waiting, bare, archived);

        var joined = new DateOnly(2025, 9, 1);
        var left = new DateOnly(2026, 1, 10);
        db.ClassMemberships.AddRange(
            new ClassMembership { StudentId = inClass.Id, ClassId = classA.Id, JoinedOn = joined },
            new ClassMembership { StudentId = girl.Id, ClassId = classA.Id, JoinedOn = joined },
            new ClassMembership { StudentId = moved.Id, ClassId = classA.Id, JoinedOn = joined, LeftOn = left, LeaveReason = "transfer" },
            new ClassMembership { StudentId = moved.Id, ClassId = classB.Id, JoinedOn = left },
            new ClassMembership { StudentId = removed.Id, ClassId = classA.Id, JoinedOn = joined, LeftOn = left, LeaveReason = "removed" });

        var cashier = new AppUser { FullName = "Kassir", Role = Roles.Admin, Email = $"dash.{Guid.NewGuid():N}" };
        cashier.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(cashier);
        await db.SaveChangesAsync();

        var today = AppClock.Today;
        var thisMonth = AppClock.InstantOn(new DateOnly(today.Year, today.Month, 1)).AddHours(10);
        var lastYear = AppClock.InstantOn(today.AddYears(-1));
        long receipt = 1;
        Payment Pay(string studentId, DateTimeOffset at, Guid? reversalOf = null) => new()
        {
            ReceiptNo = receipt++, StudentId = studentId, Amount = 100_000m, Method = PaymentMethod.Cash,
            CashierId = cashier.Id, ReceivedAt = at, ReversalOf = reversalOf,
        };
        var old = Pay(inClass.Id, lastYear);                 // birinchisi — o'tgan yil
        db.Payments.AddRange(old, Pay(inClass.Id, thisMonth), Pay(girl.Id, thisMonth));
        await db.SaveChangesAsync();
        // Storno qatori "to'lov" emas — u birinchi to'lovni bu oyga ko'chirmaydi.
        db.Payments.Add(Pay(inClass.Id, thisMonth, reversalOf: old.Id));
        await db.SaveChangesAsync();

        var result = await new DashboardController(db).Get();
        var dto = Assert.IsType<AdminDashboardDto>(result.Value);
        var s = dto.Stats;

        Assert.Equal(6, s.StudentsCount);
        Assert.Equal(3, s.UnassignedCount);
        Assert.Equal(3, s.ActiveCount);
        Assert.Equal(1, s.LeftFromClassCount);   // faqat Dilnoza; Botir ko'chirilgan
        Assert.Equal(1, s.WaitingCount);         // faqat Eldor
        Assert.Equal(1, s.FirstPaymentThisMonthCount); // Zuhra; Ali birinchi marta o'tgan yil to'lagan
        Assert.Equal(4, s.MaleCount);
        Assert.Equal(2, s.FemaleCount);

        var heads = dto.ClassHeadcounts!;
        var a = heads.Single(h => h.ClassName == "5-A");
        Assert.Equal((2, 1, 1, 25), (a.StudentsCount, a.MaleCount, a.FemaleCount, a.Capacity!.Value));
        Assert.Equal(1, heads.Single(h => h.ClassName == "6-A").StudentsCount);
    }

    private static Student NewStudent(string fullName, string className, string gender) => new()
    {
        FullName = fullName,
        LastName = fullName.Split(' ')[^1],
        FirstName = fullName.Split(' ')[0],
        BirthDate = "2014-01-01",
        Gender = gender,
        ClassName = className,
        EnrollmentDate = "2025-09-01",
    };
}
