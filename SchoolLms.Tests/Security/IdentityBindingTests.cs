using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Security;

// ===========================================================================
//  SPEC §4.4 (shaxs serverda aniqlanadi) + §4.5 (ikki qavatli nazorat).
//  Vazifa: P1-22.
// ===========================================================================
//
//  SAVOL: "bu pulni kim oldi?" — javobi HECH QACHON so'rov tanasidan
//  kelmasligi kerak. Aks holda kassir to'lovni boshqa odam nomiga yozib,
//  keyin "men emas edim" deb ayta oladi; auditor esa bazadagi ismga
//  ishonadi.
//
//  UCH DARAJA
//  ----------
//  1. RAD ETISH. `cashierId` (va u bilan bir qatordagi 10 ta nom) so'rov
//     tanasida uchrasa — 400. Jimgina e'tiborsiz qoldirish YARAMAYDI:
//     yuborgan odam "boshqa nomga yozdim" deb o'ylab qolardi.
//  2. MANBA. Bazaga tushgan `cashier_id` HAR DOIM JWT'dagi shaxs.
//     Storno qatorida esa — TASDIQLOVCHINING id'si, original kassirniki
//     emas.
//  3. IZOLYATSIYA (IDOR). Kassir boshqa kassirning chekini, smenasini va
//     ro'yxatini ko'ra ham, o'zgartira ham olmaydi — boshqa odamning
//     id'sini QO'LDA yozib yuborgan taqdirda ham.
//
//  §4.4 DAN OG'ISH — YOZIB QO'YILDI, TUZATILMADI
//  ---------------------------------------------
//  `POST /api/cash/shifts/open` va `.../close` so'rov tanasidagi ortiqcha
//  `cashierId` ni RAD ETMAYDI, JIMGINA e'tiborsiz qoldiradi (muzlatilgan
//  `OpenShiftRequest`/`CloseShiftRequest` da bunday maydon yo'q, MVC esa
//  notanish maydonni tashlab ketadi). Xavfsizlik natijasi buzilmaydi —
//  smena baribir JWT'dagi shaxsga ochiladi, va quyidagi test AYNAN shuni
//  tekshiradi. Lekin §4.4 ning matni "rad etiladi" deydi, ya'ni
//  `PaymentsController.RejectServerDerivedFields` ga o'xshash tekshiruv
//  `CashShiftsController` da ham bo'lishi kerak. Bu P1-22 ning ishi emas
//  (test prod kodini o'zgartirmaydi) — hisobotga chiqarildi.
// ===========================================================================

