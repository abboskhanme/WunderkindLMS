using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// To'lov qabul qilish, taqsimlash va storno (P1-11) — SPEC §3.7, §4.
///
/// <para>
/// ENG MUHIM TEST: <see cref="Maktab_va_avtobus_bitta_tolovda_bitta_chek_va_ikkita_taqsimot_beradi"/>.
/// Bu SPEC §6 ning "tayyor deb hisoblanadi" jumlasi — bitta to'lov ikki
/// toifaga taqsimlanadi, chek esa bitta bo'ladi.
/// </para>
/// <para>
/// Testlar ikki qavatda: HTTP orqali (marshrut, rol darvozasi, status kodlari,
/// §4.4 shaxs tekshiruvi) va baza orqali (tranzaksiya natijasi, jurnal
/// qatorlari, trigger). Ikkalasi ham kerak: HTTP'siz rol darvozasi sinalmaydi,
/// bazasiz esa "aynan nima yozildi" degan savol ochiq qoladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class PaymentsTests(ApiFixture fixture)
{
    // -----------------------------------------------------------------
    //  Moliya xizmatlari ulangan host (P1-15 gacha vaqtinchalik)
    // -----------------------------------------------------------------
    //
    //  `Program.cs` P1-15 ning fayli, shuning uchun moliya xizmatlari hali
    //  DI'da yo'q (docs/PENDING_WIRING.md). HTTP testlari uchun host'ni
    //  SHU uchta xizmat ulangan holda qayta ko'taramiz — bazasi o'sha,
    //  ApiFixture'niki. BIR MARTA: host ko'tarish ~1 s, har test uchun
    //  takrorlash butun to'plamni sekinlashtirardi.
    //
    //  `ICashShiftService` — P1-10 ning ishi, u parallel yozilyapti. Bu yerda
    //  uning o'rnini <see cref="ShiftDouble"/> bosadi: P1-11 INTERFEYSGA
    //  tayanadi, implementatsiyaga emas.

    private static WebApplicationFactory<AuthController>? _wired;
    private static readonly Lock WiredLock = new();

    private static WebApplicationFactory<AuthController> Wired(ApiFixture fixture)
    {
        lock (WiredLock)
            return _wired ??= fixture.Api.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.AddScoped<ILedgerService, LedgerService>();
                    services.AddScoped<ICashShiftService, ShiftDouble>();
                    services.AddScoped<IPaymentService, PaymentService>();
                }));
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // -----------------------------------------------------------------
    //  ENG MUHIM TEST — SPEC §6
    // -----------------------------------------------------------------

    /// <summary>
    /// Bitta to'lov: o'qish 500 000 + avtobus 200 000. Natija — BITTA chek
    /// raqami, IKKITA taqsimot qatori, AYNAN IKKITA jurnal yozuvi va ikkala
    /// hisob-fakturaning statusi <c>paid</c>.
    /// </summary>
    [Fact]
    public async Task Maktab_va_avtobus_bitta_tolovda_bitta_chek_va_ikkita_taqsimot_beradi()
    {
        var world = await NewWorldAsync();
        var tuition = await NewInvoiceAsync(world.StudentId, "tuition", 500_000m);
        var bus = await NewInvoiceAsync(world.StudentId, "bus", 200_000m);

        var response = await world.Client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId = world.StudentId,
            amount = 700_000m,
            method = PaymentMethod.Cash,
            note = "Sentyabr — o'qish va avtobus",
            allocations = new[]
            {
                new { invoiceId = tuition, amount = 500_000m },
                new { invoiceId = bus, amount = 200_000m },
            },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(dto);

        // --- Chek BITTA ---
        Assert.True(dto.ReceiptNo > 0, "Chek raqami berilmadi.");
        Assert.Equal(700_000m, dto.Amount);
        Assert.Equal(0m, dto.Unallocated);

        // --- Taqsimot IKKITA, toifalari ko'rinib turadi ---
        Assert.Equal(2, dto.Allocations.Count);
        Assert.Contains(dto.Allocations, a => a.CategoryCode == "tuition" && a.Amount == 500_000m);
        Assert.Contains(dto.Allocations, a => a.CategoryCode == "bus" && a.Amount == 200_000m);

        // --- Bazada haqiqatan shunday ---
        await using var db = NewDb();

        var payment = Assert.Single(await db.Payments.AsNoTracking()
            .Where(p => p.StudentId == world.StudentId).ToListAsync());
        Assert.Equal(dto.Id, payment.Id);
        Assert.Equal(world.CashierId, payment.CashierId);        // §4.4 — JWT'dan
        Assert.Equal(world.ShiftId, payment.CashShiftId);        // smena serverda aniqlangan
        Assert.Equal(TimeSpan.Zero, payment.ReceivedAt.Offset);  // timestamptz — lahza

        var allocations = await db.PaymentAllocations.AsNoTracking()
            .Where(a => a.PaymentId == payment.Id).ToListAsync();
        Assert.Equal(2, allocations.Count);
        Assert.Equal(700_000m, allocations.Sum(a => a.Amount));

        // --- Jurnal: AYNAN ikki qator (taqsimot nechta bo'lsa ham) ---
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefId == payment.Id).ToListAsync();
        Assert.Equal(2, entries.Count);

        var debit = Assert.Single(entries, e => e.Direction == LedgerDirection.Debit);
        Assert.Equal(Accounts.Cash, debit.Account);
        Assert.Equal(700_000m, debit.Amount);

        var credit = Assert.Single(entries, e => e.Direction == LedgerDirection.Credit);
        Assert.Equal(Accounts.Receivable, credit.Account);
        Assert.Equal(700_000m, credit.Amount);

        // --- Hisob-faktura statuslari yangilandi ---
        var statuses = await db.Invoices.AsNoTracking()
            .Where(i => i.StudentId == world.StudentId)
            .ToDictionaryAsync(i => i.Id, i => i.Status);
        Assert.Equal(InvoiceStatus.Paid, statuses[tuition]);
        Assert.Equal(InvoiceStatus.Paid, statuses[bus]);
    }

    // -----------------------------------------------------------------
    //  SPEC §4.2 — ochiq smenasiz pul yozilmaydi
    // -----------------------------------------------------------------

    [Fact]
    public async Task Ochiq_smena_yoq_bolsa_409_no_open_shift_va_hech_qanday_pul_qatori_yozilmaydi()
    {
        // Smena ATAYLAB ochilmaydi.
        var (cashier, client) = await ActorAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();
        var invoiceId = await NewInvoiceAsync(studentId, "tuition", 500_000m);

        var response = await client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId,
            amount = 500_000m,
            method = PaymentMethod.Cash,
            allocations = new[] { new { invoiceId, amount = 500_000m } },
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("no_open_shift", error?.Code);

        // Eng muhimi: BIRORTA pul qatori yozilmagan.
        await using var db = NewDb();
        Assert.False(await db.Payments.AnyAsync(p => p.StudentId == studentId));
        Assert.False(await db.PaymentAllocations.AnyAsync(a => a.InvoiceId == invoiceId));
        Assert.False(await db.LedgerEntries.AnyAsync(e => e.CreatedBy == cashier.Id));
        Assert.Equal(InvoiceStatus.Open,
            await db.Invoices.Where(i => i.Id == invoiceId).Select(i => i.Status).SingleAsync());
    }

    // -----------------------------------------------------------------
    //  SPEC §4.4 — shaxs so'rov tanasida yuborilmaydi
    // -----------------------------------------------------------------

    /// <summary>
    /// So'rov tanasidagi <c>cashierId</c> JIMGINA E'TIBORSIZ QOLDIRILMAYDI:
    /// aks holda kassir "boshqa odam nomiga yozdim" deb o'ylab yuradi, yozuv
    /// esa o'z nomiga tushgan bo'ladi. Alias marshrut
    /// (<c>/api/cashier/payments</c>) ham shu yerda sinaladi.
    /// </summary>
    [Fact]
    public async Task Sorov_tanasidagi_cashierId_400_beradi_jimgina_etiborsiz_qoldirilmaydi()
    {
        var world = await NewWorldAsync();
        var invoiceId = await NewInvoiceAsync(world.StudentId, "tuition", 500_000m);
        var (other, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);

        var response = await world.Client.PostAsJsonAsync("/api/cashier/payments", new
        {
            studentId = world.StudentId,
            amount = 500_000m,
            method = PaymentMethod.Cash,
            cashierId = other.Id,                       // ← taqiqlangan maydon
            allocations = new[] { new { invoiceId, amount = 500_000m } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("identity_in_body", error?.Code);

        await using var db = NewDb();
        Assert.False(await db.Payments.AnyAsync(p => p.StudentId == world.StudentId));
    }

    [Fact]
    public async Task Sorov_tanasidagi_cashShiftId_ham_400_beradi()
    {
        var world = await NewWorldAsync();

        var response = await world.Client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId = world.StudentId,
            amount = 100_000m,
            method = PaymentMethod.Cash,
            cashShiftId = Guid.NewGuid(),               // ← smenani ham klient tanlamaydi
            allocations = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("identity_in_body", error?.Code);
    }

    // -----------------------------------------------------------------
    //  Taqsimot invarianti — IKKALA yo'l (xizmat va baza)
    // -----------------------------------------------------------------

    [Fact]
    public async Task Taqsimot_yigindisi_summadan_oshsa_xizmat_400_beradi()
    {
        var world = await NewWorldAsync();
        var tuition = await NewInvoiceAsync(world.StudentId, "tuition", 500_000m);
        var bus = await NewInvoiceAsync(world.StudentId, "bus", 200_000m);

        var response = await world.Client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId = world.StudentId,
            amount = 500_000m,
            method = PaymentMethod.Cash,
            allocations = new[]
            {
                new { invoiceId = tuition, amount = 400_000m },
                new { invoiceId = bus, amount = 200_000m },     // jami 600 000 > 500 000
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("allocation_exceeds_amount", error?.Code);

        await using var db = NewDb();
        Assert.False(await db.Payments.AnyAsync(p => p.StudentId == world.StudentId));
    }

    /// <summary>
    /// Xizmat CHETLAB O'TILSA ham invariant saqlanadi: baza trigger'i
    /// (<c>payment_allocations_total</c>) qatorni rad etadi. Bu yerda ataylab
    /// <c>PaymentService</c> emas, to'g'ridan-to'g'ri <c>DbContext</c> yozadi.
    /// </summary>
    [Fact]
    public async Task Xizmat_chetlab_otilsa_taqsimotni_baza_triggeri_bloklaydi()
    {
        var world = await NewWorldAsync();
        var tuition = await NewInvoiceAsync(world.StudentId, "tuition", 500_000m);
        var bus = await NewInvoiceAsync(world.StudentId, "bus", 200_000m);

        await using var db = NewDb();

        var payment = new Payment
        {
            ReceiptNo = 1,
            StudentId = world.StudentId,
            Amount = 500_000m,
            Method = PaymentMethod.Cash,
            CashShiftId = world.ShiftId,
            CashierId = world.CashierId,
            ReceivedAt = AppClock.NowInstant,
        };
        db.Payments.Add(payment);
        db.PaymentAllocations.Add(new PaymentAllocation
        {
            PaymentId = payment.Id,
            InvoiceId = tuition,
            Amount = 400_000m,
        });
        await db.SaveChangesAsync();

        // 400 000 + 200 000 > 500 000 → trigger.
        db.PaymentAllocations.Add(new PaymentAllocation
        {
            PaymentId = payment.Id,
            InvoiceId = bus,
            Amount = 200_000m,
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("Allocation exceeds payment amount",
            ex.InnerException?.Message ?? ex.Message, StringComparison.Ordinal);

        await using var check = NewDb();
        Assert.Equal(400_000m, await check.PaymentAllocations.AsNoTracking()
            .Where(a => a.PaymentId == payment.Id).SumAsync(a => a.Amount));
    }

    [Fact]
    public async Task Hisob_faktura_qoldigidan_ortiq_taqsimot_400_beradi()
    {
        var world = await NewWorldAsync();
        var tuition = await NewInvoiceAsync(world.StudentId, "tuition", 500_000m);

        var response = await world.Client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId = world.StudentId,
            amount = 600_000m,
            method = PaymentMethod.Cash,
            allocations = new[] { new { invoiceId = tuition, amount = 600_000m } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("allocation_exceeds_invoice", error?.Code);
    }

    // -----------------------------------------------------------------
    //  Taqsimlanmagan qoldiq = kredit, o'zgaruvchan balans ustuni EMAS
    // -----------------------------------------------------------------

    [Fact]
    public async Task Taqsimlanmagan_qoldiq_ruxsat_etiladi_va_balans_ustuniga_yozilmaydi()
    {
        var world = await NewWorldAsync();
        var tuition = await NewInvoiceAsync(world.StudentId, "tuition", 400_000m);

        var response = await world.Client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId = world.StudentId,
            amount = 1_000_000m,
            method = PaymentMethod.Cash,
            allocations = new[] { new { invoiceId = tuition, amount = 400_000m } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.Equal(600_000m, dto!.Unallocated);

        await using var db = NewDb();

        // Eski `students.balance` ustuni TEGILMAGAN: qarz ham, kredit ham
        // hisob-faktura va taqsimotlardan hisoblanadi (SPEC §3.7).
        Assert.Equal(0m, await db.Students.AsNoTracking()
            .Where(s => s.Id == world.StudentId).Select(s => s.Balance).SingleAsync());

        // Jurnal to'liq summani oladi: qarz shuncha kamayadi, ortig'i avans.
        Assert.Equal(1_000_000m, await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefId == dto.Id && e.Direction == LedgerDirection.Debit)
            .SumAsync(e => e.Amount));
    }

    /// <summary>
    /// Mijoz javobi (SPEC §8.1 Q13): usul FAQAT YORLIQ, lekin naqddan boshqasi
    /// bankka tushadi — kassada sanalmaydi.
    /// </summary>
    [Fact]
    public async Task Karta_bilan_tolov_bank_hisobiga_tushadi()
    {
        var world = await NewWorldAsync();
        var tuition = await NewInvoiceAsync(world.StudentId, "tuition", 300_000m);

        await using var db = NewDb();
        var service = new PaymentService(db, new ShiftDouble(db), new LedgerService(db));

        var dto = await service.AcceptAsync(
            new AcceptPaymentRequest(world.StudentId, 300_000m, PaymentMethod.Card, null,
                [new AllocationRequest(tuition, 300_000m)]),
            world.CashierId);

        await using var check = NewDb();
        var debit = await check.LedgerEntries.AsNoTracking()
            .SingleAsync(e => e.RefId == dto.Id && e.Direction == LedgerDirection.Debit);

        Assert.Equal(Accounts.Bank, debit.Account);
    }

    // -----------------------------------------------------------------
    //  Storno
    // -----------------------------------------------------------------

    /// <summary>
    /// Storno YANGI qator qo'shadi (<c>reversal_of</c>) va jurnalga ko'zgu
    /// yozuvlarini yozadi. Original qator TEGILMAYDI, hisob-faktura statusi
    /// esa qaytib ochiladi.
    /// </summary>
    [Fact]
    public async Task Storno_yangi_qator_va_teskari_jurnal_yozuvlarini_yaratadi_originalga_tegmaydi()
    {
        var world = await NewWorldAsync();
        var tuition = await NewInvoiceAsync(world.StudentId, "tuition", 500_000m);
        var original = await AcceptAsync(world, tuition, 500_000m);

        var (admin, adminClient) = await ActorAsync(Roles.Admin);
        await OpenShiftAsync(admin.Id);

        var response = await adminClient.PostAsJsonAsync(
            $"/api/admin/payments/{original.Id}/reverse",
            new { reason = "Kassir summani xato kiritdi" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var storno = await response.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(storno);
        Assert.Equal(original.Id, storno.ReversalOf);
        Assert.Equal(original.Amount, storno.Amount);
        Assert.Equal(admin.Id, storno.CashierId);
        Assert.True(storno.ReceiptNo > 0, "Storno ham chek raqamini oladi.");

        await using var db = NewDb();

        // Original TEGILMAGAN (o'zgarmaslik — SPEC §4.1).
        var kept = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == original.Id);
        Assert.Equal(500_000m, kept.Amount);
        Assert.Equal(world.CashierId, kept.CashierId);
        Assert.Null(kept.ReversalOf);

        // Jurnal: 2 ta original + 2 ta ko'zgu.
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefId == original.Id).ToListAsync();
        Assert.Equal(4, entries.Count);

        var mirrors = entries.Where(e => e.RefType == LedgerRefType.Reversal).ToList();
        Assert.Equal(2, mirrors.Count);
        Assert.All(mirrors, m => Assert.NotNull(m.ReversalOf));
        Assert.All(mirrors, m => Assert.Equal(admin.Id, m.CreatedBy));
        // Kassa hisobi endi kredit tomonida — pul qaytdi.
        Assert.Contains(mirrors, m => m.Account == Accounts.Cash && m.Direction == LedgerDirection.Credit);
        Assert.Contains(mirrors, m => m.Account == Accounts.Receivable && m.Direction == LedgerDirection.Debit);

        // Hisob-faktura yana ochiq: storno qilingan to'lov "to'langan" deb
        // hisoblanmaydi, taqsimot qatori esa o'chirilmaydi (u tarix).
        Assert.Equal(InvoiceStatus.Open,
            await db.Invoices.Where(i => i.Id == tuition).Select(i => i.Status).SingleAsync());
        Assert.True(await db.PaymentAllocations.AnyAsync(a => a.PaymentId == original.Id));

        // Originalni o'qiganda storno havolasi ko'rinadi.
        var reloaded = await adminClient.GetFromJsonAsync<PaymentDto>(
            $"/api/billing/payments/{original.Id}");
        Assert.Equal(storno.Id, reloaded!.ReversedBy);
    }

    [Fact]
    public async Task Kassir_storno_qila_olmaydi_403()
    {
        var world = await NewWorldAsync();
        var tuition = await NewInvoiceAsync(world.StudentId, "tuition", 500_000m);
        var original = await AcceptAsync(world, tuition, 500_000m);

        var response = await world.Client.PostAsJsonAsync(
            $"/api/admin/payments/{original.Id}/reverse", new { reason = "Xato" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = NewDb();
        Assert.False(await db.Payments.AnyAsync(p => p.ReversalOf == original.Id));
    }

    [Fact]
    public async Task Tokensiz_sorov_401_beradi()
    {
        var anonymous = Wired(fixture).CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId = "yoq",
            amount = 1_000m,
            method = PaymentMethod.Cash,
            allocations = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ikki_marta_storno_409_beradi()
    {
        var world = await NewWorldAsync();
        var tuition = await NewInvoiceAsync(world.StudentId, "tuition", 500_000m);
        var original = await AcceptAsync(world, tuition, 500_000m);

        var (admin, adminClient) = await ActorAsync(Roles.Admin);
        await OpenShiftAsync(admin.Id);

        var first = await adminClient.PostAsJsonAsync(
            $"/api/admin/payments/{original.Id}/reverse", new { reason = "Birinchi storno" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await adminClient.PostAsJsonAsync(
            $"/api/admin/payments/{original.Id}/reverse", new { reason = "Ikkinchi urinish" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = await second.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("already_reversed", error?.Code);

        await using var db = NewDb();
        Assert.Single(await db.Payments.AsNoTracking()
            .Where(p => p.ReversalOf == original.Id).ToListAsync());
    }

    [Fact]
    public async Task Storno_sababsiz_400_beradi()
    {
        var world = await NewWorldAsync();
        var tuition = await NewInvoiceAsync(world.StudentId, "tuition", 500_000m);
        var original = await AcceptAsync(world, tuition, 500_000m);

        var (admin, adminClient) = await ActorAsync(Roles.Admin);
        await OpenShiftAsync(admin.Id);

        var response = await adminClient.PostAsJsonAsync(
            $"/api/admin/payments/{original.Id}/reverse", new { reason = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("reason_required", error?.Code);

        await using var db = NewDb();
        Assert.False(await db.Payments.AnyAsync(p => p.ReversalOf == original.Id));
    }

    /// <summary>
    /// SPEC §4.5 — ikki qavatli nazorat. Admin ham kassaga tura oladi, lekin
    /// O'ZI qabul qilgan to'lovni O'ZI storno qila olmaydi: "pulni oldim,
    /// keyin o'chirdim" bitta odamning qo'lida qolmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Ozi_qabul_qilgan_tolovni_ozi_storno_qila_olmaydi_403()
    {
        var (admin, adminClient) = await ActorAsync(Roles.Admin);
        await OpenShiftAsync(admin.Id);
        var studentId = await NewStudentAsync();
        var tuition = await NewInvoiceAsync(studentId, "tuition", 300_000m);

        var accepted = await adminClient.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId,
            amount = 300_000m,
            method = PaymentMethod.Cash,
            allocations = new[] { new { invoiceId = tuition, amount = 300_000m } },
        });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var payment = await accepted.Content.ReadFromJsonAsync<PaymentDto>();

        var response = await adminClient.PostAsJsonAsync(
            $"/api/admin/payments/{payment!.Id}/reverse", new { reason = "O'zim yozgan edim" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("own_payment_reversal", error?.Code);
    }

    // -----------------------------------------------------------------
    //  O'qish va taqsimot taklifi
    // -----------------------------------------------------------------

    [Fact]
    public async Task Kassir_boshqa_kassirning_tolovini_kormaydi()
    {
        var world = await NewWorldAsync();
        var tuition = await NewInvoiceAsync(world.StudentId, "tuition", 500_000m);
        var original = await AcceptAsync(world, tuition, 500_000m);

        var (_, otherCashier) = await ActorAsync(Roles.Cashier);

        var mine = await world.Client.GetAsync($"/api/billing/payments/{original.Id}");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);

        var theirs = await otherCashier.GetAsync($"/api/billing/payments/{original.Id}");
        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);

        // Ro'yxat ham majburan o'ziniki bilan cheklanadi.
        var theirList = await otherCashier.GetFromJsonAsync<List<PaymentDto>>(
            $"/api/billing/payments?studentId={world.StudentId}");
        Assert.Empty(theirList!);

        // Admin esa hammasini ko'radi.
        var (_, adminClient) = await ActorAsync(Roles.Admin);
        var adminList = await adminClient.GetFromJsonAsync<List<PaymentDto>>(
            $"/api/billing/payments?studentId={world.StudentId}");
        Assert.Single(adminList!);
    }

    [Fact]
    public async Task Taklif_eng_eski_oydan_boshlab_taqsimlaydi()
    {
        var world = await NewWorldAsync();
        var older = await NewInvoiceAsync(world.StudentId, "tuition", 500_000m, monthOffset: -1);
        var newer = await NewInvoiceAsync(world.StudentId, "tuition", 500_000m);

        var suggestion = await world.Client.GetFromJsonAsync<List<AllocationSuggestionDto>>(
            $"/api/cash/payments/suggest-allocation?studentId={world.StudentId}&amount=600000");

        Assert.NotNull(suggestion);
        Assert.Equal(2, suggestion.Count);
        Assert.Equal(older, suggestion[0].InvoiceId);
        Assert.Equal(500_000m, suggestion[0].Suggested);
        Assert.Equal(newer, suggestion[1].InvoiceId);
        Assert.Equal(100_000m, suggestion[1].Suggested);
        Assert.Equal(500_000m, suggestion[1].Remaining);
    }

    [Fact]
    public async Task Sana_filtri_bugungi_tolovni_topadi()
    {
        var world = await NewWorldAsync();
        var tuition = await NewInvoiceAsync(world.StudentId, "tuition", 500_000m);
        var payment = await AcceptAsync(world, tuition, 500_000m);

        var today = AppClock.Today;
        var found = await world.Client.GetFromJsonAsync<List<PaymentDto>>(
            $"/api/billing/payments?studentId={world.StudentId}"
            + $"&from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Contains(found!, p => p.Id == payment.Id);

        var tomorrow = today.AddDays(1);
        var empty = await world.Client.GetFromJsonAsync<List<PaymentDto>>(
            $"/api/billing/payments?studentId={world.StudentId}&from={tomorrow:yyyy-MM-dd}");
        Assert.Empty(empty!);
    }

    // -----------------------------------------------------------------
    //  SPEC §4.1 / §4.3 — controllerning o'zi
    // -----------------------------------------------------------------

    /// <summary>
    /// Qabul mezoni: <c>PaymentsController</c> da tahrirlash va o'chirish HTTP
    /// fe'llari NOL marta uchraydi. Grep o'rniga refleksiya — u qurilgan
    /// assembly ustida ishlaydi va izohdagi so'zga aldanmaydi.
    /// </summary>
    [Fact]
    public void Controllerda_tahrirlash_va_ochirish_amallari_yoq()
    {
        foreach (var action in ControllerActions())
        {
            Assert.Empty(action.GetCustomAttributes<HttpPutAttribute>(inherit: true));
            Assert.Empty(action.GetCustomAttributes<HttpDeleteAttribute>(inherit: true));
            Assert.Empty(action.GetCustomAttributes<HttpPatchAttribute>(inherit: true));
        }
    }

    /// <summary>
    /// HAR BIR endpoint rol darvozasi ortida turadi — unutilgan endpoint
    /// jimgina ochiq qolmasin (SPEC §4.3).
    /// </summary>
    [Fact]
    public void Har_bir_endpoint_rol_darvozasi_ortida()
    {
        var actions = ControllerActions()
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .ToList();

        Assert.NotEmpty(actions);
        foreach (var action in actions)
            Assert.NotNull(action.GetCustomAttribute<FinanceRoleAttribute>(inherit: true));
    }

    private static MethodInfo[] ControllerActions() =>
        typeof(PaymentsController).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    // =================================================================
    //  Yordamchilar
    // =================================================================

    /// <summary>Bitta test uchun sahna: kassir, uning ochiq smenasi, o'quvchi.</summary>
    private sealed record World(string CashierId, Guid ShiftId, string StudentId, HttpClient Client);

    private async Task<World> NewWorldAsync()
    {
        var (cashier, client) = await ActorAsync(Roles.Cashier);
        var shiftId = await OpenShiftAsync(cashier.Id);
        var studentId = await NewStudentAsync();
        return new World(cashier.Id, shiftId, studentId, client);
    }

    private async Task<(AppUser User, HttpClient Client)> ActorAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        var client = Wired(fixture).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));
        return (user, client);
    }

    private async Task<Guid> OpenShiftAsync(string cashierId)
    {
        var shift = new CashShift
        {
            CashierId = cashierId,
            OpenedAt = AppClock.NowInstant,
            OpeningFloat = 0m,
            Status = CashShiftStatus.Open,
        };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.CashShifts.Add(shift);
            await db.SaveChangesAsync();
        });

        return shift.Id;
    }

    private async Task<string> NewStudentAsync()
    {
        var id = "stu-" + Guid.NewGuid().ToString("N")[..12];

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(new Student
            {
                Id = id,
                FullName = "Test O'quvchi " + id[^6..],
                LastName = "Test",
                FirstName = "O'quvchi",
                ClassName = "1-A",
                EnrollmentDate = AppClock.Today.ToString("yyyy-MM-dd"),
            });
            await db.SaveChangesAsync();
        });

        return id;
    }

    /// <param name="monthOffset">0 = joriy oy, -1 = o'tgan oy.</param>
    private async Task<Guid> NewInvoiceAsync(
        string studentId, string categoryCode, decimal amount, int monthOffset = 0)
    {
        var invoice = new Invoice
        {
            StudentId = studentId,
            Amount = amount,
            Discount = 0m,
            Status = InvoiceStatus.Open,
            CreatedAt = AppClock.NowInstant,
        };

        await fixture.Api.WithDbAsync(async db =>
        {
            invoice.CategoryId = await db.FeeCategories
                .Where(c => c.Code == categoryCode).Select(c => c.Id).SingleAsync();

            var first = new DateOnly(AppClock.Today.Year, AppClock.Today.Month, 1).AddMonths(monthOffset);
            invoice.PeriodMonth = first;
            invoice.DueOn = first.AddDays(9);

            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        });

        return invoice.Id;
    }

    /// <summary>To'lovni HTTP orqali qabul qiladi (tayyorlov qadami).</summary>
    private static async Task<PaymentDto> AcceptAsync(World world, Guid invoiceId, decimal amount)
    {
        var response = await world.Client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId = world.StudentId,
            amount,
            method = PaymentMethod.Cash,
            allocations = new[] { new { invoiceId, amount } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PaymentDto>())!;
    }

    /// <summary>
    /// <see cref="ICashShiftService"/> ning P1-11 ga yetadigan qismi. Haqiqiy
    /// implementatsiyani P1-10 yozadi; bu yerda faqat SHARTNOMA kerak —
    /// to'lov xizmati interfeysga tayanadi, implementatsiyaga emas.
    /// </summary>
    public sealed class ShiftDouble(IAppDbContext db) : ICashShiftService
    {
        public async Task<CashShiftDto?> CurrentAsync(string cashierId, CancellationToken ct = default)
        {
            var shift = await db.CashShifts.AsNoTracking().FirstOrDefaultAsync(
                s => s.CashierId == cashierId && s.Status == CashShiftStatus.Open, ct);

            return shift is null
                ? null
                : new CashShiftDto(
                    shift.Id, shift.CashierId, "Test kassir",
                    shift.OpenedAt, shift.ClosedAt, shift.OpeningFloat,
                    shift.ExpectedCash, shift.CountedCash, shift.Variance,
                    shift.Status, null, 0, 0m, 0m);
        }

        public async Task<long> NextReceiptNoAsync(Guid shiftId, CancellationToken ct = default)
        {
            var max = await db.Payments
                .Where(p => p.CashShiftId == shiftId)
                .MaxAsync(p => (long?)p.ReceiptNo, ct);
            return (max ?? 0L) + 1L;
        }

        // Qolgani P1-10 ning ishi — P1-11 ularni chaqirmaydi.
        public Task<CashShiftDto> OpenAsync(
            string cashierId, decimal openingFloat, CancellationToken ct = default) =>
            throw new NotSupportedException("P1-10");

        public Task<CashShiftDto> CloseAsync(
            Guid shiftId, string closedByUserId, decimal countedCash, string? note,
            CancellationToken ct = default) =>
            throw new NotSupportedException("P1-10");

        public Task<ZReportDto> ZReportAsync(Guid shiftId, CancellationToken ct = default) =>
            throw new NotSupportedException("P1-10");

        public Task<IReadOnlyList<CashShiftDto>> ListAsync(
            CashShiftQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException("P1-10");
    }
}
