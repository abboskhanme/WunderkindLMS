using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// F14.01 — <c>BillingSettingsService.UpdateAsync</c> va oylik hisoblash
/// (<c>InvoiceService.AccrueMonthAsync</c>) o'rtasidagi "oldinga qarab,
/// orqaga emas" shartnomasi.
///
/// <para>
/// <b>Nega o'z bazasida.</b> <c>AccrueMonthAsync</c> BUTUN bazani skanerlaydi
/// (har faol obuna uchun) — umumiy testlar bazasida "nechta hisob-faktura
/// yozildi" savoli boshqa vazifalarning o'quvchilariga bog'liq bo'lib qolardi.
/// Xuddi shu sabab bilan <c>InvoiceServiceTests</c> ham izolyatsiya qilingan
/// baza ishlatadi (o'sha fayl boshidagi izoh) — bu yerda AYNAN o'sha naqsh
/// takrorlanadi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class BillingSettingsAccrualTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>Migratsiya seed qilgan barqaror toifa id'i (billing_seed.sql).</summary>
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");

    private static readonly DateOnly ThisMonth = new(AppClock.Today.Year, AppClock.Today.Month, 1);

    private TestDatabase _database = default!;

    public async Task InitializeAsync() => _database = await fixture.Postgres.CreateDatabaseAsync("billingset");

    /// <summary>
    /// Konteynerning o'zi test yurishi oxirida butunlay o'chadi
    /// (<c>PostgresFixture.DisposeAsync</c>), shuning uchun bu bazani alohida
    /// o'chirish shart emas — faqat ulanish pool'ini bo'shatamiz, keyingi
    /// klass o'zining bazasiga toza pool bilan ulansin.
    /// </summary>
    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        return Task.CompletedTask;
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    private static InvoiceService InvoiceServiceFor(AppDbContext db) => new(db, new LedgerService(db));

    private static BillingSettingsService SettingsServiceFor(AppDbContext db) =>
        new(db, new AuditService(db, new Microsoft.AspNetCore.Http.HttpContextAccessor()));

    private static async Task<string> AddUserAsync(AppDbContext db, string role)
    {
        var user = new AppUser
        {
            FullName = $"F14.01 {role}",
            Role = role,
            Email = $"{role}.f1401.{Guid.NewGuid():N}",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<string> AddStudentAsync(AppDbContext db)
    {
        var student = new Student
        {
            FullName = "O'quvchi " + Guid.NewGuid().ToString("N")[..8],
            ClassName = "1-A",
            EnrollmentDate = "2026-01-01",
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return student.Id;
    }

    /// <summary>
    /// `payment_due_day` o'zgarganda ALLAQACHON hisoblangan hisob-faktura o'z
    /// eski to'lov muddatini saqlaydi; faqat KEYINGI oy yangi kunni oladi.
    /// `InvoiceService.AccrueMonthAsync` `due_on` ni hisoblash PAYTIDA yozadi
    /// va keyin unga qaytmaydi — bu invariant `InvoiceServiceTests` da qo'lda
    /// o'zgartirilgan sozlama bilan allaqachon sinalgan; bu yerda esa AYNAN
    /// haqiqiy <c>BillingSettingsService</c> orqali (topshiriqning o'zi:
    /// "a settings change moves money forward only").
    /// </summary>
    [Fact]
    public async Task Tolov_kuni_ozgarganda_eski_hisobfaktura_tegilmaydi()
    {
        await using var seedDb = NewDb();
        var actorId = await AddUserAsync(seedDb, Roles.Admin);
        var directorId = await AddUserAsync(seedDb, Roles.SuperAdmin);
        var studentId = await AddStudentAsync(seedDb);

        seedDb.StudentSubscriptions.Add(new StudentSubscription
        {
            StudentId = studentId,
            CategoryId = TuitionCategory,
            MonthlyAmount = 1_000_000m,
            StartsOn = ThisMonth,
            CreatedBy = actorId,
            CreatedAt = AppClock.NowInstant,
        });
        await seedDb.SaveChangesAsync();

        var original = await seedDb.BillingSettings.AsNoTracking().FirstAsync();
        var oldDueDay = original.PaymentDueDay;
        var newDueDay = oldDueDay >= 15 ? oldDueDay - 5 : oldDueDay + 5;

        // 1) Joriy oy — ESKI kun bilan hisoblanadi.
        await using (var accrue1 = NewDb())
            await InvoiceServiceFor(accrue1).AccrueMonthAsync(ThisMonth, actorId);

        await using (var check1 = NewDb())
        {
            var firstInvoice = await check1.Invoices.AsNoTracking()
                .SingleAsync(i => i.StudentId == studentId && i.PeriodMonth == ThisMonth);
            Assert.Equal(oldDueDay, firstInvoice.DueOn.Day);
        }

        // 2) Sozlama o'zgaradi — YANGI xizmat orqali (F14.01), qo'lda DbContext emas.
        await using (var settingsDb = NewDb())
            await SettingsServiceFor(settingsDb).UpdateAsync(
                new UpdateBillingSettingsRequest(newDueDay, original.OverdueAfterDay, original.ExpenseApprovalThreshold),
                directorId, actorIsDirector: true);

        // 3) Eski hisob-faktura TEGILMAGAN.
        await using (var check2 = NewDb())
        {
            var stillOld = await check2.Invoices.AsNoTracking()
                .SingleAsync(i => i.StudentId == studentId && i.PeriodMonth == ThisMonth);
            Assert.Equal(oldDueDay, stillOld.DueOn.Day);
        }

        // 4) Keyingi oy YANGI kun bilan hisoblanadi.
        var nextMonth = ThisMonth.AddMonths(1);
        await using (var accrue2 = NewDb())
            await InvoiceServiceFor(accrue2).AccrueMonthAsync(nextMonth, actorId);

        await using (var check3 = NewDb())
        {
            var nextInvoice = await check3.Invoices.AsNoTracking()
                .SingleAsync(i => i.StudentId == studentId && i.PeriodMonth == nextMonth);
            Assert.Equal(newDueDay, nextInvoice.DueOn.Day);
        }
    }
}
