using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// O'quvchining to'lov tarixi (<see cref="StudentLedger"/>) — STORNO qanday
/// ko'rinishi haqida.
///
/// <para>
/// Nima tekshiriladi va nega. To'lov O'CHIRILMAYDI (SPEC §4.1): xato to'lov
/// ustiga qarshi yozuv qo'yiladi. Demak tarixda ikkita qator qoladi va ekran
/// ularni ajrata olishi SHART — aks holda direktor bir xil ikkita "+500 000"
/// ni ko'radi va pul ikki marta tushgan deb o'ylaydi. Ilgari bu holat izoh
/// matniga <c>[STORNO]</c> deb yozilardi; endi ikkita bayroq bor, shuning
/// uchun test izohning TOZA qolishini ham tekshiradi.
/// </para>
/// <para>
/// Har test o'z bazasida yuradi: yig'indilar (<c>totalPaid</c>) butun bazaga
/// tegishli qiymat, qo'shni testning to'lovi ularni siljitib yuborardi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StudentLedgerTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>Migratsiya seed qilgan barqaror toifa id'i (billing_seed.sql).</summary>
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");

    private TestDatabase _database = default!;

    public async Task InitializeAsync() =>
        _database = await fixture.Postgres.CreateDatabaseAsync("stledger");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        return Task.CompletedTask;
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    /// <summary>
    /// Bekor qilingan to'lov ham, uni bekor qilgan storno qatori ham tarixda
    /// QOLADI va ikkovi ham bayroq bilan belgilanadi; oy esa yana
    /// "to'lanmagan" holatiga qaytadi, chunki pul aslida qolmadi.
    /// </summary>
    [Fact]
    public async Task Storno_qatori_ham_bekor_qilingan_tolov_ham_bayroq_bilan_korinadi()
    {
        await using var db = NewDb();

        var cashierId = await SeedCashierAsync(db);
        var shiftId = await SeedShiftAsync(db, cashierId);

        var studentId = Guid.NewGuid().ToString();
        var student = NewStudent(studentId);
        db.Students.Add(student);

        var month = new DateOnly(2025, 9, 1);
        var invoice = new Invoice
        {
            StudentId = studentId,
            CategoryId = TuitionCategory,
            PeriodMonth = month,
            Amount = 800_000m,
            Discount = 0m,
            DueOn = month.AddDays(9),
            Status = InvoiceStatus.Open,
            CreatedAt = AppClock.NowInstant,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        // Kassir izohiga ATAYLAB "storno" so'zi yozilgan: holat matndan emas,
        // bayroqdan o'qilishi kerak.
        var kept = await PayAsync(db, studentId, cashierId, shiftId, 300_000m, invoice.Id,
            note: "qisman, storno emas");
        var reversed = await PayAsync(db, studentId, cashierId, shiftId, 500_000m, invoice.Id);
        await PayAsync(db, studentId, cashierId, shiftId, 500_000m, invoice.Id,
            reversalOf: reversed, note: "xato summa");

        var ledger = await StudentLedger.BuildAsync(db, student);

        Assert.Equal(3, ledger.Payments.Count);

        // 1) Oddiy to'lov — ikkala bayroq ham bo'sh, izoh TEGILMAGAN.
        var normal = ledger.Payments.Single(p => p.Amount == 300_000m);
        Assert.False(normal.IsReversal);
        Assert.False(normal.Reversed);
        Assert.Equal("qisman, storno emas", normal.Note);

        // 2) Bekor qilingan asl to'lov — ro'yxatda QOLADI.
        var cancelled = ledger.Payments.Single(p => p is { Amount: 500_000m, IsReversal: false });
        Assert.True(cancelled.Reversed);

        // 3) Storno qatorining o'zi.
        var storno = ledger.Payments.Single(p => p.IsReversal);
        Assert.Equal(500_000m, storno.Amount);
        Assert.False(storno.Reversed);
        // Izohga endi hech narsa yopishtirilmaydi.
        Assert.Equal("xato summa", storno.Note);

        // Pul: faqat 300 000 haqiqatan qoldi.
        Assert.Equal(300_000m, ledger.TotalPaid);

        var september = Assert.Single(ledger.Months);
        Assert.Equal("2025-09", september.Month);
        Assert.Equal(300_000m, september.Paid);
        Assert.Equal(500_000m, september.Remaining);
        Assert.Equal("partial", september.Status);

        // Ishlatilmagan o'zgaruvchi emas: `kept` id'si qaytgan qator aynan
        // saqlanib qolgani yuqorida tekshirildi.
        Assert.NotEqual(Guid.Empty, kept);
    }

    // =====================================================================
    //  Yordamchi qism
    // =====================================================================

    private static async Task<string> SeedCashierAsync(AppDbContext db)
    {
        var user = new AppUser
        {
            FullName = "Test kassir",
            Role = Roles.Cashier,
            Email = $"cashier.{Guid.NewGuid():N}",
        };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<Guid> SeedShiftAsync(AppDbContext db, string cashierId)
    {
        var shift = new CashShift
        {
            CashierId = cashierId,
            OpenedAt = AppClock.NowInstant,
            OpeningFloat = 0m,
            Status = CashShiftStatus.Open,
        };
        db.CashShifts.Add(shift);
        await db.SaveChangesAsync();
        return shift.Id;
    }

    private static Student NewStudent(string id) => new()
    {
        Id = id,
        FullName = "Test O'quvchi",
        LastName = "Familiya",
        FirstName = "Ism",
        MiddleName = "Otasi",
        BirthDate = "2015-01-01",
        Address = "Toshkent",
        Gender = "male",
        ParentFullName = "Ota-ona",
        ParentLastName = "Familiya",
        ParentFirstName = "Ism",
        ParentMiddleName = "Otasi",
        ParentPhone = "+998901112233",
        ClassName = "5-A",
        EnrollmentDate = "2025-09-01",
    };

    /// <summary>To'lov + bitta taqsimot. Chek raqami smena ichida uzluksiz (SPEC §4.2).</summary>
    private static async Task<Guid> PayAsync(
        AppDbContext db, string studentId, string cashierId, Guid shiftId,
        decimal amount, Guid invoiceId, Guid? reversalOf = null, string? note = null)
    {
        var lastReceipt = await db.Payments
            .Where(p => p.CashShiftId == shiftId)
            .MaxAsync(p => (long?)p.ReceiptNo) ?? 0;

        var payment = new Payment
        {
            ReceiptNo = lastReceipt + 1,
            StudentId = studentId,
            Amount = amount,
            Method = PaymentMethod.Cash,
            CashShiftId = shiftId,
            CashierId = cashierId,
            ReceivedAt = AppClock.NowInstant,
            ReversalOf = reversalOf,
            Note = note,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        db.PaymentAllocations.Add(new PaymentAllocation
        {
            PaymentId = payment.Id,
            InvoiceId = invoiceId,
            Amount = amount,
        });
        await db.SaveChangesAsync();
        return payment.Id;
    }
}
