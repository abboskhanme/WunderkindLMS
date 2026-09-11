using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Kassa smenasi, uzluksiz chek raqami va Z-hisobot (P1-10, SPEC §4.2 va §4.6).
///
/// <para>
/// Bu yerdagi eng muhim test —
/// <see cref="Chek_raqami_50_ta_parallel_chaqiruvda_boshliqsiz_va_takrorlanmas"/>.
/// Auditor uchun chek raqamidagi BO'SHLIQ = o'chirilgan chek degani, ya'ni
/// aynan shu modul qarshi turishi kerak bo'lgan ayblov. Qolgan testlar
/// chegaralarni tekshiradi: o'zganing smenasi, sanalmagan naqd, tahrirlab
/// bo'lmaydigan nomuvofiqlik.
/// </para>
/// <para>
/// Baza OWNER ulanishi bilan ochiladi (<c>LedgerServiceTests</c> dagi sabab):
/// test ma'lumotini tayyorlash uchun <c>users</c>/<c>students</c> ga yozish
/// kerak. <c>app_rw</c> ning huquqlari P1-22 va
/// <c>tools/verify-billing-guards.sh</c> da alohida tekshiriladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class CashShiftServiceTests(ApiFixture fixture)
{
    /// <summary>Migratsiyada seed qilingan barqaror toifa id'si (billing_seed.sql).</summary>
    private static readonly Guid TuitionCategoryId = new("00000000-0000-0000-0000-0000000000c1");

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  Chek raqami — P1-10 ning asosiy mezoni
    // =====================================================================

    /// <summary>
    /// SPEC §4.2: chek raqami smena ichida UZLUKSIZ. 50 ta parallel chaqiruv
    /// AYNAN 1..50 raqamlarini berishi kerak — bittasi ham tushib qolmasin,
    /// bittasi ham takrorlanmasin.
    ///
    /// <para>
    /// Test ALOHIDA bazada yuradi: umumiy test bazasi ulanishi
    /// <c>MaxPoolSize = 10</c> bilan quriladi, ellikta chaqiruv esa unda
    /// navbatga tushib, haqiqiy parallellikni (va demak poygani) yo'q qilardi —
    /// test yashil bo'lardi, lekin hech narsani isbotlamasdi.
    /// </para>
    /// <para>
    /// Har chaqiruv O'Z ulanishida va O'Z tranzaksiyasida: raqam olish va
    /// <c>payments</c> ga yozish bitta tranzaksiyada bo'lishi
    /// <see cref="ICashShiftService.NextReceiptNoAsync"/> ning shartnomasi.
    /// Qulf olib tashlansa, ellikta tranzaksiya bir xil <c>max + 1</c> ni o'qiydi
    /// va <c>unique (cash_shift_id, receipt_no)</c> indeksi ularni yiqitadi —
    /// ya'ni bu test regressiyani ko'rsatmay o'tkazib yubora olmaydi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Chek_raqami_50_ta_parallel_chaqiruvda_boshliqsiz_va_takrorlanmas()
    {
        const int callers = 50;

        var database = await fixture.Postgres.CreateDatabaseAsync("receipts");
        var connectionString = WithPoolSize(database.OwnerConnectionString, callers + 5);

        string cashierId, studentId;
        Guid shiftId;
        await using (var db = PostgresFixture.NewContext(connectionString))
        {
            var cashier = new AppUser
            {
                FullName = "Parallel kassir",
                Role = Roles.Cashier,
                Email = "parallel." + Guid.NewGuid().ToString("N")[..8],
            };
            var student = new Student { FullName = "Parallel o'quvchi", ClassName = "1-A" };
            var shift = new CashShift
            {
                CashierId = cashier.Id,
                OpenedAt = AppClock.NowInstant,
                OpeningFloat = 0m,
                Status = CashShiftStatus.Open,
            };
            db.Users.Add(cashier);
            db.Students.Add(student);
            db.CashShifts.Add(shift);
            await db.SaveChangesAsync();

            cashierId = cashier.Id;
            studentId = student.Id;
            shiftId = shift.Id;
        }

        // Hamma vazifa BIR PAYTDA startga chiqsin — aks holda ular navbat bilan
        // yugurib, poyga umuman yuzaga kelmaydi.
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, callers).Select(async _ =>
        {
            await start.Task;

            await using var db = PostgresFixture.NewContext(connectionString);
            await using var tx = await db.BeginTransactionAsync();

            var receiptNo = await new CashShiftService(db).NextReceiptNoAsync(shiftId);

            db.Payments.Add(new Payment
            {
                ReceiptNo = receiptNo,
                StudentId = studentId,
                Amount = 1_000m,
                Method = PaymentMethod.Cash,
                CashShiftId = shiftId,
                CashierId = cashierId,
                ReceivedAt = AppClock.NowInstant,
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            return receiptNo;
        }).ToList();

        start.SetResult();
        var issued = await Task.WhenAll(tasks);

        var expected = Enumerable.Range(1, callers).Select(i => (long)i).ToList();
        Assert.Equal(expected, issued.OrderBy(n => n).ToList());

        // Va bazadagi holat ham xuddi shunday: 50 ta qator, takrorsiz.
        await using (var check = PostgresFixture.NewContext(connectionString))
        {
            var stored = await check.Payments.AsNoTracking()
                .Where(p => p.CashShiftId == shiftId)
                .Select(p => p.ReceiptNo)
                .OrderBy(n => n)
                .ToListAsync();

            Assert.Equal(expected, stored);
        }
    }

    /// <summary>
    /// Qulf (<c>pg_advisory_xact_lock</c>) tranzaksiya oxirida bo'shaydi.
    /// Tranzaksiyasiz chaqirilsa u bir zumda bo'shaydi va himoya YO'Q bo'ladi —
    /// bu esa jimgina buziladigan holat. Shuning uchun metod yiqiladi.
    /// </summary>
    [Fact]
    public async Task Chek_raqami_tranzaksiyasiz_chaqirilsa_yiqiladi()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);
        await using var db = NewDb();
        var service = new CashShiftService(db);
        var shift = await service.OpenAsync(cashierId, 0m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.NextReceiptNoAsync(shift.Id));

        Assert.Contains("tranzaksiya", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Raqam SMENA ICHIDA uzluksiz — global emas. Ikkinchi smena yana 1 dan
    /// boshlanadi, chunki unikal shart ham <c>(cash_shift_id, receipt_no)</c>.
    /// </summary>
    [Fact]
    public async Task Chek_raqami_har_smenada_1_dan_boshlanadi()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();

        await using var db = NewDb();
        var service = new CashShiftService(db);

        var first = await service.OpenAsync(cashierId, 0m);
        await using (var tx = await db.BeginTransactionAsync())
        {
            Assert.Equal(1L, await service.NextReceiptNoAsync(first.Id));
            db.Payments.Add(NewPayment(first.Id, studentId, cashierId, 1, 10_000m, PaymentMethod.Cash));
            await db.SaveChangesAsync();
            Assert.Equal(2L, await service.NextReceiptNoAsync(first.Id));
            db.Payments.Add(NewPayment(first.Id, studentId, cashierId, 2, 10_000m, PaymentMethod.Cash));
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        await service.CloseAsync(first.Id, cashierId, 20_000m, null);

        var second = await service.OpenAsync(cashierId, 0m);
        await using (var tx = await db.BeginTransactionAsync())
        {
            Assert.Equal(1L, await service.NextReceiptNoAsync(second.Id));
            await tx.CommitAsync();
        }
    }

    /// <summary>
    /// Yopilgan smenaga chek berish Z-hisobotni ORQADAN buzardi:
    /// <c>expected_cash</c> allaqachon hisoblanib, qog'ozga chiqib bo'lgan.
    /// </summary>
    [Fact]
    public async Task Yopilgan_smenaga_chek_raqami_berilmaydi()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);

        await using var db = NewDb();
        var service = new CashShiftService(db);

        var shift = await service.OpenAsync(cashierId, 0m);
        await service.CloseAsync(shift.Id, cashierId, 0m, null);

        await using var tx = await db.BeginTransactionAsync();
        var ex = await Assert.ThrowsAsync<CashShiftException>(() => service.NextReceiptNoAsync(shift.Id));
        Assert.Equal(CashShiftError.NotOpen, ex.Code);
    }

    // =====================================================================
    //  Smena ochish
    // =====================================================================

    /// <summary>
    /// SPEC §4.2: bitta kassirda bir vaqtda bitta ochiq smena. Ilova tekshiruvi
    /// ham, bazadagi <c>ux_cash_shifts_one_open_per_cashier</c> indeksi ham.
    /// </summary>
    [Fact]
    public async Task Kassirda_ikkinchi_ochiq_smena_bolmaydi()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);

        await using var db = NewDb();
        var service = new CashShiftService(db);

        var first = await service.OpenAsync(cashierId, 150_000m);
        Assert.Equal(CashShiftStatus.Open, first.Status);
        Assert.Equal(150_000m, first.OpeningFloat);
        Assert.Null(first.ExpectedCash);
        Assert.Null(first.CountedCash);
        Assert.Null(first.Variance);

        var ex = await Assert.ThrowsAsync<CashShiftException>(() => service.OpenAsync(cashierId, 0m));
        Assert.Equal(CashShiftError.AlreadyOpen, ex.Code);

        await using var check = NewDb();
        Assert.Equal(1, await check.CashShifts.CountAsync(
            s => s.CashierId == cashierId && s.Status == CashShiftStatus.Open));
    }

    /// <summary>Ochilish qoldig'i — sukut 0 (docs/ASSUMPTIONS.md, Q11), manfiysi rad etiladi.</summary>
    [Fact]
    public async Task Ochilish_qoldigi_manfiy_bolmaydi()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);
        await using var db = NewDb();

        var ex = await Assert.ThrowsAsync<CashShiftException>(
            () => new CashShiftService(db).OpenAsync(cashierId, -1m));
        Assert.Equal(CashShiftError.InvalidOpeningFloat, ex.Code);
    }

    /// <summary>Ochiq smenasi yo'q kassir uchun `CurrentAsync` — null, bu XATO EMAS.</summary>
    [Fact]
    public async Task Ochiq_smenasi_yoq_kassir_uchun_current_null()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);
        await using var db = NewDb();

        Assert.Null(await new CashShiftService(db).CurrentAsync(cashierId));
    }

    // =====================================================================
    //  Yopish: expected_cash, variance
    // =====================================================================

    /// <summary>
    /// <b>Modulning eng qimmat qoidasi.</b> <c>expected_cash</c> LEDGER'dan
    /// hisoblanadi va unga FAQAT <c>cash</c> kiradi (mijoz javobi, SPEC §8.1 Q13).
    /// Karta/o'tkazma/onlayn bankka tushadi — ularni sanash har smenada soxta
    /// kamomad berardi.
    /// </summary>
    [Fact]
    public async Task Expected_cash_ledgerdan_hisoblanadi_va_faqat_naqdni_sanaydi()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();

        await using var db = NewDb();
        var service = new CashShiftService(db);
        var shift = await service.OpenAsync(cashierId, 100_000m);

        await AddPaymentAsync(db, shift.Id, studentId, cashierId, 1, 500_000m, PaymentMethod.Cash);
        await AddPaymentAsync(db, shift.Id, studentId, cashierId, 2, 300_000m, PaymentMethod.Card);
        await AddPaymentAsync(db, shift.Id, studentId, cashierId, 3, 250_000m, PaymentMethod.Transfer);
        await AddPaymentAsync(db, shift.Id, studentId, cashierId, 4, 120_000m, PaymentMethod.Online);

        // Kassir sanadi: 100 000 (ochilish) + 500 000 (naqd) = 600 000.
        var closed = await service.CloseAsync(shift.Id, cashierId, 600_000m, "kun yakuni");

        Assert.Equal(600_000m, closed.ExpectedCash);
        Assert.Equal(600_000m, closed.CountedCash);
        Assert.Equal(0m, closed.Variance);
        Assert.Equal(CashShiftStatus.Closed, closed.Status);
        Assert.NotNull(closed.ClosedAt);

        // Naqd bo'lmagan pul YO'QOLMAYDI — u alohida ko'rsatiladi.
        Assert.Equal(500_000m, closed.CashTotal);
        Assert.Equal(670_000m, closed.NonCashTotal);
        Assert.Equal(4, closed.PaymentsCount);
    }

    /// <summary>
    /// Storno kassadan pul chiqishini ledger'da <c>credit cash</c> bilan yozadi,
    /// ya'ni kutilgan naqd o'zi kamayadi — ilovada alohida ayirish mantig'i yo'q.
    /// </summary>
    [Fact]
    public async Task Storno_kutilgan_naqdni_kamaytiradi()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);
        var approverId = await NewUserAsync(Roles.SuperAdmin);
        var studentId = await NewStudentAsync();

        await using var db = NewDb();
        var service = new CashShiftService(db);
        var shift = await service.OpenAsync(cashierId, 0m);

        var original = await AddPaymentAsync(db, shift.Id, studentId, cashierId, 1, 400_000m, PaymentMethod.Cash);
        await AddReversalAsync(db, shift.Id, studentId, cashierId, 2, original, approverId);

        var closed = await service.CloseAsync(shift.Id, cashierId, 0m, null);

        Assert.Equal(0m, closed.ExpectedCash);
        Assert.Equal(0m, closed.Variance);
        Assert.Equal(0m, closed.CashTotal);
    }

    /// <summary>
    /// SPEC §4.2: <c>variance</c> — bazada hisoblanadigan ustun. Ilova unga yoza
    /// olmaydi (xususiyatda ochiq setter YO'Q), va to'g'ridan-to'g'ri SQL bilan
    /// yozishga urinish ham BAZA darajasida rad etiladi.
    /// </summary>
    [Fact]
    public async Task Variance_bazada_hisoblanadi_va_uni_yozib_bolmaydi()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();

        await using var db = NewDb();
        var service = new CashShiftService(db);
        var shift = await service.OpenAsync(cashierId, 0m);
        await AddPaymentAsync(db, shift.Id, studentId, cashierId, 1, 300_000m, PaymentMethod.Cash);

        // Kassir 20 000 kam sanadi.
        var closed = await service.CloseAsync(shift.Id, cashierId, 280_000m, "20 ming yetishmadi");

        Assert.Equal(300_000m, closed.ExpectedCash);
        Assert.Equal(-20_000m, closed.Variance);

        // 1-qavat: entity'da ochiq setter yo'q — ilova kodi uni umuman yoza olmaydi.
        var setter = typeof(CashShift).GetProperty(nameof(CashShift.Variance))!.SetMethod;
        Assert.False(setter!.IsPublic);

        // 2-qavat: baza. Generated column'ga UPDATE — 428C9.
        await using var connection = new NpgsqlConnection(fixture.Database.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE cash_shifts SET variance = 0 WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", shift.Id);

        var pg = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("428C9", pg.SqlState);

        // Nomuvofiqlik o'sha-o'sha qoldi.
        await using var check = NewDb();
        var stored = await check.CashShifts.AsNoTracking().FirstAsync(s => s.Id == shift.Id);
        Assert.Equal(-20_000m, stored.Variance);
    }

    /// <summary>Yopilgan smenani qayta yopib bo'lmaydi (409).</summary>
    [Fact]
    public async Task Yopilgan_smenani_qayta_yopib_bolmaydi()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);

        await using var db = NewDb();
        var service = new CashShiftService(db);
        var shift = await service.OpenAsync(cashierId, 0m);
        await service.CloseAsync(shift.Id, cashierId, 0m, null);

        var ex = await Assert.ThrowsAsync<CashShiftException>(
            () => service.CloseAsync(shift.Id, cashierId, 0m, null));
        Assert.Equal(CashShiftError.AlreadyClosed, ex.Code);
    }

    /// <summary>
    /// SPEC §4.2: kassir o'zganing smenasini yopa olmaydi; direktor yopa oladi va
    /// <c>closed_by</c> da AYNAN u qoladi. Tekshiruv XIZMATDA — endpoint chetlab
    /// o'tilsa ham qoida kuchda.
    /// </summary>
    [Fact]
    public async Task Ozganing_smenasini_kassir_yopa_olmaydi_direktor_yopa_oladi()
    {
        var (owner, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        var (stranger, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        var (director, _) = await fixture.Api.SeedUserAsync(Roles.SuperAdmin);

        await using var db = NewDb();
        var service = new CashShiftService(db);
        var shift = await service.OpenAsync(owner.Id, 0m);

        var ex = await Assert.ThrowsAsync<CashShiftException>(
            () => service.CloseAsync(shift.Id, stranger.Id, 0m, null));
        Assert.Equal(CashShiftError.NotYourShift, ex.Code);

        var closed = await service.CloseAsync(shift.Id, director.Id, 0m, "kassir kasal bo'lib qoldi");
        Assert.Equal(director.FullName, closed.ClosedByName);

        await using var check = NewDb();
        var stored = await check.CashShifts.AsNoTracking().FirstAsync(s => s.Id == shift.Id);
        Assert.Equal(director.Id, stored.ClosedBy);
    }

    /// <summary>
    /// Yopish izohi (<c>note</c>) — <c>cash_shifts</c> da ustun yo'q, shuning uchun
    /// u <c>audit_log</c> ga tushadi (SPEC §4.6). Izohni jimgina yo'qotish eng
    /// yomon variant bo'lardi: u aynan nomuvofiqlik tekshirilayotganda kerak.
    /// </summary>
    [Fact]
    public async Task Yopish_izohi_audit_jurnalida_qoladi()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);

        await using var db = NewDb();
        var service = new CashShiftService(db);
        var shift = await service.OpenAsync(cashierId, 0m);
        await service.CloseAsync(shift.Id, cashierId, 0m, "kassa apparati o'chib qoldi");

        await using var check = NewDb();
        var entry = await check.AuditLogs.AsNoTracking().FirstOrDefaultAsync(
            a => a.EntityType == CashShiftService.AuditEntityCashShift
                 && a.EntityId == shift.Id.ToString());

        Assert.NotNull(entry);
        Assert.Equal("close", entry.Action);
        Assert.Equal(cashierId, entry.ActorId);
        Assert.Contains("kassa apparati o'chib qoldi", entry.Summary);
    }

    // =====================================================================
    //  Z-hisobot
    // =====================================================================

    /// <summary>
    /// SPEC §4.6 — kunlik Z-hisobot: ochilish qoldig'i, usullar kesimi,
    /// toifalar kesimi, chek raqamlari oralig'i, kutilgan/sanalgan/nomuvofiqlik.
    ///
    /// <para>
    /// Eng muhim tasdiq oxirida: <c>opening_float + (cash qatori) == expected_cash</c>.
    /// Ikki tomon bir xil to'plamdan hisoblangani uchun bu invariant har doim
    /// bajarilishi kerak — buzilsa, kassirga ko'rsatiladigan jadval va uni
    /// ayblaydigan raqam bir-biriga zid bo'lib qolardi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Z_hisobot_usullar_va_toifalar_kesimini_beradi()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);
        var approverId = await NewUserAsync(Roles.SuperAdmin);
        var studentId = await NewStudentAsync();

        await using var db = NewDb();
        var service = new CashShiftService(db);
        var shift = await service.OpenAsync(cashierId, 50_000m);

        var cash = await AddPaymentAsync(db, shift.Id, studentId, cashierId, 1, 200_000m, PaymentMethod.Cash);
        await AddAllocationAsync(db, cash, studentId, 200_000m);
        await AddPaymentAsync(db, shift.Id, studentId, cashierId, 2, 150_000m, PaymentMethod.Card);
        await AddReversalAsync(db, shift.Id, studentId, cashierId, 3, cash, approverId);

        var closed = await service.CloseAsync(shift.Id, cashierId, 50_000m, null);
        var report = await service.ZReportAsync(shift.Id);

        // Usullar: TO'RTTASI HAM qatorda, ishlatilmagani nol bilan.
        Assert.Equal(PaymentMethod.All.ToList(), report.ByMethod.Select(r => r.Method).ToList());

        var cashRow = report.ByMethod.Single(r => r.Method == PaymentMethod.Cash);
        Assert.Equal(2, cashRow.Count);          // to'lov + storno cheki
        Assert.Equal(0m, cashRow.Amount);        // storno musbat summani qaytaradi

        var cardRow = report.ByMethod.Single(r => r.Method == PaymentMethod.Card);
        Assert.Equal(1, cardRow.Count);
        Assert.Equal(150_000m, cardRow.Amount);

        Assert.Equal(0m, report.ByMethod.Single(r => r.Method == PaymentMethod.Transfer).Amount);
        Assert.Equal(0m, report.ByMethod.Single(r => r.Method == PaymentMethod.Online).Amount);

        // Toifalar: taqsimot `amount > 0` bo'lgani uchun storno bu kesimda yo'q.
        var tuition = Assert.Single(report.ByCategory);
        Assert.Equal("tuition", tuition.CategoryCode);
        Assert.Equal(200_000m, tuition.Amount);

        Assert.Equal(1L, report.ReceiptFrom);
        Assert.Equal(3L, report.ReceiptTo);
        Assert.Equal(1, report.ReversalsCount);

        Assert.Equal(50_000m, report.Shift.OpeningFloat);
        Assert.Equal(50_000m, report.Shift.ExpectedCash);
        Assert.Equal(0m, report.Shift.Variance);

        // INVARIANT: ochilish qoldig'i + naqd qatori == kutilgan naqd.
        Assert.Equal(report.Shift.ExpectedCash, report.Shift.OpeningFloat + cashRow.Amount);
        Assert.Equal(closed.ExpectedCash, report.Shift.ExpectedCash);
    }

    /// <summary>To'lovsiz smenaning Z-hisoboti ham to'liq jadval beradi (bo'sh emas).</summary>
    [Fact]
    public async Task Bosh_smenaning_Z_hisoboti_nollar_bilan_toliq()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);

        await using var db = NewDb();
        var service = new CashShiftService(db);
        var shift = await service.OpenAsync(cashierId, 0m);

        var report = await service.ZReportAsync(shift.Id);

        Assert.Equal(PaymentMethod.All.Count, report.ByMethod.Count);
        Assert.All(report.ByMethod, r => Assert.Equal(0m, r.Amount));
        Assert.Empty(report.ByCategory);
        Assert.Null(report.ReceiptFrom);
        Assert.Null(report.ReceiptTo);
        Assert.Equal(0, report.ReversalsCount);
    }

    /// <summary>Ro'yxat filtri: nomuvofiqligi bor smenalar (direktor paneli, SPEC §4.6).</summary>
    [Fact]
    public async Task Royxat_faqat_nomuvofiqligi_bor_smenalarni_qaytara_oladi()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();

        await using var db = NewDb();
        var service = new CashShiftService(db);

        var clean = await service.OpenAsync(cashierId, 0m);
        await service.CloseAsync(clean.Id, cashierId, 0m, null);

        var dirty = await service.OpenAsync(cashierId, 0m);
        await AddPaymentAsync(db, dirty.Id, studentId, cashierId, 1, 90_000m, PaymentMethod.Cash);
        await service.CloseAsync(dirty.Id, cashierId, 85_000m, null);

        var flagged = await service.ListAsync(new CashShiftQuery(cashierId, OnlyWithVariance: true));

        var only = Assert.Single(flagged);
        Assert.Equal(dirty.Id, only.Id);
        Assert.Equal(-5_000m, only.Variance);

        var all = await service.ListAsync(new CashShiftQuery(cashierId));
        Assert.Equal(2, all.Count);
    }

    // =====================================================================
    //  HTTP: qabul mezonlari (SPEC §4.2, §4.3)
    // =====================================================================

    [Fact]
    public async Task POST_open_ikkinchi_marta_409_qaytaradi()
    {
        var (cashier, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        using var client = ClientFor(cashier);

        var first = await client.PostAsJsonAsync("/api/cash/shifts/open", new { openingFloat = 0m });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/cash/shifts/open", new { openingFloat = 0m });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains(CashShiftError.AlreadyOpen, await second.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// SPEC §4.2: smenani SANALGAN naqdsiz yopib bo'lmaydi. Maydon so'rovda
    /// bo'lmasa 400 — jimgina 0 emas (0 haqiqiy va qonuniy qiymat).
    /// </summary>
    [Fact]
    public async Task POST_close_countedCash_siz_400_qaytaradi()
    {
        var (cashier, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        using var client = ClientFor(cashier);

        var shift = await OpenViaHttpAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close", new { note = "sanamadim" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(CashShiftError.InvalidCountedCash, await response.Content.ReadAsStringAsync());

        // Va smena OCHIQ qolgan bo'lishi kerak.
        await using var check = NewDb();
        Assert.Equal(CashShiftStatus.Open,
            (await check.CashShifts.AsNoTracking().FirstAsync(s => s.Id == shift.Id)).Status);
    }

    /// <summary>Nol — haqiqiy qiymat: naqd to'lovsiz smena aynan shunday yopiladi.</summary>
    [Fact]
    public async Task POST_close_countedCash_nol_bolsa_qabul_qilinadi()
    {
        var (cashier, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        using var client = ClientFor(cashier);

        var shift = await OpenViaHttpAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close", new { countedCash = 0m, note = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var closed = await response.Content.ReadFromJsonAsync<CashShiftDto>();
        Assert.Equal(CashShiftStatus.Closed, closed!.Status);
        Assert.Equal(0m, closed.CountedCash);
    }

    [Fact]
    public async Task POST_close_ozganing_smenasini_yopish_403()
    {
        var (owner, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        var (stranger, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);

        using var ownerClient = ClientFor(owner);
        var shift = await OpenViaHttpAsync(ownerClient);

        using var strangerClient = ClientFor(stranger);
        var response = await strangerClient.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close", new { countedCash = 0m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task POST_close_admin_ozganing_smenasini_yopa_oladi()
    {
        var (owner, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        var (admin, _) = await fixture.Api.SeedUserAsync(Roles.Admin);

        using var ownerClient = ClientFor(owner);
        var shift = await OpenViaHttpAsync(ownerClient);

        using var adminClient = ClientFor(admin);
        var response = await adminClient.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close", new { countedCash = 0m, note = "kassir ketib qoldi" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var closed = await response.Content.ReadFromJsonAsync<CashShiftDto>();
        Assert.Equal(admin.FullName, closed!.ClosedByName);
    }

    [Fact]
    public async Task GET_z_report_ozganing_smenasi_uchun_403()
    {
        var (owner, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        var (stranger, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);

        using var ownerClient = ClientFor(owner);
        var shift = await OpenViaHttpAsync(ownerClient);

        Assert.Equal(HttpStatusCode.OK,
            (await ownerClient.GetAsync($"/api/cash/shifts/{shift.Id}/z-report")).StatusCode);

        using var strangerClient = ClientFor(stranger);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await strangerClient.GetAsync($"/api/cash/shifts/{shift.Id}/z-report")).StatusCode);
    }

    /// <summary>Ochiq smenasi yo'q kassir uchun 204 — bu kutilgan holat, xato emas.</summary>
    [Fact]
    public async Task GET_current_ochiq_smena_yoq_bolsa_204()
    {
        var (cashier, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        using var client = ClientFor(cashier);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.GetAsync("/api/cash/shifts/current")).StatusCode);

        await OpenViaHttpAsync(client);

        var response = await client.GetAsync("/api/cash/shifts/current");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var current = await response.Content.ReadFromJsonAsync<CashShiftDto>();
        Assert.Equal(cashier.Id, current!.CashierId);
    }

    /// <summary>
    /// SPEC §4.3: kassir boshqa kassirlar kesimini ko'rmaydi. Filtr JIMGINA
    /// o'ziga toraytiriladi — bu 403 emas, chunki "smenalar ro'yxati" kassir
    /// uchun o'z ro'yxati degani.
    /// </summary>
    [Fact]
    public async Task GET_shifts_kassir_faqat_ozining_smenalarini_koradi()
    {
        var (mine, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        var (other, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);

        using var otherClient = ClientFor(other);
        await OpenViaHttpAsync(otherClient);

        using var myClient = ClientFor(mine);
        await OpenViaHttpAsync(myClient);

        // Ataylab O'ZGANING id'sini so'raymiz.
        var response = await myClient.GetAsync($"/api/cash/shifts?cashierId={other.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<CashShiftDto>>();
        Assert.NotEmpty(list!);
        Assert.All(list!, s => Assert.Equal(mine.Id, s.CashierId));
    }

    /// <summary>SPEC §4.3: kassa yuzasi o'qituvchiga ham, anonimga ham yopiq.</summary>
    [Fact]
    public async Task Kassa_endpointlari_begonaga_yopiq()
    {
        var (teacher, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        using var teacherClient = ClientFor(teacher);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacherClient.GetAsync("/api/cash/shifts/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacherClient.PostAsJsonAsync("/api/cash/shifts/open", new { openingFloat = 0m })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacherClient.GetAsync("/api/cash/shifts")).StatusCode);

        using var anonymous = Wired.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/cash/shifts/current")).StatusCode);
    }

    /// <summary>
    /// SPEC §4.2: smena tarixdir — u yopiladi, tahrirlanmaydi va o'chirilmaydi.
    /// Controller'da PUT/DELETE bo'lmasligi shartnomaning bir qismi (P1-11 dagi
    /// <c>PaymentsController</c> uchun ham xuddi shunday mezon bor).
    /// </summary>
    [Fact]
    public async Task Smena_endpointlarida_PUT_va_DELETE_yoq()
    {
        var actions = typeof(CashShiftsController)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .SelectMany(m => m.GetCustomAttributes(inherit: true))
            .Select(a => a.GetType().Name)
            .ToList();

        Assert.DoesNotContain("HttpPutAttribute", actions);
        Assert.DoesNotContain("HttpDeleteAttribute", actions);
        Assert.DoesNotContain("HttpPatchAttribute", actions);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private async Task<string> NewUserAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        return user.Id;
    }

    private async Task<string> NewStudentAsync()
    {
        await using var db = NewDb();
        var student = new Student { FullName = "Test o'quvchi", ClassName = "1-A" };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return student.Id;
    }

    private static Payment NewPayment(
        Guid shiftId, string studentId, string cashierId,
        long receiptNo, decimal amount, string method, Guid? reversalOf = null) => new()
        {
            ReceiptNo = receiptNo,
            StudentId = studentId,
            Amount = amount,
            Method = method,
            CashShiftId = shiftId,
            CashierId = cashierId,
            ReceivedAt = AppClock.NowInstant,
            ReversalOf = reversalOf,
        };

    /// <summary>
    /// To'lov + unga mos jurnal yozuvi. P1-11 (<c>PaymentService</c>) hali yo'q,
    /// shuning uchun test AYNAN o'sha ikki qatorni yozadi: <c>debit cash|bank</c>
    /// / <c>credit receivable</c>. Hisobni <see cref="Accounts.SettlementFor"/>
    /// tanlaydi — "naqdmi yoki yo'q" savolining yagona javobi o'sha yerda.
    /// </summary>
    private static async Task<Payment> AddPaymentAsync(
        AppDbContext db, Guid shiftId, string studentId, string cashierId,
        long receiptNo, decimal amount, string method)
    {
        var payment = NewPayment(shiftId, studentId, cashierId, receiptNo, amount, method);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        await new LedgerService(db).PostAsync(
        [
            new LedgerPosting(Accounts.SettlementFor(method), LedgerDirection.Debit, amount,
                LedgerRefType.Payment, payment.Id),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, amount,
                LedgerRefType.Payment, payment.Id),
        ], cashierId);

        return payment;
    }

    /// <summary>Storno: qarshi to'lov qatori + ko'zgu jurnal yozuvlari (P1-11 shunday qiladi).</summary>
    private static async Task AddReversalAsync(
        AppDbContext db, Guid shiftId, string studentId, string cashierId,
        long receiptNo, Payment original, string approverId)
    {
        db.Payments.Add(NewPayment(
            shiftId, studentId, cashierId, receiptNo, original.Amount, original.Method, original.Id));
        await db.SaveChangesAsync();

        await new LedgerService(db).PostAsync(
        [
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, original.Amount,
                LedgerRefType.Reversal, original.Id),
            new LedgerPosting(Accounts.SettlementFor(original.Method), LedgerDirection.Credit, original.Amount,
                LedgerRefType.Reversal, original.Id),
        ], approverId);
    }

    /// <summary>Hisob-faktura + taqsimot — Z-hisobotning toifalar kesimi uchun.</summary>
    private static async Task AddAllocationAsync(
        AppDbContext db, Payment payment, string studentId, decimal amount)
    {
        var invoice = new Invoice
        {
            StudentId = studentId,
            CategoryId = TuitionCategoryId,
            PeriodMonth = new DateOnly(2026, 9, 1),
            Amount = amount,
            Discount = 0m,
            DueOn = new DateOnly(2026, 9, 10),
            Status = InvoiceStatus.Paid,
            CreatedAt = AppClock.NowInstant,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        db.PaymentAllocations.Add(new PaymentAllocation
        {
            PaymentId = payment.Id,
            InvoiceId = invoice.Id,
            Amount = amount,
        });
        await db.SaveChangesAsync();
    }

    private static string WithPoolSize(string connectionString, int maxPoolSize) =>
        new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = maxPoolSize }.ConnectionString;

    // ---------------------------------------------------------------------
    //  HTTP klienti
    // ---------------------------------------------------------------------

    /// <summary>
    /// <b>Nega ilovaning alohida nusxasi kerak.</b> <c>ICashShiftService</c>
    /// hali <c>Program.cs</c> da ro'yxatdan o'tmagan — u P1-15 ning fayli va
    /// P1-10 unga tegmaydi (docs/PENDING_WIRING.md). Shuning uchun test AYNAN
    /// o'sha bitta qator qo'shilgan host'ni ko'taradi: endpoint'lar HOZIR
    /// sinaladi, <c>Program.cs</c> esa tegilmay qoladi.
    ///
    /// <para>
    /// <c>WithWebHostBuilder</c> ota-fabrikaning sozlamalarini (test bazasi,
    /// o'chirilgan fon xizmatlari, JWT kaliti) MEROS QILIB oladi va uning
    /// konstruktorini chaqirmaydi — ya'ni "bir vaqtda bitta ApiFactory"
    /// qoidasi buzilmaydi.
    /// </para>
    /// <para>
    /// Host butun test yurishi uchun BIR MARTA ko'tariladi (statik): ilova
    /// ishga tushishida migratsiya va seed yuradi, uni har test uchun takrorlash
    /// vaqt byudjetini yeb qo'yardi. Tozalash kerak emas —
    /// <c>WebApplicationFactory</c> o'zidan hosil qilingan fabrikalarni
    /// <c>ApiFixture.DisposeAsync</c> paytida o'zi yopadi.
    /// </para>
    /// </summary>
    private static WebApplicationFactory<AuthController>? wired;
    private static readonly Lock WiredGate = new();

    private WebApplicationFactory<AuthController> Wired
    {
        get
        {
            lock (WiredGate)
                return wired ??= fixture.Api.WithWebHostBuilder(builder =>
                    builder.ConfigureServices(services =>
                        services.AddScoped<ICashShiftService, CashShiftService>()));
        }
    }

    private HttpClient ClientFor(AppUser user)
    {
        var client = Wired.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.Api.TokenFor(user.Role, user.Id, user.FullName, user.Email));
        return client;
    }

    private static async Task<CashShiftDto> OpenViaHttpAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/cash/shifts/open", new { openingFloat = 0m });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashShiftDto>())!;
    }
}