/// <summary>
/// Shaxsning serverda aniqlanishi va yozuvlar izolyatsiyasi
/// (SPEC §4.4, §4.5 — P1-22). Batafsil: fayl boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class IdentityBindingTests(ApiFixture fixture)
{
    private static readonly Guid TuitionCategoryId = new("00000000-0000-0000-0000-0000000000c1");

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  1. RAD ETISH — shaxs nomi so'rov tanasida bo'lmasin (SPEC §4.4)
    // =====================================================================

    /// <summary>
    /// <c>PaymentsController.ServerDerivedFields</c> ro'yxatidagi HAR bir
    /// nom to'lov so'rovida uchrasa — 400 <c>identity_in_body</c>, va
    /// bazaga BIRORTA qator tushmaydi.
    ///
    /// <para>
    /// Nega hammasi sanab o'tilgan: ro'yxatdan bittasi tushib qolsa
    /// (masalan kimdir <c>cashShiftId</c> ni "keraksiz" deb olib tashlasa),
    /// o'sha maydon jimgina qabul qilinadigan bo'lib qolardi. Har nom —
    /// alohida test holati.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("cashierId")]
    [InlineData("cashierName")]
    [InlineData("cashShiftId")]
    [InlineData("shiftId")]
    [InlineData("receiptNo")]
    [InlineData("receivedAt")]
    [InlineData("reversalOf")]
    [InlineData("reversedBy")]
    [InlineData("createdBy")]
    [InlineData("approvedBy")]
    [InlineData("approverId")]
    // Registr ahamiyatsiz — `OrdinalIgnoreCase`. Aks holda `CashierId` deb
    // yozgan odam tekshiruvni chetlab o'tardi.
    [InlineData("CashierId")]
    [InlineData("CASHIERID")]
    public async Task Tolov_sorovida_server_maydoni_400_identity_in_body(string field)
    {
        var world = await CashierWithOpenShiftAsync();
        var studentId = await NewStudentAsync();
        var invoiceId = await NewInvoiceAsync(studentId, 500_000m);
        var (intruder, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);

        var body = new Dictionary<string, object?>
        {
            ["studentId"] = studentId,
            ["amount"] = 500_000m,
            ["method"] = PaymentMethod.Cash,
            ["allocations"] = new[] { new { invoiceId, amount = 500_000m } },
            [field] = intruder.Id,
        };

        var response = await world.Client.PostAsJsonAsync("/api/cash/payments", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("identity_in_body", error?.Code);
        Assert.Contains(field, error?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        await using var db = NewDb();
        Assert.False(await db.Payments.AsNoTracking().AnyAsync(p => p.StudentId == studentId),
            "So'rov rad etildi, lekin to'lov qatori baribir yozildi.");
    }

    /// <summary>
    /// Storno so'rovi ham xuddi shunday tekshiriladi — <c>approvedBy</c> ni
    /// tashqaridan berish "ikkinchi shaxs tasdiqladi" degan yolg'onni
    /// yozib qo'yish yo'li bo'lardi (SPEC §4.5).
    /// </summary>
    [Theory]
    [InlineData("approvedBy")]
    [InlineData("approverId")]
    [InlineData("reversedBy")]
    [InlineData("cashierId")]
    public async Task Storno_sorovida_server_maydoni_400_identity_in_body(string field)
    {
        var original = await PaymentByCashierAsync(200_000m);
        var admin = await ActorWithOpenShiftAsync(Roles.Admin);

        var body = new Dictionary<string, object?>
        {
            ["reason"] = "Xato summa",
            [field] = original.CashierId,
        };

        var response = await admin.Client.PostAsJsonAsync(
            $"/api/admin/payments/{original.PaymentId}/reverse", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("identity_in_body", error?.Code);

        await using var db = NewDb();
        Assert.False(await db.Payments.AsNoTracking().AnyAsync(p => p.ReversalOf == original.PaymentId));
    }

    // =====================================================================
    //  2. MANBA — `cashier_id` HAR DOIM JWT'dagi shaxs
    // =====================================================================

    /// <summary>
    /// Ikki kassir ketma-ket to'lov qabul qiladi. Har bir qator AYNAN
    /// o'zining tokenidagi shaxsga yoziladi — chalkashish yo'q. Jurnal
    /// (<c>ledger_entries.created_by</c>) ham shu shaxsni ko'rsatadi.
    /// </summary>
    [Fact]
    public async Task Saqlangan_cashier_id_har_doim_JWT_dagi_shaxs()
    {
        var first = await CashierWithOpenShiftAsync();
        var second = await CashierWithOpenShiftAsync();

        var firstPayment = await AcceptAsync(first, 111_000m);
        var secondPayment = await AcceptAsync(second, 222_000m);

        Assert.NotEqual(first.User.Id, second.User.Id);

        await using var db = NewDb();

        var firstRow = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == firstPayment.Id);
        Assert.Equal(first.User.Id, firstRow.CashierId);
        // "Smena" endi yo'q (kassalar modeli, 2026-09) — yangi to'lovda har doim null.
        Assert.Null(firstRow.CashShiftId);
        Assert.Equal(111_000m, firstRow.Amount);

        var secondRow = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == secondPayment.Id);
        Assert.Equal(second.User.Id, secondRow.CashierId);
        Assert.Null(secondRow.CashShiftId);
        Assert.Equal(222_000m, secondRow.Amount);

        // Jurnalda ham AYNAN o'sha shaxs.
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefId == firstPayment.Id).ToListAsync();
        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal(first.User.Id, e.CreatedBy));
    }

    /// <summary>
    /// Storno qatorining <c>cashier_id</c> si — TASDIQLOVCHI, original
    /// kassir emas. Aks holda hisobotda "kassir o'zi qaytardi" ko'rinardi
    /// va SPEC §4.5 dagi ikkinchi imzo yo'qolardi.
    /// </summary>
    [Fact]
    public async Task Storno_qatori_tasdiqlovchining_nomiga_yoziladi()
    {
        var original = await PaymentByCashierAsync(300_000m);
        var admin = await ActorWithOpenShiftAsync(Roles.Admin);

        var response = await admin.Client.PostAsJsonAsync(
            $"/api/admin/payments/{original.PaymentId}/reverse",
            new { reason = "Ikki marta qabul qilingan" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(dto);
        Assert.Equal(admin.User.Id, dto.CashierId);
        Assert.NotEqual(original.CashierId, dto.CashierId);

        await using var db = NewDb();
        var storno = await db.Payments.AsNoTracking().SingleAsync(p => p.ReversalOf == original.PaymentId);
        Assert.Equal(admin.User.Id, storno.CashierId);
        Assert.Null(storno.CashShiftId);

        // Original TEGILMAGAN (SPEC §4.1).
        var untouched = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == original.PaymentId);
        Assert.Equal(original.CashierId, untouched.CashierId);
        Assert.Equal(300_000m, untouched.Amount);
        Assert.Null(untouched.ReversalOf);
    }

    /// <summary>
    /// Smena ochishda ham shaxs JWT'dan. So'rov tanasiga boshqa odamning
    /// id'si yozilsa — u E'TIBORSIZ qoladi (fayl boshidagi "§4.4 dan
    /// og'ish" izohi), smena esa baribir chaqiruvchining nomiga ochiladi.
    /// Bu test aynan shu xavfsizlik natijasini qulflaydi.
    /// </summary>
    [Fact]
    public async Task Smena_sorov_tanasidagi_cashierId_ga_qaramay_JWT_egasiga_ochiladi()
    {
        var (actor, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        var (victim, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        using var client = ClientFor(actor);

        var response = await client.PostAsJsonAsync("/api/cash/shifts/open", new
        {
            openingFloat = 0m,
            cashierId = victim.Id,      // ← tashqaridan berilgan shaxs
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<CashShiftDto>();
        Assert.NotNull(dto);
        Assert.Equal(actor.Id, dto.CashierId);
        Assert.NotEqual(victim.Id, dto.CashierId);

        await using var db = NewDb();
        Assert.Equal(actor.Id, await db.CashShifts.AsNoTracking()
            .Where(s => s.Id == dto.Id).Select(s => s.CashierId).SingleAsync());
        Assert.False(await db.CashShifts.AsNoTracking().AnyAsync(s => s.CashierId == victim.Id),
            "Boshqa kassir nomiga smena ochildi — SPEC §4.4 buzilgan.");
    }

    // =====================================================================
    //  3. STORNO CHEGARALARI (SPEC §4.3, §4.5)
    // =====================================================================

    /// <summary>
    /// Kassir storno qila olmaydi — 403, va bazada birorta qator
    /// o'zgarmaydi. Bu mijoz tasvirlagan firibgarlikning to'g'ridan-to'g'ri
    /// yopilishi.
    /// </summary>
    [Fact]
    public async Task Kassir_storno_qila_olmaydi_403_va_hech_narsa_yozilmaydi()
    {
        var original = await PaymentByCashierAsync(150_000m);
        var attacker = await ActorWithOpenShiftAsync(Roles.Cashier);

        var response = await attacker.Client.PostAsJsonAsync(
            $"/api/admin/payments/{original.PaymentId}/reverse",
            new { reason = "O'zim tuzataman" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());

        await using var db = NewDb();
        Assert.False(await db.Payments.AsNoTracking().AnyAsync(p => p.ReversalOf == original.PaymentId));
        // Jurnalda ham storno yozuvi yo'q.
        Assert.False(await db.LedgerEntries.AsNoTracking()
            .AnyAsync(e => e.RefType == LedgerRefType.Reversal && e.RefId == original.PaymentId));
    }

    /// <summary>
    /// Storno'ni storno qilib bo'lmaydi — <b>409 <c>cannot_reverse_reversal</c></b>.
    /// Aks holda "qaytardim → qaytarishni qaytardim" zanjiri bilan pulni
    /// hisobotda istalgan tomonga siljitish mumkin bo'lardi.
    /// </summary>
    [Fact]
    public async Task Admin_oz_stornosini_qayta_storno_qila_olmaydi_409()
    {
        var original = await PaymentByCashierAsync(400_000m);
        var admin = await ActorWithOpenShiftAsync(Roles.Admin);

        // 1-qadam: admin kassirning to'lovini storno qiladi — bu MUMKIN.
        var first = await admin.Client.PostAsJsonAsync(
            $"/api/admin/payments/{original.PaymentId}/reverse", new { reason = "Xato summa" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var storno = await first.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(storno);
        Assert.Equal(original.PaymentId, storno.ReversalOf);

        // 2-qadam: endi O'ZI yaratgan storno qatorini storno qilmoqchi.
        var second = await admin.Client.PostAsJsonAsync(
            $"/api/admin/payments/{storno.Id}/reverse", new { reason = "Yana o'ylab ko'rdim" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = await second.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("cannot_reverse_reversal", error?.Code);
        Assert.Contains("storno", error?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // Zanjir uzunligi ikkitadan oshmadi: original + bitta storno.
        await using var db = NewDb();
        Assert.Equal(2, await db.Payments.AsNoTracking()
            .CountAsync(p => p.Id == original.PaymentId || p.ReversalOf == original.PaymentId));
        Assert.False(await db.Payments.AsNoTracking().AnyAsync(p => p.ReversalOf == storno.Id));
    }

    /// <summary>
    /// Bitta to'lovni IKKI marta storno qilib bo'lmaydi —
    /// <b>409 <c>already_reversed</c></b> (pulni ikki marta "qaytarish").
    /// </summary>
    [Fact]
    public async Task Bir_tolov_ikki_marta_storno_qilinmaydi_409()
    {
        var original = await PaymentByCashierAsync(250_000m);
        var admin = await ActorWithOpenShiftAsync(Roles.Admin);
        var director = await ActorWithOpenShiftAsync(Roles.SuperAdmin);

        var first = await admin.Client.PostAsJsonAsync(
            $"/api/admin/payments/{original.PaymentId}/reverse", new { reason = "Birinchi storno" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Boshqa odam (direktor) urinsa ham — baribir 409.
        var second = await director.Client.PostAsJsonAsync(
            $"/api/admin/payments/{original.PaymentId}/reverse", new { reason = "Ikkinchi storno" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = await second.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("already_reversed", error?.Code);

        await using var db = NewDb();
        Assert.Single(await db.Payments.AsNoTracking()
            .Where(p => p.ReversalOf == original.PaymentId).ToListAsync());
    }

    /// <summary>
    /// SPEC §4.5 — ikki qavatli nazorat. Admin kassa oldida turib pul olsa,
    /// O'SHA to'lovni o'zi storno qila olmaydi: <b>403
    /// <c>own_payment_reversal</c></b>. Rol yetarli emas, ikkinchi SHAXS
    /// kerak.
    /// </summary>
    [Fact]
    public async Task Oz_qabul_qilgan_tolovini_ozi_storno_qila_olmaydi_403()
    {
        var admin = await ActorWithOpenShiftAsync(Roles.Admin);
        var payment = await AcceptAsync(admin, 180_000m);

        var response = await admin.Client.PostAsJsonAsync(
            $"/api/admin/payments/{payment.Id}/reverse", new { reason = "O'zim tuzataman" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("own_payment_reversal", error?.Code);

        await using var db = NewDb();
        Assert.False(await db.Payments.AsNoTracking().AnyAsync(p => p.ReversalOf == payment.Id));
    }

    /// <summary>Storno sababi bo'sh bo'lsa — 400, va hech narsa yozilmaydi (SPEC §4.3).</summary>
    [Fact]
    public async Task Storno_sababisiz_400_reason_required()
    {
        var original = await PaymentByCashierAsync(90_000m);
        var admin = await ActorWithOpenShiftAsync(Roles.Admin);

        var response = await admin.Client.PostAsJsonAsync(
            $"/api/admin/payments/{original.PaymentId}/reverse", new { reason = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("reason_required", error?.Code);

        await using var db = NewDb();
        Assert.False(await db.Payments.AsNoTracking().AnyAsync(p => p.ReversalOf == original.PaymentId));
    }

    // =====================================================================
    //  4. IZOLYATSIYA (IDOR) — boshqa kassirning yozuvlari
    // =====================================================================

    /// <summary>
    /// Kassir boshqa kassirning chekini so'rasa — <b>404</b>, 403 emas:
    /// "bunday chek bor, lekin senga ko'rsatmaymiz" javobining o'zi ham
    /// ma'lumot (kim, qachon, qancha oldi degan savolga yarim javob).
    /// Nazorat sifatida admin AYNAN o'sha chekni ko'ra olishi tekshiriladi —
    /// aks holda test "endpoint umuman ishlamayapti" holatida ham yashil
    /// bo'lardi.
    /// </summary>
    [Fact]
    public async Task Kassir_boshqa_kassirning_chekini_kora_olmaydi_404()
    {
        var owner = await CashierWithOpenShiftAsync();
        var payment = await AcceptAsync(owner, 130_000m);
        var stranger = await CashierWithOpenShiftAsync();

        var response = await stranger.Client.GetAsync($"/api/billing/payments/{payment.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal("payment_not_found", error?.Code);

        // Nazorat: egasi ko'radi.
        var ownerResponse = await owner.Client.GetAsync($"/api/billing/payments/{payment.Id}");
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        var mine = await ownerResponse.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.Equal(owner.User.Id, mine?.CashierId);

        // Nazorat: admin ham ko'radi (SPEC §4.3 — nazoratchi kesim ko'radi).
        var admin = await ActorAsync(Roles.Admin);
        var adminResponse = await admin.Client.GetAsync($"/api/billing/payments/{payment.Id}");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        var seen = await adminResponse.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.Equal(payment.Id, seen?.Id);
        Assert.Equal(130_000m, seen?.Amount);
    }

    /// <summary>
    /// Ro'yxat so'rovida boshqa kassirning id'sini yozib yuborish ham
    /// yordam bermaydi: so'rov majburan chaqiruvchining o'ziga toraytiriladi.
    /// </summary>
    [Fact]
    public async Task Kassir_royxatni_boshqa_kassir_id_si_bilan_sorasa_ham_faqat_ozinikini_koradi()
    {
        var owner = await CashierWithOpenShiftAsync();
        var hidden = await AcceptAsync(owner, 175_000m);

        var stranger = await CashierWithOpenShiftAsync();
        var mine = await AcceptAsync(stranger, 25_000m);

        var response = await stranger.Client.GetAsync($"/api/billing/payments?cashierId={owner.User.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<PaymentDto>>();
        Assert.NotNull(list);
        Assert.DoesNotContain(list, p => p.Id == hidden.Id);
        Assert.Contains(list, p => p.Id == mine.Id);
        Assert.All(list, p => Assert.Equal(stranger.User.Id, p.CashierId));
    }

    /// <summary>
    /// Boshqa kassirning smenasini yopishga urinish — <b>403
    /// <c>not_your_shift</c></b>, va o'sha smena OCHIQ qoladi. Yopish
    /// sanalgan naqdni yozib qo'yardi, ya'ni o'zganing nomidan
    /// nomuvofiqlik yaratish yo'li bo'lardi.
    /// </summary>
    [Fact]
    public async Task Kassir_boshqaning_smenasini_yopa_olmaydi_403()
    {
        var victim = await CashierWithOpenShiftAsync();
        var attacker = await CashierWithOpenShiftAsync();

        var response = await attacker.Client.PostAsJsonAsync(
            $"/api/cash/shifts/{victim.ShiftId}/close", new { countedCash = 0m, note = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ShiftErrorDto>();
        Assert.Equal(CashShiftError.NotYourShift, error?.Code);

        await using var db = NewDb();
        var shift = await db.CashShifts.AsNoTracking().SingleAsync(s => s.Id == victim.ShiftId);
        Assert.Equal(CashShiftStatus.Open, shift.Status);
        Assert.Null(shift.ClosedBy);
        Assert.Null(shift.CountedCash);
    }

    /// <summary>
    /// Kassalar modeli (2026-09): smena so'rovdan qabul qilinmaydi (SPEC
    /// §4.4 — server aniqlaydi) qoidasi o'zgarmadi, lekin endi to'lov
    /// UMUMAN smenaga bog'liq emas — "boshqaning ochiq smenasidan
    /// foydalanish" degan xavf ham YO'QOLDI, chunki bunday bog'lanishning
    /// o'zi yo'q. Kassirda ochiq smena bo'lmasa ham to'lov MUVAFFAQIYATLI
    /// o'tadi va boshqa hech kimning (jumladan "qurbon" kassirning)
    /// smenasiga UMUMAN tegmaydi.
    /// </summary>
    [Fact]
    public async Task Ochiq_smenasi_yoq_kassir_ham_tolov_yoza_oladi_va_boshqaning_smenasiga_tegmaydi()
    {
        var victim = await CashierWithOpenShiftAsync();

        // Bu kassir ATAYLAB smena ochmaydi — kassalar modelida bu endi shart emas.
        var (attacker, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        using var client = ClientFor(attacker);
        var studentId = await NewStudentAsync();
        var invoiceId = await NewInvoiceAsync(studentId, 100_000m);

        var response = await client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId,
            amount = 100_000m,
            method = PaymentMethod.Cash,
            allocations = new[] { new { invoiceId, amount = 100_000m } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.Null(dto?.CashShiftId);

        await using var db = NewDb();
        // "Qurbon" kassirning smenasiga BU to'lov UMUMAN tegmadi.
        Assert.False(await db.Payments.AsNoTracking().AnyAsync(p => p.CashShiftId == victim.ShiftId
                                                                    && p.CashierId == attacker.Id));
        Assert.True(await db.Payments.AsNoTracking().AnyAsync(p => p.StudentId == studentId
                                                                    && p.CashierId == attacker.Id
                                                                    && p.CashShiftId == null));
    }

    /// <summary>
    /// Mavjud bo'lmagan to'lov id'si ham <b>404</b> beradi — ya'ni
    /// "boshqaning cheki" va "umuman yo'q chek" javoblari BIR XIL.
    /// Farq bo'lsa, tasodifiy id'larni terib chek bor-yo'qligini aniqlash
    /// mumkin bo'lardi.
    /// </summary>
    [Fact]
    public async Task Yoq_tolov_va_boshqaning_tolovi_bir_xil_javob_beradi()
    {
        var owner = await CashierWithOpenShiftAsync();
        var payment = await AcceptAsync(owner, 60_000m);
        var stranger = await CashierWithOpenShiftAsync();

        var foreign = await stranger.Client.GetAsync($"/api/billing/payments/{payment.Id}");
        var missing = await stranger.Client.GetAsync($"/api/billing/payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var foreignBody = await foreign.Content.ReadFromJsonAsync<PaymentErrorDto>();
        var missingBody = await missing.Content.ReadFromJsonAsync<PaymentErrorDto>();
        Assert.Equal(missingBody?.Code, foreignBody?.Code);
        Assert.Equal(missingBody?.Message, foreignBody?.Message);
    }

    // =====================================================================
    //  Test ma'lumoti
    // =====================================================================

    private sealed record Actor(AppUser User, HttpClient Client, Guid ShiftId);

    private sealed record Original(string CashierId, Guid ShiftId, Guid PaymentId);

    /// <summary>Smena xatosining javob shakli (`CashShiftsController.Failure`).</summary>
    private sealed record ShiftErrorDto(string Code, string Message);

    private async Task<Actor> ActorAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        return new Actor(user, ClientFor(user), Guid.Empty);
    }

    private async Task<Actor> ActorWithOpenShiftAsync(string role)
    {
        var actor = await ActorAsync(role);

        var response = await actor.Client.PostAsJsonAsync("/api/cash/shifts/open", new { openingFloat = 0m });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var shift = await response.Content.ReadFromJsonAsync<CashShiftDto>();
        Assert.NotNull(shift);

        return actor with { ShiftId = shift.Id };
    }

    private Task<Actor> CashierWithOpenShiftAsync() => ActorWithOpenShiftAsync(Roles.Cashier);

    /// <summary>Chaqiruvchi nomidan haqiqiy to'lov qabul qiladi (HTTP orqali).</summary>
    private async Task<PaymentDto> AcceptAsync(Actor actor, decimal amount)
    {
        var studentId = await NewStudentAsync();
        var invoiceId = await NewInvoiceAsync(studentId, amount);

        var response = await actor.Client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId,
            amount,
            method = PaymentMethod.Cash,
            allocations = new[] { new { invoiceId, amount } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(dto);
        return dto;
    }

    private async Task<Original> PaymentByCashierAsync(decimal amount)
    {
        var cashier = await CashierWithOpenShiftAsync();
        var payment = await AcceptAsync(cashier, amount);
        return new Original(cashier.User.Id, cashier.ShiftId, payment.Id);
    }

    private async Task<string> NewStudentAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var student = new Student { FullName = $"Identity o'quvchi {suffix}", ClassName = "1-A" };

        await using var db = NewDb();
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return student.Id;
    }

    private async Task<Guid> NewInvoiceAsync(string studentId, decimal amount)
    {
        var invoice = new Invoice
        {
            StudentId = studentId,
            CategoryId = TuitionCategoryId,
            PeriodMonth = new DateOnly(2026, 9, 1),
            Amount = amount,
            Discount = 0m,
            DueOn = new DateOnly(2026, 9, 10),
            Status = InvoiceStatus.Open,
            CreatedAt = AppClock.NowInstant,
        };

        await using var db = NewDb();
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    // ---------------------------------------------------------------------
    //  DI bo'shlig'i (P1-15 gacha) — docs/PENDING_WIRING.md
    // ---------------------------------------------------------------------

    private static readonly Lock WireLock = new();
    private static WebApplicationFactory<AuthController>? _wired;

    private WebApplicationFactory<AuthController> Wired
    {
        get
        {
            lock (WireLock)
                return _wired ??= fixture.Api.WithWebHostBuilder(builder =>
                    builder.ConfigureServices(services =>
                    {
                        services.AddScoped<ILedgerService, LedgerService>();
                        services.AddScoped<ICashShiftService, CashShiftService>();
                        services.AddScoped<IPaymentService, PaymentService>();
                    }));
        }
    }

    private HttpClient ClientFor(AppUser user)
    {
        var client = Wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.Api.TokenFor(user.Role, user.Id, user.FullName, user.Email));
        return client;
    }
}
