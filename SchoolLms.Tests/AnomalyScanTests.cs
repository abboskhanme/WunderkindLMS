using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Moliya auditi va tungi tekshiruv (P1-14) — SPEC §4.6.
///
/// <para>
/// ENG MUHIM TEST:
/// <see cref="Ellik_ming_nomuvofiqlik_aynan_bitta_bayroq_beradi"/> — P1-14 ning
/// beshinchi qabul mezoni. U ALOHIDA, toza bazada yuradi: "aynan bitta" degan
/// da'vo faqat butun jadval ko'rinib turganda ma'noga ega, umumiy test bazasida
/// esa boshqa testlarning qatorlari ham yotadi.
/// </para>
/// <para>
/// Qolgan testlar ikki qavatda: xizmat orqali (nima yozildi) va HTTP orqali
/// (marshrut, rol darvozasi, status kodlari). Baza OWNER ulanishi bilan
/// ochiladi — <c>LedgerServiceTests</c> dagi bilan bir xil sabab: test
/// ma'lumotini tayyorlash uchun <c>users</c>/<c>students</c> ga yozish kerak.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AnomalyScanTests(ApiFixture fixture)
{
    /// <summary>Migratsiyada seed qilingan barqaror toifa id'si (billing_seed.sql).</summary>
    private static readonly Guid TuitionCategoryId = new("00000000-0000-0000-0000-0000000000c1");

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  1-mezon — audit: before/after JSONB
    // =====================================================================

    /// <summary>
    /// SPEC §4.6: <c>before</c>/<c>after</c> — <b><c>jsonb</c></b>, oddiy matn
    /// emas. Buni ustun turining O'ZIDAN tekshiramiz: kod nima yozishidan
    /// qat'i nazar, sxema noto'g'ri bo'lsa mezon bajarilmagan.
    /// </summary>
    [Fact]
    public async Task Audit_jadvalidagi_before_va_after_ustunlari_jsonb()
    {
        await using var conn = new NpgsqlConnection(fixture.Database.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT column_name, udt_name
              FROM information_schema.columns
             WHERE table_name = 'audit_logs' AND column_name IN ('before', 'after')
             ORDER BY column_name
            """, conn);

        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                types[reader.GetString(0)] = reader.GetString(1);

        Assert.Equal("jsonb", types["after"]);
        Assert.Equal("jsonb", types["before"]);
    }

    /// <summary>
    /// Har bir jurnal partiyasi <c>audit_log</c> ga bitta qator qoldiradi, va
    /// <c>after</c> HAQIQIY JSON bo'ladi — uni <c>jsonb</c> operatorlari bilan
    /// o'qib bo'ladi (matn bo'lsa bu so'rov umuman ishlamasdi).
    /// </summary>
    [Fact]
    public async Task Jurnal_yozuvi_audit_qatorini_qoldiradi()
    {
        var actorId = await NewUserAsync(Roles.Admin);
        var refId = Guid.NewGuid();

        await using var db = NewDb();
        await new LedgerService(db).PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 120_000m,
                LedgerRefType.Payment, refId),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, 120_000m,
                LedgerRefType.Payment, refId),
        ], actorId);

        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(
            a => a.EntityType == "LedgerEntry" && a.EntityId == refId.ToString("D"));

        Assert.Equal("create", audit.Action);
        Assert.Equal(actorId, audit.ActorId);
        Assert.Null(audit.Before);

        // Snapshot: ikkita satr, summalar joyida.
        var rows = JsonDocument.Parse(audit.After!).RootElement;
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Contains(
            rows.EnumerateArray(),
            r => r.GetProperty("Account").GetString() == Accounts.Cash
                 && r.GetProperty("Amount").GetDecimal() == 120_000m);
    }

    /// <summary>
    /// Storno: <c>before</c> — original satrlar, <c>after</c> — ko'zgu satrlar.
    /// Ikkalasi ham to'ldiriladi, chunki jurnalda hech narsa o'chirilmaydi va
    /// "nima teskari qilindi" degan savol keyin ochiq qolmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Jurnal_stornosi_before_va_after_bilan_yoziladi()
    {
        var authorId = await NewUserAsync(Roles.Cashier);
        var approverId = await NewUserAsync(Roles.SuperAdmin);
        var refId = Guid.NewGuid();

        await using var db = NewDb();
        var posted = await new LedgerService(db).PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, 70_000m,
                LedgerRefType.Payment, refId),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, 70_000m,
                LedgerRefType.Payment, refId),
        ], authorId);

        await new LedgerService(db).ReverseAsync(posted[0].Id, "Xato summa", approverId);

        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(
            a => a.EntityType == "LedgerEntry"
                 && a.EntityId == refId.ToString("D")
                 && a.Action == "reverse");

        Assert.Equal(approverId, audit.ActorId);
        Assert.Contains("Xato summa", audit.Summary, StringComparison.Ordinal);
        Assert.Equal(2, JsonDocument.Parse(audit.Before!).RootElement.GetArrayLength());
        Assert.Equal(2, JsonDocument.Parse(audit.After!).RootElement.GetArrayLength());
    }

    /// <summary>
    /// To'lov qabul qilinganda audit qatori paydo bo'ladi va u TAQSIMOTLARNI
    /// ham saqlaydi — "pul qaysi oyga ketdi" degan savolga to'lov qatorining
    /// o'zi javob bermaydi.
    /// </summary>
    [Fact]
    public async Task Tolov_qabul_qilinganda_audit_qatori_taqsimotlar_bilan_yoziladi()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();

        await using var db = NewDb();
        var shift = await new CashShiftService(db).OpenAsync(cashierId, 0m);
        var invoiceId = await NewInvoiceAsync(db, studentId, 300_000m, InvoiceStatus.Open);

        var payments = new PaymentService(db, new CashShiftService(db), new LedgerService(db));
        var payment = await payments.AcceptAsync(
            new AcceptPaymentRequest(studentId, 300_000m, PaymentMethod.Cash, null,
                [new AllocationRequest(invoiceId, 300_000m)]),
            cashierId);

        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(
            a => a.EntityType == "Payment" && a.EntityId == payment.Id.ToString("D"));

        Assert.Equal("create", audit.Action);
        Assert.Equal(cashierId, audit.ActorId);
        Assert.Equal(studentId, audit.StudentId);

        var after = JsonDocument.Parse(audit.After!).RootElement;
        Assert.Equal(300_000m, after.GetProperty("Amount").GetDecimal());
        Assert.Equal(invoiceId, after.GetProperty("Allocations")[0].GetProperty("InvoiceId").GetGuid());
    }

    /// <summary>
    /// Chegirma yo'li (P1-08) allaqachon audit yozardi — bu test uning
    /// natijasi endi HAQIQATAN <c>jsonb</c> ustuniga tushishini tekshiradi:
    /// snapshot bazadagi JSON operatori bilan o'qiladi, C# tomonida emas.
    /// </summary>
    [Fact]
    public async Task Chegirma_snapshoti_bazada_jsonb_sifatida_oqiladi()
    {
        var adminId = await NewUserAsync(Roles.Admin);
        var studentId = await NewStudentAsync();

        await using var db = NewDb();
        var discounts = new DiscountService(
            db, new SchoolLms.Application.Services.AuditService(db, new HttpContextAccessor()));

        var discount = await discounts.CreateAsync(
            new CreateDiscountRequest(studentId, TuitionCategoryId, 10m, 0m, "Ko'p farzandli",
                AppClock.Today, null),
            adminId);

        // AYNAN jsonb operatori (`->>`). Ustun `text` bo'lganda bu so'rov
        // 42883 (operator does not exist) bilan yiqilardi.
        await using var conn = new NpgsqlConnection(fixture.Database.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT after->>'Percent' FROM audit_logs WHERE entity_id = @id AND action = 'create'",
            conn);
        cmd.Parameters.AddWithValue("id", discount.Id.ToString());

        Assert.Equal("10", (string?)await cmd.ExecuteScalarAsync());
    }

    // =====================================================================
    //  5-mezon — 50 000 nomuvofiqlik AYNAN bitta bayroq beradi
    // =====================================================================

    /// <summary>
    /// <b>P1-14 ning beshinchi qabul mezoni.</b> Ataylab 50 000 so'mlik
    /// nomuvofiqlik bilan yopilgan smena keyingi tekshiruvda AYNAN BITTA
    /// bayroq beradi — ko'p ham emas, kam ham emas.
    ///
    /// <para>
    /// Test ALOHIDA, toza bazada yuradi: "aynan bitta" degan da'vo butun
    /// jadval bo'sh bo'lgandagina isbotlanadi. Umumiy test bazasida boshqa
    /// testlar ham smena yopib, to'lov yozib ketadi.
    /// </para>
    /// <para>
    /// Ikkinchi yurish ham tekshiriladi: tungi ish har kuni bir xil oynani
    /// qayta ko'radi, ya'ni idempotentlik bu yerda "yoqimli xossa" emas,
    /// mezonning bir qismi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Ellik_ming_nomuvofiqlik_aynan_bitta_bayroq_beradi()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("variance");

        Guid shiftId;
        await using (var db = PostgresFixture.NewContext(database.OwnerConnectionString))
        {
            var cashier = new AppUser
            {
                FullName = "Nomuvofiqlik kassiri",
                Role = Roles.Cashier,
                Email = "variance." + Guid.NewGuid().ToString("N")[..8],
            };
            db.Users.Add(cashier);
            await db.SaveChangesAsync();

            // ATAYLAB 50 000 kamomad: kutilgan 500 000, sanalgan 450 000.
            // `variance` — bazada hisoblanadigan ustun, ilova unga yozmaydi.
            var shift = new CashShift
            {
                CashierId = cashier.Id,
                OpenedAt = AppClock.NowInstant.AddHours(-8),
                ClosedAt = AppClock.NowInstant.AddHours(-1),
                OpeningFloat = 0m,
                ExpectedCash = 500_000m,
                CountedCash = 450_000m,
                Status = CashShiftStatus.Closed,
                ClosedBy = cashier.Id,
            };
            db.CashShifts.Add(shift);
            await db.SaveChangesAsync();
            shiftId = shift.Id;
        }

        await using (var db = PostgresFixture.NewContext(database.OwnerConnectionString))
        {
            var first = await new AnomalyService(db).ScanAsync();
            Assert.Equal(1, first.Created);
            Assert.Equal(1, first.CreatedByKind[AnomalyKind.ShiftVariance]);
        }

        await using (var db = PostgresFixture.NewContext(database.OwnerConnectionString))
        {
            // Butun jadvalda AYNAN bitta qator.
            var flags = await db.FinanceAnomalyFlags.AsNoTracking().ToListAsync();
            var flag = Assert.Single(flags);

            Assert.Equal(AnomalyKind.ShiftVariance, flag.Kind);
            Assert.Equal(AnomalyRefType.CashShift, flag.RefType);
            Assert.Equal(shiftId, flag.RefId);
            Assert.Equal(-50_000m, flag.Amount);
            Assert.Null(flag.ResolvedAt);
            Assert.Contains("50 000", flag.Summary, StringComparison.Ordinal);

            // Ikkinchi yurish — dublikat YO'Q.
            var second = await new AnomalyService(db).ScanAsync();
            Assert.Equal(0, second.Created);
            Assert.Single(await db.FinanceAnomalyFlags.AsNoTracking().ToListAsync());
        }
    }

    // =====================================================================
    //  Qolgan uchta shart (SPEC §4.6)
    // =====================================================================

    /// <summary>
    /// 2-shart: storno original to'lovdan keyin 24 soat ichida qilingan.
    /// 25 soatdan keyingi storno bayroqlanmaydi — chegara aynan shu yerda
    /// sinaladi, aks holda test "har qanday storno" ni tasdiqlab qo'yardi.
    /// </summary>
    [Fact]
    public async Task Yigirma_tort_soat_ichidagi_storno_bayroqlanadi_kechrogi_yoq()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();

        await using var db = NewDb();
        var shift = await new CashShiftService(db).OpenAsync(cashierId, 0m);

        var now = AppClock.NowInstant;
        var fast = await NewReversalPairAsync(
            db, shift.Id, studentId, cashierId,
            originalAt: now.AddHours(-3), stornoAt: now.AddHours(-1));
        var slow = await NewReversalPairAsync(
            db, shift.Id, studentId, cashierId,
            originalAt: now.AddHours(-30), stornoAt: now.AddHours(-1));

        await new AnomalyService(db).ScanAsync();

        var flags = await db.FinanceAnomalyFlags.AsNoTracking()
            .Where(f => f.Kind == AnomalyKind.FastReversal)
            .Select(f => f.RefId)
            .ToListAsync();

        Assert.Contains(fast, flags);
        Assert.DoesNotContain(slow, flags);
    }

    /// <summary>
    /// 3-shart: ish soatlaridan (08:00–20:00, maktab mintaqasi) tashqarida
    /// yozilgan to'lov. Kunduzgi to'lov bayroqlanmaydi.
    /// </summary>
    [Fact]
    public async Task Ish_vaqtidan_tashqari_tolov_bayroqlanadi_kunduzgisi_yoq()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();

        await using var db = NewDb();
        var shift = await new CashShiftService(db).OpenAsync(cashierId, 0m);

        // Toshkent = UTC+5, yozgi vaqt yo'q. 22:00 UTC = 03:00 (tun), 07:00 UTC = 12:00 (kunduz).
        var today = AppClock.Today.AddDays(-1);
        var night = new DateTimeOffset(today.Year, today.Month, today.Day, 22, 0, 0, TimeSpan.Zero);
        var noon = new DateTimeOffset(today.Year, today.Month, today.Day, 7, 0, 0, TimeSpan.Zero);

        var nightPayment = await NewPaymentAsync(db, shift.Id, studentId, cashierId, 10_000m, night);
        var noonPayment = await NewPaymentAsync(db, shift.Id, studentId, cashierId, 10_000m, noon);

        await new AnomalyService(db).ScanAsync();

        var flags = await db.FinanceAnomalyFlags.AsNoTracking()
            .Where(f => f.Kind == AnomalyKind.OffHoursPayment)
            .Select(f => f.RefId)
            .ToListAsync();

        Assert.Contains(nightPayment, flags);
        Assert.DoesNotContain(noonPayment, flags);
    }

    /// <summary>
    /// 4-shart: <c>paid</c> hisob-faktura, taqsimotsiz.
    ///
    /// <para>
    /// To'lanadigan summasi 0 bo'lgan hisob-faktura (100% chegirma, bepul
    /// o'qish) BAYROQLANMAYDI: <c>InvoiceService.StatusFor</c> uni ataylab
    /// <c>paid</c> qiladi va bu qonuniy holat. Shu satr bo'lmasa tekshiruv
    /// har oyda o'nlab soxta bayroq berardi va panel ishonchini yo'qotardi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Taqsimotsiz_tolangan_hisob_faktura_bayroqlanadi_nol_summalisi_yoq()
    {
        var studentId = await NewStudentAsync();

        await using var db = NewDb();
        var broken = await NewInvoiceAsync(db, studentId, 400_000m, InvoiceStatus.Paid);
        var freeOfCharge = await NewInvoiceAsync(
            db, studentId, 400_000m, InvoiceStatus.Paid, discount: 400_000m);

        await new AnomalyService(db).ScanAsync();

        var flags = await db.FinanceAnomalyFlags.AsNoTracking()
            .Where(f => f.Kind == AnomalyKind.PaidWithoutAllocation)
            .Select(f => f.RefId)
            .ToListAsync();

        Assert.Contains(broken, flags);
        Assert.DoesNotContain(freeOfCharge, flags);
    }

    /// <summary>
    /// Storno qilingan to'lovning taqsimoti "haqiqiy" hisoblanmaydi
    /// (<c>PaymentService.EffectiveAllocations</c> ta'rifi). Ya'ni holat
    /// <c>paid</c> bo'lib qolgan hisob-faktura BAYROQLANADI — aynan shu
    /// tekshiruv topishi kerak bo'lgan holat.
    /// </summary>
    [Fact]
    public async Task Storno_qilingan_tolovning_taqsimoti_hisobga_olinmaydi()
    {
        var cashierId = await NewUserAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();

        await using var db = NewDb();
        var shift = await new CashShiftService(db).OpenAsync(cashierId, 0m);
        var invoiceId = await NewInvoiceAsync(db, studentId, 200_000m, InvoiceStatus.Paid);

        var original = await NewPaymentAsync(
            db, shift.Id, studentId, cashierId, 200_000m, AppClock.NowInstant.AddHours(-2));
        db.PaymentAllocations.Add(new PaymentAllocation
        {
            PaymentId = original,
            InvoiceId = invoiceId,
            Amount = 200_000m,
        });
        await db.SaveChangesAsync();

        // Storno qatori — taqsimotni O'CHIRMAYDI (jadval o'zgarmas), lekin uni
        // hisobdan chiqaradi.
        await NewPaymentAsync(
            db, shift.Id, studentId, cashierId, 200_000m, AppClock.NowInstant.AddHours(-1),
            reversalOf: original);

        await new AnomalyService(db).ScanAsync();

        Assert.True(await db.FinanceAnomalyFlags.AsNoTracking().AnyAsync(
            f => f.Kind == AnomalyKind.PaidWithoutAllocation && f.RefId == invoiceId));
    }

    // =====================================================================
    //  3-mezon — yopish faqat yozma sabab bilan, o'chirish YO'Q
    // =====================================================================

    /// <summary>
    /// SPEC §4.6: bayroqni "bekor qilib bo'lmaydi, faqat yozma sabab bilan
    /// yopiladi". Bo'sh sabab ham, faqat probel ham — 400.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task Bosh_sabab_bilan_bayroqni_yopib_bolmaydi(string reason)
    {
        var adminId = await NewUserAsync(Roles.Admin);

        await using var db = NewDb();
        var flagId = await NewFlagAsync(db);

        var ex = await Assert.ThrowsAsync<BillingRuleException>(
            () => new AnomalyService(db).ResolveAsync(flagId, reason, adminId));

        Assert.Equal(BillingFault.Invalid, ex.Fault);
        Assert.Equal("reason_required", ex.Code);

        // Va bayroq HALI HAM ochiq.
        Assert.Null(await db.FinanceAnomalyFlags.AsNoTracking()
            .Where(f => f.Id == flagId).Select(f => f.ResolvedAt).SingleAsync());
    }

    /// <summary>
    /// Bo'sh sababni ILOVA chetlab o'tilsa ham baza to'xtatadi
    /// (<c>ck_finance_anomaly_flags_resolution</c>). Bu qavat muhim: ilova
    /// tekshiruvini kelajakdagi kod yoki qo'lda yozilgan <c>UPDATE</c>
    /// aylanib o'tishi mumkin, bu esa yo'q.
    /// </summary>
    [Fact]
    public async Task Bosh_sababni_baza_ham_rad_etadi()
    {
        var adminId = await NewUserAsync(Roles.Admin);

        await using var db = NewDb();
        var flagId = await NewFlagAsync(db);

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var conn = new NpgsqlConnection(fixture.Database.OwnerConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE finance_anomaly_flags
                   SET resolved_at = now(), resolved_by = @by, resolved_reason = '   '
                 WHERE id = @id
                """, conn);
            cmd.Parameters.AddWithValue("by", adminId);
            cmd.Parameters.AddWithValue("id", flagId);
            await cmd.ExecuteNonQueryAsync();
        });

        Assert.Equal("23514", ex.SqlState);
        Assert.Contains("ck_finance_anomaly_flags_resolution", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Yopish: sabab, yopgan shaxs va vaqt saqlanadi, hisoblagichdan chiqadi
    /// va <c>audit_log</c> ga o'z qatori tushadi.
    /// </summary>
    [Fact]
    public async Task Bayroq_yozma_sabab_bilan_yopiladi_va_audit_qoldiradi()
    {
        var (admin, _) = await fixture.Api.SeedUserAsync(Roles.Admin);

        await using var db = NewDb();
        var flagId = await NewFlagAsync(db);

        var resolved = await new AnomalyService(db).ResolveAsync(
            flagId, "  Kassir pulni ertasiga topshirdi, kvitansiya bor  ", admin.Id);

        Assert.NotNull(resolved.ResolvedAt);
        Assert.Equal(admin.Id, resolved.ResolvedBy);
        Assert.Equal(admin.FullName, resolved.ResolvedByName);
        // Sabab TRIM qilinadi — atrofdagi probel saqlanmaydi.
        Assert.Equal("Kassir pulni ertasiga topshirdi, kvitansiya bor", resolved.ResolvedReason);

        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(
            a => a.EntityType == "AnomalyFlag" && a.EntityId == flagId.ToString("D"));
        Assert.Equal("resolve", audit.Action);
        Assert.Equal(admin.Id, audit.ActorId);
        Assert.Null(JsonDocument.Parse(audit.Before!).RootElement
            .GetProperty("ResolvedAt").GetString());
    }

    /// <summary>Yopilgan bayroqni qayta yopib bo'lmaydi — 409.</summary>
    [Fact]
    public async Task Yopilgan_bayroqni_qayta_yopib_bolmaydi()
    {
        var adminId = await NewUserAsync(Roles.Admin);

        await using var db = NewDb();
        var flagId = await NewFlagAsync(db);
        var service = new AnomalyService(db);

        await service.ResolveAsync(flagId, "Birinchi sabab", adminId);

        var ex = await Assert.ThrowsAsync<BillingRuleException>(
            () => service.ResolveAsync(flagId, "Ikkinchi sabab", adminId));

        Assert.Equal(BillingFault.Conflict, ex.Fault);
        Assert.Equal("flag_already_resolved", ex.Code);
    }

    /// <summary>
    /// SPEC §4.6: bayroqni O'CHIRIB bo'lmaydi. Controller'da DELETE
    /// (va PUT/PATCH) endpoint'i bo'lmasligi — shartnomaning bir qismi,
    /// <c>CashShiftsController</c> dagi bilan bir xil mezon.
    /// </summary>
    [Fact]
    public async Task Bayroq_endpointlarida_DELETE_yoq()
    {
        var attributes = typeof(FinanceFlagsController)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .SelectMany(m => m.GetCustomAttributes(inherit: true))
            .Select(a => a.GetType().Name)
            .ToList();

        Assert.DoesNotContain("HttpDeleteAttribute", attributes);
        Assert.DoesNotContain("HttpPutAttribute", attributes);
        Assert.DoesNotContain("HttpPatchAttribute", attributes);

        // Va `IAnomalyService` da ham o'chirish metodi yo'q.
        Assert.DoesNotContain(
            typeof(IAnomalyService).GetMethods(),
            m => m.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                 || m.Name.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }

    // =====================================================================
    //  4-mezon — endpoint va RBAC (SPEC §4.3)
    // =====================================================================

    /// <summary>
    /// <c>GET /api/admin/finance/flags?unresolved=true</c> — direktor paneli
    /// shu javobdan hisoblagichni oladi. Hisoblagich RO'YXATDAN MUSTAQIL:
    /// <c>limit=1</c> bo'lsa ham to'liq son qaytadi.
    /// </summary>
    [Fact]
    public async Task Bayroqlar_endpointi_hisoblagichni_qaytaradi()
    {
        await using (var db = NewDb())
        {
            await NewFlagAsync(db);
            await NewFlagAsync(db);
        }

        var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await client.GetAsync("/api/admin/finance/flags?unresolved=true&limit=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<AnomalyFlagsDto>())!;

        Assert.True(body.Unresolved >= 2);
        Assert.True(body.Total >= body.Unresolved);
        // Ro'yxat chegaralangan, hisoblagich esa yo'q.
        Assert.Single(body.Items);
        // Turlar kesimi HAR DOIM to'rt qator — UI shakli barqaror bo'lsin.
        Assert.Equal(AnomalyKind.All.Count, body.ByKind.Count);
        Assert.All(body.ByKind, k => Assert.False(string.IsNullOrWhiteSpace(k.KindLabel)));
    }

    /// <summary>
    /// SPEC §4.3: kassir "See variance report across cashiers" qatorida ⛔.
    /// Tekshirilayotgan odam tekshiruvni ko'ra ham, yopa ham olmaydi.
    /// </summary>
    [Fact]
    public async Task Kassir_bayroqlarni_kora_olmaydi()
    {
        var client = await fixture.Api.ClientAsAsync(Roles.Cashier);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/admin/finance/flags")).StatusCode);

        var resolve = await client.PostAsJsonAsync(
            $"/api/admin/finance/flags/{Guid.NewGuid()}/resolve", new { reason = "sabab" });
        Assert.Equal(HttpStatusCode.Forbidden, resolve.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsync("/api/admin/finance/flags/scan", null)).StatusCode);
    }

    /// <summary>Token'siz — 401 (rol darvozasidan oldin).</summary>
    [Fact]
    public async Task Tokensiz_bayroqlar_401_beradi()
    {
        var anonymous = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/admin/finance/flags")).StatusCode);
    }

    /// <summary>
    /// HTTP orqali yopish: bo'sh sabab — 400, to'g'ri sabab — 200,
    /// takrori — 409, mavjud bo'lmagan bayroq — 404. To'rttasi bitta testda,
    /// chunki ular BITTA shartnomaning to'rtta tomoni.
    /// </summary>
    [Fact]
    public async Task Yopish_endpointi_status_kodlarini_togri_beradi()
    {
        Guid flagId;
        await using (var db = NewDb()) flagId = await NewFlagAsync(db);

        var client = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);

        var empty = await client.PostAsJsonAsync(
            $"/api/admin/finance/flags/{flagId}/resolve", new { reason = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("reason_required",
            (await empty.Content.ReadFromJsonAsync<BillingErrorDto>())!.Code);

        var missing = await client.PostAsJsonAsync(
            $"/api/admin/finance/flags/{Guid.NewGuid()}/resolve", new { reason = "sabab" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var ok = await client.PostAsJsonAsync(
            $"/api/admin/finance/flags/{flagId}/resolve", new { reason = "Tekshirildi, xato yo'q" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Tekshirildi, xato yo'q",
            (await ok.Content.ReadFromJsonAsync<AnomalyFlagDto>())!.ResolvedReason);

        var again = await client.PostAsJsonAsync(
            $"/api/admin/finance/flags/{flagId}/resolve", new { reason = "Yana bir bor" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    /// <summary>Noma'lum tur — 400, 500 emas.</summary>
    [Fact]
    public async Task Nomalum_tur_400_beradi()
    {
        var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync("/api/admin/finance/flags?kind=hech-nima");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_kind",
            (await response.Content.ReadFromJsonAsync<BillingErrorDto>())!.Code);
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
        var id = "stu-" + Guid.NewGuid().ToString("N")[..12];

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(new Student
            {
                Id = id,
                FullName = "Tekshiruv o'quvchisi " + id[^6..],
                LastName = "Tekshiruv",
                FirstName = "O'quvchi",
                ClassName = "1-A",
                EnrollmentDate = AppClock.Today.ToString("yyyy-MM-dd"),
            });
            await db.SaveChangesAsync();
        });

        return id;
    }

    /// <summary>Hisob-faktura. Oy har chaqiruvda boshqa — unikal indeks urilmasin.</summary>
    private static int monthCounter;

    private static async Task<Guid> NewInvoiceAsync(
        AppDbContext db, string studentId, decimal amount, string status, decimal discount = 0m)
    {
        var month = new DateOnly(AppClock.Today.Year, 1, 1)
            .AddMonths(Interlocked.Increment(ref monthCounter) % 12);

        var invoice = new Invoice
        {
            StudentId = studentId,
            CategoryId = TuitionCategoryId,
            PeriodMonth = month,
            Amount = amount,
            Discount = discount,
            DueOn = month.AddDays(9),
            Status = status,
            CreatedAt = AppClock.NowInstant,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    /// <summary>Chek raqami smena ichida unikal — hisoblagich shu uchun.</summary>
    private static long receiptCounter = 1_000;

    private static async Task<Guid> NewPaymentAsync(
        AppDbContext db, Guid shiftId, string studentId, string cashierId,
        decimal amount, DateTimeOffset receivedAt, Guid? reversalOf = null)
    {
        var payment = new Payment
        {
            ReceiptNo = Interlocked.Increment(ref receiptCounter),
            StudentId = studentId,
            Amount = amount,
            Method = PaymentMethod.Cash,
            CashShiftId = shiftId,
            CashierId = cashierId,
            ReceivedAt = receivedAt,
            ReversalOf = reversalOf,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return payment.Id;
    }

    /// <summary>Original + storno juftligi; storno id'sini qaytaradi (bayroq unga tegishli).</summary>
    private static async Task<Guid> NewReversalPairAsync(
        AppDbContext db, Guid shiftId, string studentId, string cashierId,
        DateTimeOffset originalAt, DateTimeOffset stornoAt)
    {
        var original = await NewPaymentAsync(db, shiftId, studentId, cashierId, 50_000m, originalAt);
        return await NewPaymentAsync(
            db, shiftId, studentId, cashierId, 50_000m, stornoAt, reversalOf: original);
    }

    /// <summary>
    /// Yopish testlari uchun tayyor bayroq. Tekshiruvni ishga tushirmaydi —
    /// bu testlar YOPISH oqimini sinaydi, topish oqimini emas.
    /// </summary>
    private static async Task<Guid> NewFlagAsync(AppDbContext db)
    {
        var flag = new FinanceAnomalyFlag
        {
            Kind = AnomalyKind.ShiftVariance,
            RefType = AnomalyRefType.CashShift,
            RefId = Guid.NewGuid(),
            OccurredAt = AppClock.NowInstant,
            DetectedAt = AppClock.NowInstant,
            Amount = -50_000m,
            Summary = "Test bayrog'i",
        };
        db.FinanceAnomalyFlags.Add(flag);
        await db.SaveChangesAsync();
        return flag.Id;
    }
}
