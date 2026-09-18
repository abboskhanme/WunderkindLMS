using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// Hisob-fakturalar registri va "bekor qilish"
/// (docs/modules/finance-parity.md §2.10, F10.01 va F10.02).
///
/// <para>
/// <b>F10.02 dagi uchta nosozlik shu yerda mixlangan:</b>
/// </para>
/// <list type="number">
///   <item><see cref="Storno_qilingan_tolovli_hisob_faktura_bekor_qilinadi"/> —
///   storno qilingan to'lov endi to'siq emas;</item>
///   <item><see cref="Bekor_qilishning_xatolari_500_emas"/> — hamma rad etish
///   o'qiladigan 400 / 404 / 409 bilan qaytadi;</item>
///   <item><see cref="Avtomatik_hisoblangan_oyni_boshqa_admin_bekor_qila_oladi"/> —
///   fon xizmati nomidan yozilgan oy boshqa admin tomonidan bekor qilinadi,
///   yozuvchining o'ziga esa sababi AYTILGAN 403 chiqadi.</item>
/// </list>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class InvoiceRegisterTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>
    /// Shu klass yaratgan bazalarning ulanish satrlari — test tugagach
    /// hovuzlari yopiladi (<c>max_connections</c> tugab qolmasin).
    /// </summary>
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));

        return Task.CompletedTask;
    }

    private const string Register = "/api/admin/billing/invoices";

    // =====================================================================
    //  1. RUXSAT — SPEC §4.3
    // =====================================================================

    /// <summary>
    /// Kassir registrga ham, bekor qilishga ham kira olmaydi: §4.3 da unga
    /// "Change monthly fee / subscription" ⛔ va hisobot ustuni yo'q.
    /// </summary>
    [Fact]
    public async Task Kassir_registrga_ham_bekor_qilishga_ham_kira_olmaydi_403()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Register)).StatusCode);

        var void403 = await client.PostAsJsonAsync(
            $"{Register}/{Guid.NewGuid()}/void", new { reason = "test" });
        Assert.Equal(HttpStatusCode.Forbidden, void403.StatusCode);
    }

    /// <summary>"finance" ruxsatli xodim va o'qituvchi ham yopiq.</summary>
    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Teacher)]
    public async Task Xodim_va_oqituvchi_ham_kira_olmaydi_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "finance");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Register)).StatusCode);

        var void403 = await client.PostAsJsonAsync(
            $"{Register}/{Guid.NewGuid()}/void", new { reason = "test" });
        Assert.Equal(HttpStatusCode.Forbidden, void403.StatusCode);
    }

    /// <summary>Token'siz so'rov — 401.</summary>
    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Register)).StatusCode);

        var void401 = await client.PostAsJsonAsync(
            $"{Register}/{Guid.NewGuid()}/void", new { reason = "test" });
        Assert.Equal(HttpStatusCode.Unauthorized, void401.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_direktor_registrni_ochadi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);

        var response = await client.GetAsync(Register);
        Assert.True(response.IsSuccessStatusCode,
            $"{Register} → {(int)response.StatusCode} {response.StatusCode}");
    }

    // =====================================================================
    //  2. F10.02 (2) — xatolar 500 emas
    // =====================================================================

    /// <summary>
    /// Avvalgi kodda <c>VoidAsync</c> <c>ArgumentException</c> va
    /// <c>InvalidOperationException</c> tashlardi; <c>[BillingFault]</c> ularni
    /// TANIMAYDI, ya'ni foydalanuvchi "Serverda xatolik" (500) ko'rardi va
    /// nima qilishni bilmasdi. Endi har rad etishning o'z kodi va o'zbekcha
    /// matni bor.
    /// </summary>
    [Fact]
    public async Task Bekor_qilishning_xatolari_500_emas()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        // Yo'q hisob-faktura → 404, 500 emas.
        var missing = await client.PostAsJsonAsync(
            $"{Register}/{Guid.NewGuid()}/void", new { reason = "Xato oy" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var missingBody = await missing.Content.ReadFromJsonAsync<BillingErrorDto>();
        Assert.Equal("invoice_not_found", missingBody?.Code);
        Assert.False(string.IsNullOrWhiteSpace(missingBody?.Message));

        // Bo'sh sabab → 400.
        var noReason = await client.PostAsJsonAsync(
            $"{Register}/{Guid.NewGuid()}/void", new { reason = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal("reason_required",
            (await noReason.Content.ReadFromJsonAsync<BillingErrorDto>())?.Code);

        // Noma'lum holat filtri → 400 (bo'sh ro'yxat ham, 500 ham emas).
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Register}?status=ochiq")).StatusCode);

        // Teskari davr → 400.
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync($"{Register}?fromMonth=2026-05-01&toMonth=2026-04-01")).StatusCode);
    }

    // =====================================================================
    //  3. F10.02 (1) — storno qilingan to'lov to'siq emas
    // =====================================================================

    /// <summary>
    /// <b>Eng muhim tuzatish.</b> Hisob-faktura to'langan → to'lov storno
    /// qilingan → hisob-faktura endi bekor qilinadi.
    ///
    /// <para>
    /// Avvalgi shart <c>PaymentAllocations.Any(...)</c> edi. Taqsimot qatori
    /// esa O'CHMAYDI (jadval o'zgarmas) va storno uni faqat KUCHSIZ qiladi —
    /// ya'ni bir marta to'langan oyni keyin HECH QACHON tuzatib bo'lmasdi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Storno_qilingan_tolovli_hisob_faktura_bekor_qilinadi()
    {
        await using var db = await NewDbAsync("void-storno");
        var world = await SeedAsync(db);
        var invoices = new InvoiceService(db, world.Ledger);

        var invoice = await AccrueAsync(db, world, 500_000m);
        var payment = await PayAsync(db, world, invoice.Id, 500_000m);

        // 1. KUCHDAGI taqsimot bor — bekor qilish rad etiladi.
        var refused = await Assert.ThrowsAsync<BillingRuleException>(
            () => invoices.VoidAsync(invoice.Id, "Xato hisoblangan", world.AdminId));
        Assert.Equal("has_effective_allocation", refused.Code);
        Assert.Equal(BillingFault.Conflict, refused.Fault);

        // 2. To'lovni storno qilamiz.
        await new PaymentService(db, world.Ledger)
            .ReverseAsync(payment.Id, "Noto'g'ri o'quvchiga yozilgan", world.AdminId);

        // 3. Endi bekor qilish O'TADI — taqsimot qatori joyida, lekin kuchsiz.
        var result = await invoices.VoidAsync(invoice.Id, "Xato hisoblangan", world.AdminId);
        Assert.Equal(InvoiceStatus.Void, result.Status);
        Assert.Equal(0m, result.Paid);

        // Taqsimot qatori TEGILMAGAN (SPEC §4.1 — hech narsa o'chirilmaydi).
        Assert.True(await db.PaymentAllocations.AsNoTracking()
            .AnyAsync(a => a.InvoiceId == invoice.Id));

        // Ikkinchi marta — 409.
        var twice = await Assert.ThrowsAsync<BillingRuleException>(
            () => invoices.VoidAsync(invoice.Id, "Yana", world.AdminId));
        Assert.Equal("already_void", twice.Code);
    }

    /// <summary>
    /// Jurnal ham tuzaladi: hisob-faktura partiyasi ko'zgu satrlar bilan
    /// qaytariladi va debet = kredit qoladi (trial balance nolga yig'iladi).
    /// </summary>
    [Fact]
    public async Task Bekor_qilingan_hisob_faktura_jurnalni_muvozanatda_qoldiradi()
    {
        await using var db = await NewDbAsync("void-ledger");
        var world = await SeedAsync(db);

        var invoice = await AccrueAsync(db, world, 750_000m);
        await new InvoiceService(db, world.Ledger).VoidAsync(invoice.Id, "Obuna xato", world.AdminId);

        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefId == invoice.Id).ToListAsync();

        // Asl juft + ko'zgu juft = 4 satr; originallar TEGILMAGAN.
        Assert.Equal(4, entries.Count);
        Assert.Equal(2, entries.Count(e => e.ReversalOf != null));
        Assert.All(entries.Where(e => e.ReversalOf != null),
            e => Assert.Equal(LedgerRefType.Reversal, e.RefType));

        Assert.Equal(
            entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount),
            entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount));

        // Qarzga ham kirmaydi.
        var debtors = await new FinanceReportQueries(db).DebtorsAsync(
            new DebtorReportQuery(ClassName: null));
        Assert.DoesNotContain(debtors, d => d.StudentId == world.StudentId);
    }

    // =====================================================================
    //  4. F10.02 (3) — fon xizmati yozgan oy
    // =====================================================================

    /// <summary>
    /// Oylik hisoblashni fon xizmati BIRINCHI direktor nomidan yozadi
    /// (<c>BillingAccrualService.ResolveActorAsync</c>). SPEC §4.5 esa
    /// partiyani qo'ygan odamga uni storno qilishni taqiqlaydi — ya'ni o'sha
    /// direktor avtomatik hisoblangan oyni bekor qila olmaydi.
    ///
    /// <para>
    /// <b>Qoida SAQLANADI</b> (u firibgarlikka qarshi), lekin endi sababi
    /// AYTILADI: yozuvchining o'ziga 403 <c>self_reversal</c>, boshqa adminda
    /// esa amal o'tadi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Avtomatik_hisoblangan_oyni_boshqa_admin_bekor_qila_oladi()
    {
        await using var db = await NewDbAsync("void-accrual");
        var world = await SeedAsync(db);
        var invoices = new InvoiceService(db, world.Ledger);

        // Fon xizmati qanday yozsa, shunday: hisoblash direktor nomidan.
        await AddSubscriptionAsync(db, world, 1_200_000m);
        var accrual = await invoices.AccrueMonthAsync(ThisMonth, world.DirectorId);
        Assert.Equal(1, accrual.Created);

        var invoiceId = await db.Invoices.AsNoTracking()
            .Where(i => i.StudentId == world.StudentId).Select(i => i.Id).SingleAsync();

        // Yozuvchining o'zi — 403, va xabar nima qilish kerakligini aytadi.
        var refused = await Assert.ThrowsAsync<BillingRuleException>(
            () => invoices.VoidAsync(invoiceId, "Obuna xato kiritilgan", world.DirectorId));
        Assert.Equal("self_reversal", refused.Code);
        Assert.Equal(BillingFault.Forbidden, refused.Fault);
        Assert.Contains("boshqa admin", refused.Message, StringComparison.OrdinalIgnoreCase);

        // Boshqa admin — o'tadi.
        var result = await invoices.VoidAsync(invoiceId, "Obuna xato kiritilgan", world.AdminId);
        Assert.Equal(InvoiceStatus.Void, result.Status);
    }

    /// <summary>
    /// To'lanadigan summasi 0 bo'lgan hisob-fakturada (100% chegirma) jurnal
    /// partiyasi UMUMAN yo'q — bekor qilish baribir ishlaydi va audit izi
    /// qoladi.
    /// </summary>
    [Fact]
    public async Task Jurnalsiz_hisob_faktura_ham_bekor_qilinadi_va_audit_qoldiradi()
    {
        await using var db = await NewDbAsync("void-zero");
        var world = await SeedAsync(db);

        var invoice = new Invoice
        {
            StudentId = world.StudentId,
            CategoryId = world.CategoryId,
            PeriodMonth = ThisMonth,
            Amount = 400_000m,
            Discount = 400_000m,
            DueOn = ThisMonth.AddDays(9),
            Status = InvoiceStatus.Paid,
            CreatedAt = AppClock.NowInstant,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var result = await new InvoiceService(db, world.Ledger)
            .VoidAsync(invoice.Id, "Chegirma noto'g'ri", world.AdminId);

        Assert.Equal(InvoiceStatus.Void, result.Status);

        var audit = Assert.Single(await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == InvoiceService.AuditEntity
                        && a.EntityId == invoice.Id.ToString("D"))
            .ToListAsync());
        Assert.Equal("void", audit.Action);
        Assert.Equal(world.AdminId, audit.ActorId);
        Assert.Contains("Chegirma noto'g'ri", audit.Summary, StringComparison.Ordinal);
    }

    // =====================================================================
    //  5. F10.01 — registr
    // =====================================================================

    /// <summary>
    /// Yakun BUTUN FILTR bo'yicha: ikkinchi sahifada turgan foydalanuvchi ham
    /// o'sha raqamni ko'radi, va bekor qilingan hisob-faktura filtrga tushmasa
    /// yakunga ham kirmaydi.
    /// </summary>
    [Fact]
    public async Task Registr_yakuni_sahifadan_mustaqil()
    {
        await using var db = await NewDbAsync("register");
        var world = await SeedAsync(db);
        var invoices = new InvoiceService(db, world.Ledger);

        // Har biri BOSHQA oyga: (student_id, category_id, period_month) unikal.
        for (var i = 1; i <= 5; i++)
            await AccrueAsync(db, world, 100_000m * i, ThisMonth.AddMonths(1 - i));

        var query = new InvoicePageQuery(
            FromMonth: ThisMonth.AddMonths(-4), ToMonth: ThisMonth, PageSize: 2);

        var first = await invoices.ListPageAsync(query with { Page = 1 });
        var last = await invoices.ListPageAsync(query with { Page = 3 });

        Assert.Equal(5, first.Total);
        Assert.Equal(2, first.Rows.Count);
        Assert.Single(last.Rows);

        Assert.Equal(1_500_000m, first.Totals.Amount);      // 100k + … + 500k
        Assert.Equal(1_500_000m, first.Totals.Payable);
        Assert.Equal(0m, first.Totals.Paid);
        Assert.Equal(1_500_000m, first.Totals.Remaining);
        Assert.Equal(first.Totals, last.Totals);

        // Sinf qatorda emas, yonma-yon jadvalda (`InvoiceDto` muzlatilgan).
        Assert.Equal("7-B", first.ClassNames[world.StudentId]);
    }

    /// <summary>
    /// <b>"To'langan" ta'rifi ikkita ekranda BIR XIL.</b> Registr
    /// <c>InvoiceService.EffectiveAllocations</c> ni, qarzdorlar hisoboti esa
    /// <c>FinanceReportQueries.EffectiveAllocations</c> ni ishlatadi — ular
    /// alohida yozilgan (ikkinchisi <c>private</c>), shuning uchun ajralib
    /// ketmaganini MASHINA tekshiradi: to'lov storno qilingach ikkala ekran
    /// ham "to'lanmagan" deyishi shart.
    /// </summary>
    [Fact]
    public async Task Storno_dan_keyin_registr_va_qarzdorlar_hisoboti_bir_xil_deydi()
    {
        await using var db = await NewDbAsync("agree");
        var world = await SeedAsync(db);
        var invoices = new InvoiceService(db, world.Ledger);
        var reports = new FinanceReportQueries(db);

        var invoice = await AccrueAsync(db, world, 800_000m);
        var payment = await PayAsync(db, world, invoice.Id, 800_000m);

        var query = new InvoicePageQuery(FromMonth: ThisMonth, ToMonth: ThisMonth);

        // To'langan: registrda 800 000, qarzdorlar ro'yxatida bu o'quvchi YO'Q.
        var paid = await invoices.ListPageAsync(query);
        Assert.Equal(800_000m, paid.Totals.Paid);
        Assert.Equal(0m, paid.Totals.Remaining);
        Assert.DoesNotContain(
            await reports.DebtorsAsync(new DebtorReportQuery()),
            d => d.StudentId == world.StudentId);

        // Storno: ikkala ekran ham qarzni QAYTA ko'rsatadi.
        await new PaymentService(db, world.Ledger)
            .ReverseAsync(payment.Id, "Boshqa o'quvchiniki edi", world.AdminId);

        var afterStorno = await invoices.ListPageAsync(query);
        Assert.Equal(0m, afterStorno.Totals.Paid);
        Assert.Equal(800_000m, afterStorno.Totals.Remaining);

        var debtor = Assert.Single(
            await reports.DebtorsAsync(new DebtorReportQuery()),
            d => d.StudentId == world.StudentId);
        Assert.Equal(afterStorno.Totals.Remaining, debtor.Debt);
    }

    /// <summary>"Faqat qarzdorlar" va "faqat muddati o'tganlar" filtrlari.</summary>
    [Fact]
    public async Task Faqat_qarzdorlar_va_muddati_otganlar_filtrlari()
    {
        await using var db = await NewDbAsync("filters");
        var world = await SeedAsync(db);
        var invoices = new InvoiceService(db, world.Ledger);

        // Muddati o'tgan: uch oy oldingi oy, sukut muddati bilan.
        var oldMonth = ThisMonth.AddMonths(-3);
        var overdue = await AccrueAsync(db, world, 300_000m, oldMonth);

        // Muddati YETMAGAN ikkitasi: to'lov sanasi kelajakda, ya'ni test
        // oyning qaysi kunida yurishidan qat'i nazar natija o'zgarmaydi
        // (`overdue_after_day` sukut bo'yicha 15 — 16-kundan keyin
        // "joriy oy" ham muddati o'tganga aylanib qolardi).
        var future = AppClock.Today.AddDays(30);
        var current = await AccrueAsync(db, world, 400_000m, ThisMonth.AddMonths(-1), future);
        var settled = await AccrueAsync(db, world, 500_000m, ThisMonth, future);
        await PayAsync(db, world, settled.Id, 500_000m);

        var period = new InvoicePageQuery(FromMonth: oldMonth, ToMonth: ThisMonth);

        var all = await invoices.ListPageAsync(period);
        Assert.Equal(3, all.Total);
        Assert.Equal(1_200_000m, all.Totals.Payable);
        Assert.Equal(500_000m, all.Totals.Paid);
        Assert.Equal(700_000m, all.Totals.Remaining);

        var debtorsOnly = await invoices.ListPageAsync(period with { OnlyDebtors = true });
        Assert.Equal(2, debtorsOnly.Total);
        Assert.DoesNotContain(debtorsOnly.Rows, r => r.Id == settled.Id);
        Assert.Equal(700_000m, debtorsOnly.Totals.Payable);

        // Muddati o'tgan: uch oy oldingi oy. Joriy oy hali muddatida.
        var overdueOnly = await invoices.ListPageAsync(period with { OnlyOverdue = true });
        Assert.Equal(overdue.Id, Assert.Single(overdueOnly.Rows).Id);
        Assert.All(overdueOnly.Rows, r => Assert.True(r.IsOverdue));
        Assert.Equal(300_000m, overdueOnly.Totals.Remaining);
    }

    // =====================================================================
    //  6. F10.05 — eksport
    // =====================================================================

    /// <summary>
    /// <c>ExportRowsAsync</c> BUTUN filtr bo'yicha (sahifasiz) — qatorlar soni
    /// va yig'indisi <c>ListPageAsync</c> ning yakuni bilan AYNAN bir xil
    /// bo'lishi shart, chunki ikkovi ham <c>Filtered</c> dan o'qiydi.
    /// </summary>
    [Fact]
    public async Task Eksport_qatorlari_registr_yakuniga_mos_keladi()
    {
        await using var db = await NewDbAsync("export");
        var world = await SeedAsync(db);
        var invoices = new InvoiceService(db, world.Ledger);

        for (var i = 1; i <= 3; i++)
            await AccrueAsync(db, world, 100_000m * i, ThisMonth.AddMonths(1 - i));

        var query = new InvoicePageQuery(FromMonth: ThisMonth.AddMonths(-2), ToMonth: ThisMonth);

        var exported = await invoices.ExportRowsAsync(query);
        var paged = await invoices.ListPageAsync(query);

        Assert.Equal(paged.Total, exported.Count);
        Assert.Equal(paged.Totals.Payable, exported.Sum(r => r.Payable));
        Assert.Equal(paged.Totals.Paid, exported.Sum(r => r.Paid));
        Assert.Equal(paged.Totals.Remaining, exported.Sum(r => r.Remaining));
    }

    /// <summary>
    /// HTTP yuzasi: kassir/xodim/o'qituvchi 403 (§4.3), admin — to'g'ri
    /// content-type va bo'sh bo'lmagan tana.
    /// </summary>
    [Fact]
    public async Task Eksport_HTTP_ruxsat_va_content_type()
    {
        using var cashier = await fixture.Api.ClientAsAsync(Roles.Cashier);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync($"{Register}/export")).StatusCode);

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await admin.GetAsync($"{Register}/export");
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.StatusCode}");
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static readonly DateOnly ThisMonth = new(AppClock.Today.Year, AppClock.Today.Month, 1);

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync(prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private sealed record World(
        string CashierId, string AdminId, string DirectorId,
        Guid ShiftId, Guid AdminShiftId, string StudentId, Guid CategoryId,
        LedgerService Ledger);

    private static async Task<World> SeedAsync(AppDbContext db)
    {
        var cashierId = await SeedUserAsync(db, Roles.Cashier);
        var adminId = await SeedUserAsync(db, Roles.Admin);
        var directorId = await SeedUserAsync(db, Roles.SuperAdmin);

        var shift = new CashShift
        {
            CashierId = cashierId,
            OpenedAt = AppClock.NowInstant,
            OpeningFloat = 0m,
            Status = CashShiftStatus.Open,
        };
        var adminShift = new CashShift
        {
            CashierId = adminId,
            OpenedAt = AppClock.NowInstant,
            OpeningFloat = 0m,
            Status = CashShiftStatus.Open,
        };
        db.CashShifts.AddRange(shift, adminShift);

        var studentId = "stu-" + Guid.NewGuid().ToString("N")[..12];
        db.Students.Add(new Student
        {
            Id = studentId,
            FullName = "Registr O'quvchisi",
            LastName = "Registr",
            FirstName = "O'quvchi",
            ClassName = "7-B",
            EnrollmentDate = "2024-09-01",
        });
        await db.SaveChangesAsync();

        var categoryId = await db.FeeCategories.AsNoTracking()
            .Where(c => c.Code == "tuition").Select(c => c.Id).SingleAsync();

        return new World(
            cashierId, adminId, directorId, shift.Id, adminShift.Id, studentId, categoryId,
            new LedgerService(db));
    }

    private static async Task<string> SeedUserAsync(AppDbContext db, string role)
    {
        var user = new AppUser
        {
            FullName = $"Test {role} {Guid.NewGuid().ToString("N")[..6]}",
            Role = role,
            Email = $"{role}.{Guid.NewGuid().ToString("N")[..8]}",
        };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task AddSubscriptionAsync(AppDbContext db, World world, decimal amount)
    {
        db.StudentSubscriptions.Add(new StudentSubscription
        {
            StudentId = world.StudentId,
            CategoryId = world.CategoryId,
            MonthlyAmount = amount,
            StartsOn = ThisMonth,
            CreatedBy = world.DirectorId,
            CreatedAt = AppClock.NowInstant,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Hisob-faktura + jurnal partiyasi — <c>InvoiceService.AccrueMonthAsync</c>
    /// ning aynan o'sha ikki qatori (debet <c>receivable</c>, kredit
    /// <c>revenue:tuition</c>). Partiyani KASSIR emas, admin qo'yadi, ya'ni
    /// bekor qilishni direktor ham qila oladi.
    /// </summary>
    private static async Task<Invoice> AccrueAsync(
        AppDbContext db, World world, decimal amount, DateOnly? month = null, DateOnly? dueOn = null)
    {
        var period = month ?? ThisMonth;
        var invoice = new Invoice
        {
            StudentId = world.StudentId,
            CategoryId = world.CategoryId,
            PeriodMonth = period,
            Amount = amount,
            Discount = 0m,
            DueOn = dueOn ?? period.AddDays(9),
            Status = InvoiceStatus.Open,
            CreatedAt = AppClock.NowInstant,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        await world.Ledger.PostAsync(
        [
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Debit, amount,
                LedgerRefType.Invoice, invoice.Id, period),
            new LedgerPosting(Accounts.RevenueTuition, LedgerDirection.Credit, amount,
                LedgerRefType.Invoice, invoice.Id, period),
        ], world.CashierId);

        return invoice;
    }

    /// <summary>To'lov + taqsimot + jurnal partiyasi, kassir nomidan.</summary>
    private static async Task<Payment> PayAsync(
        AppDbContext db, World world, Guid invoiceId, decimal amount)
    {
        var lastReceipt = await db.Payments
            .Where(p => p.CashShiftId == world.ShiftId)
            .MaxAsync(p => (long?)p.ReceiptNo) ?? 0;

        var payment = new Payment
        {
            ReceiptNo = lastReceipt + 1,
            StudentId = world.StudentId,
            Amount = amount,
            Method = PaymentMethod.Cash,
            CashShiftId = world.ShiftId,
            CashierId = world.CashierId,
            ReceivedAt = AppClock.NowInstant,
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

        var invoice = await db.Invoices.FirstAsync(i => i.Id == invoiceId);
        invoice.Status = InvoiceService.StatusFor(invoice.Amount - invoice.Discount, amount);
        await db.SaveChangesAsync();

        await world.Ledger.PostAsync(
        [
            new LedgerPosting(Accounts.Cash, LedgerDirection.Debit, amount,
                LedgerRefType.Payment, payment.Id, AppClock.Today),
            new LedgerPosting(Accounts.Receivable, LedgerDirection.Credit, amount,
                LedgerRefType.Payment, payment.Id, AppClock.Today),
        ], world.CashierId);

        return payment;
    }
}
