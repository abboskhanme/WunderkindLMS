using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// Chek raqamining uzluksizligi — P1-24, SPEC §4.2 va §4.7.
///
/// <para>
/// <b>Nima uchun bu fayl bor.</b> <c>CashShiftServiceTests</c> chek raqamini
/// AJRATUVCHINING o'zini (<see cref="ICashShiftService.NextReceiptNoAsync"/>)
/// parallel sinaydi. Bu yerdagi test bir qavat yuqoridan boradi: 50 ta parallel
/// chaqiruv AYNAN <see cref="PaymentService.AcceptAsync"/> ni ishga tushiradi,
/// ya'ni butun pul tranzaksiyasi (to'lov + taqsimot + hisob-faktura statusi +
/// ikkita jurnal qatori) qulf ostida yuguradi. Farq muhim: raqam ajratgichning
/// o'zi to'g'ri bo'lib, uni chaqiruvchi tranzaksiyani noto'g'ri chegaralasa,
/// natija baribir bo'shliq (yoki dublikat) bo'lardi — va auditor uchun
/// chekdagi bo'shliq = O'CHIRILGAN CHEK degani.
/// </para>
///
/// <para>
/// <b>Testlar ULANISH POOLINI ataylab kattalashtiradi.</b> Umumiy test bazasi
/// satri <c>MaxPoolSize = 10</c> bilan quriladi (<c>PostgresFixture</c>).
/// Ellikta yozuvchi shunda pool navbatida turardi: ular baribir yashil natija
/// berardi, lekin QULF sinovdan o'tmasdi — ular hech qachon bir vaqtda
/// tranzaksiya ichida bo'lmasdi. Shuning uchun parallel testlar O'Z bazasini
/// (<c>Postgres.CreateDatabaseAsync</c>) va o'z, kengaytirilgan satrini oladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ReceiptNumberingTests : IDisposable
{
    /// <summary>P1-24 qabul mezoni: aynan 50 ta parallel to'lov.</summary>
    private const int Callers = 50;

    /// <summary>Har to'lovning summasi — hisob-kitobni ko'z bilan tekshirsa bo'ladigan son.</summary>
    private const decimal Unit = 10_000m;

    private readonly ApiFixture fixture;

    public ReceiptNumberingTests(ApiFixture fixture)
    {
        this.fixture = fixture;
        ReleaseIdleConnections();
    }

    /// <summary>
    /// <b>Ulanishlarni ORQANGDAN TOZALASH — bu yerda majburiy.</b>
    ///
    /// <para>
    /// Bu fayldagi parallel testlar 50+ ulanish oladi, Npgsql esa test
    /// tugagach ularni pool'da BO'SH holda ~5 daqiqa (<c>ConnectionIdleLifetime</c>)
    /// ushlab turadi. Konteynerdagi <c>max_connections</c> = 100, ya'ni bunday
    /// pool qolib ketsa keyingi testlar
    /// <c>53300: remaining connection slots are reserved…</c> bilan yiqiladi —
    /// va aybdor test allaqachon yashil bo'lib o'tib ketgan bo'ladi.
    /// </para>
    /// <para>
    /// Konstruktorda ham chaqiriladi: oldingi test klassi (masalan
    /// <c>CashShiftServiceTests</c> ning o'z parallel testi) o'zidan keyin
    /// bo'sh ulanish qoldirgan bo'lishi mumkin, bizga esa AYNAN 50 ta joy
    /// kerak. Hamma testlar bitta xUnit kolleksiyasida — KETMA-KET — yuradi,
    /// shuning uchun umumiy poolni bo'shatish xavfsiz: ilova keyingi so'rovda
    /// ulanishni o'zi qayta ochadi. <c>PostgresFixture</c> ham xuddi shu
    /// chaqiruvdan foydalanadi.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        ReleaseIdleConnections();
        GC.SuppressFinalize(this);
    }

    private static void ReleaseIdleConnections() => NpgsqlConnection.ClearAllPools();

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  P1-24 ning bosh mezoni
    // =====================================================================

    /// <summary>
    /// <b>50 ta PARALLEL to'lov bitta smenaga → chek raqamlari AYNAN 1..50.</b>
    /// Bo'shliq ham, takror ham bo'lmasin (SPEC §4.2).
    ///
    /// <para>
    /// Test HAQIQATAN parallel ekanini o'zi o'lchaydi: <c>peak</c> — bir vaqtda
    /// <c>AcceptAsync</c> ichida turgan vazifalarning eng katta soni. Agar
    /// vazifalar ketma-ket yugursa u 1 bo'lib qolardi va test hech narsani
    /// isbotlamasdi, shuning uchun bu qiymat ham tasdiqlanadi.
    /// </para>
    /// <para>
    /// Regressiyani qanday ushlaydi: <c>pg_advisory_xact_lock</c> olib tashlansa,
    /// ellikta tranzaksiya bir xil <c>max(receipt_no) + 1</c> ni o'qiydi;
    /// ulardan biri o'tadi, qolganlari <c>ix_payments_cash_shift_id_receipt_no</c>
    /// unikal indeksida yiqiladi va <see cref="Task.WhenAll(Task[])"/> xatoni
    /// shu yerga olib chiqadi. Qulf tranzaksiyadan tashqariga chiqarilsa —
    /// aynan shu, faqat kamroq to'lov bilan. Ikkala holatda ham test QIZIL.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Ellik_parallel_tolov_bitta_smenada_1_dan_50_gacha_chek_beradi()
    {
        // Alohida baza: umumiy bazadagi 10 ta ulanish 50 ta yozuvchiga yetmaydi.
        var database = await fixture.Postgres.CreateDatabaseAsync("receipts50");
        // 50 yozuvchi + 10 zaxira (tayyorlash/tekshirish konteksti, Npgsql xizmat
        // ulanishlari). Konteynerdagi `max_connections` = 100, ilovaning o'z
        // pooli esa BOSHQA bazada va ko'pi bilan 10 ta — ya'ni chegaraga tegmaydi.
        var connectionString = WithPoolSize(database.OwnerConnectionString, Callers + 10);

        var scene = await ArrangeAsync(connectionString, Callers);

        // Hamma vazifa BIR PAYTDA startga chiqsin.
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = 0;
        var peak = 0;

        var tasks = Enumerable.Range(0, Callers).Select(async index =>
        {
            await start.Task;

            var now = Interlocked.Increment(ref inFlight);
            RecordPeak(ref peak, now);
            try
            {
                await using var db = PostgresFixture.NewContext(connectionString);
                var service = new PaymentService(db, new CashShiftService(db), new LedgerService(db));

                var dto = await service.AcceptAsync(
                    new AcceptPaymentRequest(
                        scene.StudentIds[index], Unit, PaymentMethod.Cash, $"parallel #{index}",
                        [new AllocationRequest(scene.InvoiceIds[index], Unit)]),
                    scene.CashierId);

                return dto.ReceiptNo;
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        }).ToList();

        start.SetResult();
        var issued = await Task.WhenAll(tasks);

        // ---- 0. Test haqiqatan parallel bo'ldimi? ----
        Assert.True(peak >= 10,
            $"Bir vaqtda ko'pi bilan {peak} ta to'lov ishlagan — vazifalar amalda KETMA-KET "
            + "yugurdi. Bunday holatda qulf sinovdan o'tmaydi va test hech narsani isbotlamaydi.");

        // ---- 1. Xizmat qaytargan raqamlar: aynan {1..50} ----
        var expected = Enumerable.Range(1, Callers).Select(i => (long)i).ToList();
        Assert.Equal(expected, issued.OrderBy(n => n).ToList());
        Assert.Equal(Callers, issued.Distinct().Count());

        // ---- 2. Bazadagi holat ham AYNAN shunday ----
        await using var check = PostgresFixture.NewContext(connectionString);

        var payments = await check.Payments.AsNoTracking()
            .Where(p => p.CashShiftId == scene.ShiftId)
            .ToListAsync();

        Assert.Equal(Callers, payments.Count);
        Assert.Equal(expected, payments.Select(p => p.ReceiptNo).OrderBy(n => n).ToList());
        Assert.Equal(Callers, payments.Select(p => p.ReceiptNo).Distinct().Count());
        Assert.All(payments, p =>
        {
            Assert.Equal(scene.CashierId, p.CashierId);
            Assert.Equal(Unit, p.Amount);
            Assert.Equal(PaymentMethod.Cash, p.Method);
            Assert.Null(p.ReversalOf);
        });

        // ---- 3. Har to'lov to'liq yozildi: taqsimot + IKKITA jurnal qatori ----
        var paymentIds = payments.Select(p => (Guid?)p.Id).ToList();

        var allocations = await check.PaymentAllocations.AsNoTracking()
            .Where(a => paymentIds.Contains(a.PaymentId))
            .ToListAsync();
        Assert.Equal(Callers, allocations.Count);
        Assert.Equal(Callers * Unit, allocations.Sum(a => a.Amount));

        var entries = await check.LedgerEntries.AsNoTracking()
            .Where(e => paymentIds.Contains(e.RefId))
            .ToListAsync();
        Assert.Equal(Callers * 2, entries.Count);
        Assert.Equal(Callers * Unit, entries
            .Where(e => e.Direction == LedgerDirection.Debit && e.Account == Accounts.Cash)
            .Sum(e => e.Amount));
        Assert.Equal(Callers * Unit, entries
            .Where(e => e.Direction == LedgerDirection.Credit && e.Account == Accounts.Receivable)
            .Sum(e => e.Amount));

        // ---- 4. Hisob-fakturalar to'langan deb belgilandi ----
        var statuses = await check.Invoices.AsNoTracking()
            .Where(i => scene.InvoiceIds.Contains(i.Id))
            .Select(i => i.Status)
            .ToListAsync();
        Assert.Equal(Callers, statuses.Count);
        Assert.All(statuses, s => Assert.Equal(InvoiceStatus.Paid, s));

        // ---- 5. Keyingi chek — 51 (raqam "sakrab ketmadi") ----
        await using var tx = await check.BeginTransactionAsync();
        Assert.Equal(Callers + 1L, await new CashShiftService(check).NextReceiptNoAsync(scene.ShiftId));
        await tx.RollbackAsync();
    }

    /// <summary>
    /// Chek raqami SMENA ichida uzluksiz, lekin smenalar bir-birini
    /// BLOKLAMAYDI va raqamlari ARALASHMAYDI: ikkita kassir bir vaqtda
    /// ishlaganda ikkala smena ham 1 dan boshlab o'z ketma-ketligini oladi.
    ///
    /// <para>
    /// Nega kerak: qulf kaliti smena bo'yicha hisoblanadi
    /// (<c>hashtextextended("cash_shift_receipt:{id}")</c>). Agar u global
    /// konstantaga aylanib qolsa, testlar baribir yashil bo'lardi — faqat
    /// butun kassa bitta navbatga tushardi. Agar aksincha, kalit smenani
    /// hisobga olmay qolsa (masalan kassir bo'yicha), raqamlar aralashardi.
    /// Ikkala xatoni ham shu test ko'radi: 24 ta parallel to'lov, ikkita
    /// mustaqil ketma-ketlik.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Ikki_smena_parallel_ishlaganda_chek_raqamlari_aralashmaydi()
    {
        const int perShift = 12;

        var database = await fixture.Postgres.CreateDatabaseAsync("receipts2x");
        // Ikki smena × 12 yozuvchi = 24 bir vaqtdagi tranzaksiya, + 6 zaxira.
        var connectionString = WithPoolSize(database.OwnerConnectionString, (perShift * 2) + 6);

        var first = await ArrangeAsync(connectionString, perShift);
        var second = await ArrangeAsync(connectionString, perShift);
        Assert.NotEqual(first.ShiftId, second.ShiftId);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = new[] { first, second }
            .SelectMany(scene => Enumerable.Range(0, perShift).Select(async index =>
            {
                await start.Task;

                await using var db = PostgresFixture.NewContext(connectionString);
                var service = new PaymentService(db, new CashShiftService(db), new LedgerService(db));

                var dto = await service.AcceptAsync(
                    new AcceptPaymentRequest(
                        scene.StudentIds[index], Unit, PaymentMethod.Cash, null,
                        [new AllocationRequest(scene.InvoiceIds[index], Unit)]),
                    scene.CashierId);

                return (scene.ShiftId, dto.ReceiptNo);
            }))
            .ToList();

        start.SetResult();
        var issued = await Task.WhenAll(tasks);

        var expected = Enumerable.Range(1, perShift).Select(i => (long)i).ToList();
        foreach (var shiftId in new[] { first.ShiftId, second.ShiftId })
            Assert.Equal(expected, issued
                .Where(r => r.ShiftId == shiftId)
                .Select(r => r.ReceiptNo)
                .OrderBy(n => n)
                .ToList());

        // Bazada ham: har smenada 12 qator, raqamlari 1..12.
        await using var check = PostgresFixture.NewContext(connectionString);
        foreach (var shiftId in new[] { first.ShiftId, second.ShiftId })
        {
            var stored = await check.Payments.AsNoTracking()
                .Where(p => p.CashShiftId == shiftId)
                .Select(p => p.ReceiptNo)
                .OrderBy(n => n)
                .ToListAsync();

            Assert.Equal(expected, stored);
        }
    }

    // =====================================================================
    //  Uzluksizlikning ikkinchi yarmi: bekor qilingan urinish va dublikat
    // =====================================================================

    /// <summary>
    /// <b>Yiqilgan so'rov chekda BO'SHLIQ qoldirmaydi.</b> Raqam tranzaksiya
    /// ichida ajratiladi va hech qayerga saqlanmaydi (sekvens emas, <c>max + 1</c>),
    /// shuning uchun tranzaksiya qaytarilsa raqam "ishlatilmagan" bo'lib qoladi
    /// va keyingi to'lov AYNAN o'shani oladi.
    ///
    /// <para>
    /// Auditor uchun farq hal qiluvchi: <c>1, 3</c> ketma-ketligi "2-chek
    /// o'chirilgan" degan savolni tug'diradi, <c>1, 2</c> esa hech qanday
    /// savol tug'dirmaydi. Sekvensga (<c>nextval</c>) o'tilsa bu xossa
    /// yo'qoladi — shuning uchun u alohida tekshiriladi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Bekor_qilingan_tranzaksiya_chek_raqamida_boshliq_qoldirmaydi()
    {
        await using var db = NewDb();
        var scene = await ArrangeAsync(fixture.Database.OwnerConnectionString, invoices: 2);
        var payments = new PaymentService(db, new CashShiftService(db), new LedgerService(db));

        var first = await payments.AcceptAsync(
            new AcceptPaymentRequest(
                scene.StudentIds[0], Unit, PaymentMethod.Cash, null,
                [new AllocationRequest(scene.InvoiceIds[0], Unit)]),
            scene.CashierId);
        Assert.Equal(1L, first.ReceiptNo);

        // Yiqilgan so'rovni takrorlaymiz: raqam olindi, qator yozildi, keyin
        // tranzaksiya qaytarildi (jarayon o'ldi / xato chiqdi).
        await using (var doomed = NewDb())
        {
            await using var tx = await doomed.BeginTransactionAsync();

            var wasted = await new CashShiftService(doomed).NextReceiptNoAsync(scene.ShiftId);
            Assert.Equal(2L, wasted);

            doomed.Payments.Add(new Payment
            {
                ReceiptNo = wasted,
                StudentId = scene.StudentIds[1],
                Amount = Unit,
                Method = PaymentMethod.Cash,
                CashShiftId = scene.ShiftId,
                CashierId = scene.CashierId,
                ReceivedAt = AppClock.NowInstant,
            });
            await doomed.SaveChangesAsync();

            await tx.RollbackAsync();
        }

        var second = await payments.AcceptAsync(
            new AcceptPaymentRequest(
                scene.StudentIds[1], Unit, PaymentMethod.Cash, null,
                [new AllocationRequest(scene.InvoiceIds[1], Unit)]),
            scene.CashierId);

        // AYNAN 2 — 3 emas. Bo'shliq yo'q.
        Assert.Equal(2L, second.ReceiptNo);

        await using var check = NewDb();
        var stored = await check.Payments.AsNoTracking()
            .Where(p => p.CashShiftId == scene.ShiftId)
            .Select(p => p.ReceiptNo)
            .OrderBy(n => n)
            .ToListAsync();

        Assert.Equal(new List<long> { 1L, 2L }, stored);
    }

    /// <summary>
    /// Ikkinchi qavat: ilovani butunlay chetlab o'tib, bitta smenaga bir xil
    /// chek raqamini ikki marta yozishga urinish BAZADA rad etiladi
    /// (<c>unique (cash_shift_id, receipt_no)</c>, SPEC §4.2).
    ///
    /// <para>
    /// Bu qulfning zaxirasi: qulf ishlamay qolsa dublikat jimgina emas,
    /// SQLSTATE 23505 bilan chiqadi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Bir_smenada_bir_xil_chek_raqami_ikki_marta_yozilmaydi()
    {
        var scene = await ArrangeAsync(fixture.Database.OwnerConnectionString, invoices: 0);

        await using var db = NewDb();
        db.Payments.Add(NewPaymentRow(scene, receiptNo: 7));
        await db.SaveChangesAsync();

        await using var second = NewDb();
        second.Payments.Add(NewPaymentRow(scene, receiptNo: 7));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal("23505", pg.SqlState);
        Assert.Equal("ix_payments_cash_shift_id_receipt_no", pg.ConstraintName);

        // Birinchi qator joyida — rad etish uni buzmadi.
        await using var check = NewDb();
        var stored = await check.Payments.AsNoTracking()
            .Where(p => p.CashShiftId == scene.ShiftId)
            .ToListAsync();
        Assert.Equal(7L, Assert.Single(stored).ReceiptNo);
    }

    // =====================================================================
    //  HTTP: chek raqami mijozgacha yetib boradi
    // =====================================================================

    /// <summary>
    /// Kassir ekrani ko'radigan yo'l: ketma-ket uchta to'lov 1, 2, 3 chekini
    /// beradi va ro'yxat endpoint'i ham AYNAN shu uchtasini qaytaradi.
    ///
    /// <para>
    /// Xizmat qatlamidagi testlar buni ushlay olmaydi: raqam DTO'da, marshrutda
    /// yoki JSON'da yo'qolishi mumkin, va o'shanda kassir chop etadigan chekda
    /// raqam bo'lmasdi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task HTTP_ketma_ket_tolovlar_1_2_3_chek_raqamlarini_beradi()
    {
        var (cashier, client) = await ClientAsync(Roles.Cashier);

        var open = await client.PostAsJsonAsync("/api/cash/shifts/open", new { openingFloat = 0m });
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var shift = (await open.Content.ReadFromJsonAsync<CashShiftDto>())!;

        var scene = await ArrangeAsync(
            fixture.Database.OwnerConnectionString, invoices: 3,
            cashierId: cashier.Id, shiftId: shift.Id);

        var receipts = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/cash/payments", new
            {
                studentId = scene.StudentIds[i],
                amount = Unit,
                method = PaymentMethod.Cash,
                allocations = new[] { new { invoiceId = scene.InvoiceIds[i], amount = Unit } },
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = (await response.Content.ReadFromJsonAsync<PaymentDto>())!;

            Assert.Equal(shift.Id, dto.CashShiftId);
            Assert.Equal(cashier.Id, dto.CashierId);
            Assert.Equal(Unit, dto.Amount);
            receipts.Add(dto.ReceiptNo);
        }

        Assert.Equal(new List<long> { 1L, 2L, 3L }, receipts);

        // Ro'yxat ham xuddi shu uchtasini ko'rsatadi.
        var list = await client.GetAsync($"/api/billing/payments?cashShiftId={shift.Id}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var rows = (await list.Content.ReadFromJsonAsync<List<PaymentDto>>())!;

        Assert.Equal(3, rows.Count);
        Assert.Equal(new List<long> { 1L, 2L, 3L }, rows.Select(r => r.ReceiptNo).OrderBy(n => n).ToList());
        Assert.All(rows, r => Assert.Equal(shift.Id, r.CashShiftId));
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    /// <summary>Bitta test sahnasi: kassir, uning ochiq smenasi, N ta o'quvchi + hisob-faktura.</summary>
    private sealed record Scene(
        string CashierId, Guid ShiftId, List<string> StudentIds, List<Guid> InvoiceIds);

    /// <summary>
    /// Sahnani OWNER ulanishi bilan tayyorlaydi (test ma'lumoti — <c>users</c>,
    /// <c>students</c>, <c>invoices</c>). Har to'lov O'Z o'quvchisiga va O'Z
    /// hisob-fakturasiga ketadi: aks holda parallel vazifalar bitta hisob-faktura
    /// qoldig'i uchun kurashardi va test qulfni emas, taqsimot qoidasini
    /// sinagan bo'lardi.
    /// </summary>
    private static async Task<Scene> ArrangeAsync(
        string connectionString, int invoices, string? cashierId = null, Guid? shiftId = null)
    {
        await using var db = PostgresFixture.NewContext(connectionString);

        var suffix = Guid.NewGuid().ToString("N")[..8];

        if (cashierId is null)
        {
            var cashier = new AppUser
            {
                FullName = $"Kassir {suffix}",
                Role = Roles.Cashier,
                Email = $"receipts.{suffix}",
            };
            db.Users.Add(cashier);
            cashierId = cashier.Id;
        }

        if (shiftId is null)
        {
            var shift = new CashShift
            {
                CashierId = cashierId,
                OpenedAt = AppClock.NowInstant,
                OpeningFloat = 0m,
                Status = CashShiftStatus.Open,
            };
            db.CashShifts.Add(shift);
            shiftId = shift.Id;
        }

        var categoryId = await db.FeeCategories.AsNoTracking()
            .Where(c => c.Code == "tuition").Select(c => c.Id).SingleAsync();

        var month = new DateOnly(AppClock.Today.Year, AppClock.Today.Month, 1);
        var studentIds = new List<string>(invoices);
        var invoiceIds = new List<Guid>(invoices);

        for (var i = 0; i < invoices; i++)
        {
            var student = new Student
            {
                FullName = $"O'quvchi {suffix}-{i}",
                LastName = "Test",
                FirstName = $"O'quvchi{i}",
                ClassName = "1-A",
                EnrollmentDate = AppClock.Today.ToString("yyyy-MM-dd"),
            };
            db.Students.Add(student);

            var invoice = new Invoice
            {
                StudentId = student.Id,
                CategoryId = categoryId,
                PeriodMonth = month,
                Amount = Unit,
                Discount = 0m,
                DueOn = month.AddDays(9),
                Status = InvoiceStatus.Open,
                CreatedAt = AppClock.NowInstant,
            };
            db.Invoices.Add(invoice);

            studentIds.Add(student.Id);
            invoiceIds.Add(invoice.Id);
        }

        // O'quvchisiz sahna ham kerak (dublikat testi) — o'shanda bitta o'quvchi
        // baribir yaratiladi, chunki `payments.student_id` NOT NULL va FK.
        if (invoices == 0)
        {
            var student = new Student
            {
                FullName = $"O'quvchi {suffix}",
                LastName = "Test",
                FirstName = "O'quvchi",
                ClassName = "1-A",
                EnrollmentDate = AppClock.Today.ToString("yyyy-MM-dd"),
            };
            db.Students.Add(student);
            studentIds.Add(student.Id);
        }

        await db.SaveChangesAsync();
        return new Scene(cashierId, shiftId.Value, studentIds, invoiceIds);
    }

    private static Payment NewPaymentRow(Scene scene, long receiptNo) => new()
    {
        ReceiptNo = receiptNo,
        StudentId = scene.StudentIds[0],
        Amount = Unit,
        Method = PaymentMethod.Cash,
        CashShiftId = scene.ShiftId,
        CashierId = scene.CashierId,
        ReceivedAt = AppClock.NowInstant,
    };

    /// <summary>
    /// Parallel yozuvchilar uchun kengaytirilgan pool. Fixture'ning o'z satri
    /// 10 ta ulanish bilan cheklangan (u 20+ test klassi uchun mo'ljallangan),
    /// bu yerda esa har yozuvchi tranzaksiya davomida O'Z ulanishini ushlab
    /// turishi SHART — aks holda ular pool navbatida turadi va poyga yuzaga
    /// kelmaydi.
    /// </summary>
    private static string WithPoolSize(string connectionString, int maxPoolSize) =>
        new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = maxPoolSize }.ConnectionString;

    private static void RecordPeak(ref int peak, int candidate)
    {
        int seen;
        while (candidate > (seen = Volatile.Read(ref peak)))
            Interlocked.CompareExchange(ref peak, candidate, seen);
    }

    // ---------------------------------------------------------------------
    //  HTTP klienti — ILOVANING O'Z DI grafi bilan
    // ---------------------------------------------------------------------
    //
    //  Bu yerda `WithWebHostBuilder` bilan xizmat ULANMAYDI. P1-15 dan keyin
    //  `ICashShiftService`, `IPaymentService` va `ILedgerService` `Program.cs`
    //  da ro'yxatdan o'tgan (docs/PENDING_WIRING.md dagi bandlar yopildi),
    //  ya'ni testda ularni qayta ro'yxatdan o'tkazish HAQIQIY simni yashirib
    //  qo'yardi: kimdir `Program.cs` dan `AddScoped<IPaymentService, ...>`
    //  qatorini olib tashlasa, test baribir yashil qolaverardi. Endi bunday
    //  regressiya shu yerda 500 bo'lib chiqadi.

    private async Task<(AppUser User, HttpClient Client)> ClientAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        var client = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));
        return (user, client);
    }
}
