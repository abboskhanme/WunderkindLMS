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
//  SPEC §4.3 — ROL CHEGARALARI, HAQIQIY HTTP SO'ROVLAR BILAN. Vazifa: P1-22.
// ===========================================================================
//
//  `CashierRoleTests` §4.3 jadvalini MA'LUMOT sifatida tekshiradi
//  (`FinanceMatrix.IsAllowed(...)`). Bu yetarli emas: jadval to'g'ri
//  yozilgani bilan endpoint'ga atribut qo'yish UNUTILGAN bo'lishi mumkin —
//  o'shanda matritsa testi yashil, eshik esa ochiq bo'lardi. Shuning uchun
//  bu yerda har katak HAQIQIY so'rov bilan tekshiriladi.
//
//  JADVAL: 8 QATOR × 3 ROL = 24 KATAK
//  ----------------------------------
//  | # | Amal (SPEC §4.3)              | cashier | admin | director |
//  |---|-------------------------------|---------|-------|----------|
//  | 1 | To'lov qabul qilish, chek     |   200   |  200  |   200    |
//  | 2 | To'lovni storno qilish        |   403   |  200  |   200    |
//  | 3 | To'lovni tahrirlash/o'chirish |   404   |  404  |   404    |
//  | 4 | Oylik narx / obuna            |   403   |  200  |   200    |
//  | 5 | Chegirma berish               |   403   |  200  |   200    |
//  | 6 | O'z smenasini yopish          |   200   |  200  |   200    |
//  | 7 | Kassirlar kesimida variance   |   403   |  200  |   200    |
//  | 8 | Chiqim yozish                 |   404   |  404  |   404    |
//
//  Har qator — bitta `[Theory]`, uchta `[InlineData]`. Ya'ni xUnit 24 ta
//  ALOHIDA test holatini ko'rsatadi va kutilgan status kodi har katakda
//  KO'RINIB turadi. Ro'yxat bo'ylab aylanadigan bitta test bunday
//  bo'lmasdi: ro'yxat bo'shab qolsa u jimgina yashil bo'lardi.
//
//  3-QATOR (404) NEGA SHUNDAY
//  --------------------------
//  "Edit or delete a payment — impossible for anyone". To'lovni
//  o'zgartiradigan yoki o'chiradigan endpoint YOZILMAGAN va yozilmaydi
//  (`PaymentsController` fayl boshidagi izoh), shuning uchun `PUT` va
//  `DELETE` uchun mos endpoint topilmaydi → 404. Javob
//  AVTORIZATSIYADAN OLDIN beriladi, ya'ni rol umuman ahamiyatsiz —
//  aynan §4.3 aytgan "hech kimga mumkin emas" holati. Kimdir bunday
//  endpoint qo'shsa, uchala katak ham darhol qizil bo'ladi.
//
//  8-QATOR (404) — ISBOTLANMAGAN KATAK, DIQQAT
//  -------------------------------------------
//  Chiqim endpoint'i (`POST /api/admin/billing/expenses`) HALI YOZILMAGAN:
//  muzlatilgan klient shartnomasida u `notImplemented(..., 'P1-13')` deb
//  turibdi, `expenses` jadvali esa bor. Ya'ni bu qatorning ✅ kataklarini
//  (admin/direktor chiqim yoza oladi) HTTP orqali isbotlab BO'LMAYDI — u
//  amal umuman mavjud emas. Isbotlanadigan qismi shu: BUGUN hech kim,
//  jumladan kassir ham, chiqim yoza olmaydi. Endpoint paydo bo'lgan kuni
//  bu uch katak qizil bo'ladi va uni yozgan odam matritsani yangilashga
//  majbur bo'ladi — ataylab shunday.
//
//  DI (P1-15 gacha)
//  ----------------
//  Moliya xizmatlari `Program.cs` da hali ro'yxatdan o'tmagan
//  (docs/PENDING_WIRING.md). Boshqa test fayllaridagi kabi shu yerda ham
//  `WithWebHostBuilder` bilan HOSILA fabrika ko'tariladi — o'sha baza,
//  o'sha JWT kaliti, faqat beshta `AddScoped` qo'shiladi. Prod fayllariga
//  TEGILMAYDI.
// ===========================================================================

/// <summary>
/// SPEC §4.3 rol matritsasi — 8 amal × 3 rol, haqiqiy HTTP so'rovlar bilan
/// (P1-22). Batafsil: fayl boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class RbacMatrixTests(ApiFixture fixture)
{
    private static readonly Guid TuitionCategoryId = new("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid BusCategoryId = new("00000000-0000-0000-0000-0000000000c2");

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  1-QATOR: "Accept payment, print receipt" — ✅ ✅ ✅
    // =====================================================================

    /// <summary>
    /// Kassa oldida uchala rol ham tura oladi (SPEC §4.3 birinchi qatori):
    /// kichik maktabda direktor kassirni almashtiradi.
    /// </summary>
    [Theory]
    [InlineData(Roles.Cashier, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    [InlineData(Roles.SuperAdmin, HttpStatusCode.OK)]
    public async Task Qator1_Tolov_qabul_qilish(string role, HttpStatusCode expected)
    {
        var actor = await ActorWithOpenShiftAsync(role);
        var studentId = await NewStudentAsync();
        var invoiceId = await NewInvoiceAsync(studentId, TuitionCategoryId, 500_000m);

        var response = await actor.Client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId,
            amount = 500_000m,
            method = PaymentMethod.Cash,
            allocations = new[] { new { invoiceId, amount = 500_000m } },
        });

        Assert.Equal(expected, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(dto);
        Assert.True(dto.ReceiptNo > 0, "Chek raqami berilmadi.");
        Assert.Equal(500_000m, dto.Amount);
        // SPEC §4.4 — kassir JWT'dan, so'rovdan emas.
        Assert.Equal(actor.User.Id, dto.CashierId);
        // "Smena" endi yo'q (kassalar modeli, 2026-09) — yangi to'lovda har doim null.
        Assert.Null(dto.CashShiftId);
        Assert.NotNull(dto.CashBoxId);

        await using var db = NewDb();
        Assert.Equal(actor.User.Id, await db.Payments.AsNoTracking()
            .Where(p => p.Id == dto.Id).Select(p => p.CashierId).SingleAsync());
    }

    // =====================================================================
    //  2-QATOR: "Reverse a payment" — ⛔ ✅ ✅
    // =====================================================================

    /// <summary>
    /// Kassirga storno YOPIQ: "pulni oldim, keyin o'zim bekor qildim" —
    /// mijoz tasvirlagan firibgarlikning aynan o'zi. Rad etilgan holatda
    /// bazada BIRORTA storno qatori paydo bo'lmasligi ham tekshiriladi:
    /// 403 qaytib, yozuv baribir tushib qolishi eng yomon holat bo'lardi.
    /// </summary>
    [Theory]
    [InlineData(Roles.Cashier, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    [InlineData(Roles.SuperAdmin, HttpStatusCode.OK)]
    public async Task Qator2_Tolovni_storno_qilish(string role, HttpStatusCode expected)
    {
        var original = await PaymentByAnotherCashierAsync(300_000m);
        var actor = await ActorWithOpenShiftAsync(role);

        var response = await actor.Client.PostAsJsonAsync(
            $"/api/admin/payments/{original.PaymentId}/reverse",
            new { reason = "Kassir summani xato kiritdi" });

        Assert.Equal(expected, response.StatusCode);

        await using var db = NewDb();
        var storno = await db.Payments.AsNoTracking()
            .SingleOrDefaultAsync(p => p.ReversalOf == original.PaymentId);

        if (expected == HttpStatusCode.OK)
        {
            var dto = await response.Content.ReadFromJsonAsync<PaymentDto>();
            Assert.NotNull(dto);
            Assert.Equal(original.PaymentId, dto.ReversalOf);
            Assert.Equal(300_000m, dto.Amount);
            // Storno tasdiqlovchi nomidan yoziladi (SPEC §4.5, P1-11); "smena" endi yo'q.
            Assert.Equal(actor.User.Id, dto.CashierId);
            Assert.Null(dto.CashShiftId);
            Assert.NotNull(dto.CashBoxId);
            Assert.Equal("Kassir summani xato kiritdi", dto.Note);

            Assert.NotNull(storno);
            Assert.Equal(dto.Id, storno.Id);
        }
        else
        {
            // 403 — `ForbidResult`, tanasi bo'sh. Haqiqiy dalil bazada.
            Assert.Empty(await response.Content.ReadAsStringAsync());
            Assert.Null(storno);
        }
    }

    // =====================================================================
    //  3-QATOR: "Edit or delete a payment" — ⛔ ⛔ ⛔ (hech kimga)
    // =====================================================================

    /// <summary>
    /// To'lovni o'chiradigan endpoint YO'Q va bo'lmaydi → <b>404</b>, va bu
    /// javob rolga umuman qaramaydi. Qator bazada tegilmasdan qolishi ham
    /// tekshiriladi: 404 qaytib, o'chirish baribir bajarilishi mumkin
    /// bo'lgan holat (boshqa marshrut ushlab olishi) e'tiborsiz qolmasin.
    /// </summary>
    [Theory]
    [InlineData(Roles.Cashier, HttpStatusCode.NotFound)]
    [InlineData(Roles.Admin, HttpStatusCode.NotFound)]
    [InlineData(Roles.SuperAdmin, HttpStatusCode.NotFound)]
    public async Task Qator3_Tolovni_ochirish_hech_kimga(string role, HttpStatusCode expected)
    {
        var original = await PaymentByAnotherCashierAsync(120_000m);
        var actor = await ActorAsync(role);

        var response = await actor.Client.DeleteAsync($"/api/billing/payments/{original.PaymentId}");

        Assert.Equal(expected, response.StatusCode);

        await using var db = NewDb();
        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == original.PaymentId);
        Assert.Equal(120_000m, payment.Amount);
    }

    /// <summary>
    /// To'lovni TAHRIRLASH ham xuddi shunday: <c>PUT</c> uchun ham endpoint yo'q.
    /// (Matritsaning 3-qatori — bitta amal, ikkita fe'l.)
    /// </summary>
    [Theory]
    [InlineData(Roles.Cashier)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Qator3_Tolovni_tahrirlash_hech_kimga(string role)
    {
        var original = await PaymentByAnotherCashierAsync(120_000m);
        var actor = await ActorAsync(role);

        var response = await actor.Client.PutAsJsonAsync(
            $"/api/billing/payments/{original.PaymentId}", new { amount = 1m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var db = NewDb();
        Assert.Equal(120_000m, await db.Payments.AsNoTracking()
            .Where(p => p.Id == original.PaymentId).Select(p => p.Amount).SingleAsync());
    }

    // =====================================================================
    //  4-QATOR: "Change monthly fee / subscription" — ⛔ ✅ ✅
    // =====================================================================

    /// <summary>
    /// Oylik narxni kassir o'zgartira olmaydi: aks holda u qarzni "nolga
    /// tushirib", so'ng naqdni cho'ntagiga solardi va hisob-kitob to'g'ri
    /// ko'rinardi.
    /// </summary>
    [Theory]
    [InlineData(Roles.Cashier, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    [InlineData(Roles.SuperAdmin, HttpStatusCode.OK)]
    public async Task Qator4_Oylik_narx_va_obunani_ozgartirish(string role, HttpStatusCode expected)
    {
        var studentId = await NewStudentAsync();
        var actor = await ActorAsync(role);

        var response = await actor.Client.PostAsJsonAsync("/api/admin/billing/subscriptions", new
        {
            studentId,
            categoryId = BusCategoryId,
            monthlyAmount = 250_000m,
            detail = "3-yo'nalish",
            startsOn = "2026-09-01",
        });

        Assert.Equal(expected, response.StatusCode);

        await using var db = NewDb();
        var saved = await db.StudentSubscriptions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.StudentId == studentId);

        if (expected == HttpStatusCode.OK)
        {
            var dto = await response.Content.ReadFromJsonAsync<StudentSubscriptionDto>();
            Assert.NotNull(dto);
            Assert.Equal(250_000m, dto.MonthlyAmount);
            Assert.Equal("3-yo'nalish", dto.Detail);
            Assert.Equal(BusCategoryId, dto.CategoryId);

            Assert.NotNull(saved);
            Assert.Equal(250_000m, saved.MonthlyAmount);
            Assert.Equal(actor.User.Id, saved.CreatedBy);    // §4.4 — JWT'dan
        }
        else
        {
            Assert.Null(saved);
        }
    }

    // =====================================================================
    //  5-QATOR: "Grant a discount" — ⛔ ✅ ✅
    // =====================================================================

    /// <summary>
    /// Chegirma — pulni kamaytirishning eng jimgina yo'li, shuning uchun
    /// kassirga yopiq. Admin va direktor SO'RAY oladi, lekin so'rov har
    /// doim <c>pending</c> tug'iladi (mijoz javobi, SPEC §8.1 Q5) — tasdiq
    /// alohida amal (`ApproveDiscount`, faqat direktor).
    /// </summary>
    [Theory]
    [InlineData(Roles.Cashier, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    [InlineData(Roles.SuperAdmin, HttpStatusCode.OK)]
    public async Task Qator5_Chegirma_berish(string role, HttpStatusCode expected)
    {
        var studentId = await NewStudentAsync();
        var actor = await ActorAsync(role);

        var response = await actor.Client.PostAsJsonAsync("/api/admin/billing/discounts", new
        {
            studentId,
            categoryId = TuitionCategoryId,
            percent = 20m,
            amount = 0m,
            reason = "Ko'p bolali oila",
            startsOn = "2026-09-01",
        });

        Assert.Equal(expected, response.StatusCode);

        await using var db = NewDb();
        var saved = await db.Discounts.AsNoTracking()
            .SingleOrDefaultAsync(d => d.StudentId == studentId);

        if (expected == HttpStatusCode.OK)
        {
            var dto = await response.Content.ReadFromJsonAsync<DiscountDto>();
            Assert.NotNull(dto);
            Assert.Equal(20m, dto.Percent);
            Assert.Equal("Ko'p bolali oila", dto.Reason);
            Assert.Equal(DiscountStatus.Pending, dto.Status);
            Assert.Null(dto.ApprovedByName);

            Assert.NotNull(saved);
            Assert.Equal(actor.User.Id, saved.CreatedBy);    // §4.4 — JWT'dan
            Assert.Null(saved.ApprovedBy);
        }
        else
        {
            Assert.Null(saved);
        }
    }

    // =====================================================================
    //  6-QATOR: "Close own shift" — ✅ ✅ ✅
    // =====================================================================

    /// <summary>
    /// O'Z smenasini uchala rol ham yopa oladi. Yopishda sanalgan naqd
    /// MAJBURIY (SPEC §4.2) va <c>variance</c> serverda hisoblanadi.
    /// </summary>
    [Theory]
    [InlineData(Roles.Cashier, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    [InlineData(Roles.SuperAdmin, HttpStatusCode.OK)]
    public async Task Qator6_Oz_smenasini_yopish(string role, HttpStatusCode expected)
    {
        var actor = await ActorWithOpenShiftAsync(role, openingFloat: 100_000m);

        var response = await actor.Client.PostAsJsonAsync(
            $"/api/cash/shifts/{actor.ShiftId}/close", new { countedCash = 100_000m, note = "Kun yakuni" });

        Assert.Equal(expected, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<CashShiftDto>();
        Assert.NotNull(dto);
        Assert.Equal(CashShiftStatus.Closed, dto.Status);
        Assert.Equal(100_000m, dto.CountedCash);
        Assert.Equal(100_000m, dto.ExpectedCash);
        Assert.Equal(0m, dto.Variance);

        await using var db = NewDb();
        var shift = await db.CashShifts.AsNoTracking().SingleAsync(s => s.Id == actor.ShiftId);
        Assert.Equal(CashShiftStatus.Closed, shift.Status);
        Assert.Equal(actor.User.Id, shift.ClosedBy);
    }

    // =====================================================================
    //  7-QATOR: "See variance report across cashiers" — ⛔ ✅ ✅
    // =====================================================================

    /// <summary>
    /// Boshqa kassirning nomuvofiqligini kassir KO'RA OLMAYDI. Bu qator
    /// firibgarlikni ANIQLASH bilan bog'liq: kam chiqqan kassa haqidagi
    /// ma'lumot faqat nazoratchida bo'lishi kerak.
    ///
    /// <para>
    /// So'rov — boshqa kassirning yopilgan smenasining Z-hisoboti; unda
    /// <c>variance</c> aynan ko'rinadi. Kassir uchun javob 403 va
    /// <c>not_your_shift</c> kodi, ya'ni raqam UMUMAN chiqmaydi.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(Roles.Cashier, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    [InlineData(Roles.SuperAdmin, HttpStatusCode.OK)]
    public async Task Qator7_Kassirlar_kesimidagi_variance_hisoboti(string role, HttpStatusCode expected)
    {
        var other = await ShiftClosedWithVarianceAsync(shortfall: 50_000m);
        var actor = await ActorAsync(role);

        var response = await actor.Client.GetAsync($"/api/cash/shifts/{other.ShiftId}/z-report");

        Assert.Equal(expected, response.StatusCode);

        if (expected == HttpStatusCode.OK)
        {
            var report = await response.Content.ReadFromJsonAsync<ZReportDto>();
            Assert.NotNull(report);
            Assert.Equal(other.CashierId, report.Shift.CashierId);
            Assert.Equal(-50_000m, report.Shift.Variance);
            Assert.Equal(other.PaymentAmount, report.Shift.ExpectedCash);
            Assert.Contains(report.ByMethod, m => m.Method == PaymentMethod.Cash
                                                  && m.Amount == other.PaymentAmount);
        }
        else
        {
            var error = await response.Content.ReadFromJsonAsync<ShiftErrorDto>();
            Assert.Equal(CashShiftError.NotYourShift, error?.Code);
            // Raqamning O'ZI javobda umuman uchramasin.
            Assert.DoesNotContain("50000", await response.Content.ReadAsStringAsync());
        }
    }

    /// <summary>
    /// Ro'yxat endpoint'i esa 403 BERMAYDI — u so'rovni JIMGINA chaqiruvchining
    /// o'z smenalari bilan cheklaydi (P1-10 shartnomasi). Natija bir xil:
    /// kassir boshqa kassirning smenasini ko'rmaydi. Bu 7-qatorning ikkinchi
    /// yuzasi va uni alohida tekshirish shart — "200 OK" ni "ruxsat berildi"
    /// deb o'qib qo'yish oson.
    /// </summary>
    [Fact]
    public async Task Qator7_Kassir_royxatda_boshqa_kassirning_smenasini_kormaydi()
    {
        var other = await ShiftClosedWithVarianceAsync(shortfall: 10_000m);
        var actor = await ActorWithOpenShiftAsync(Roles.Cashier);

        // Ataylab BOSHQA kassirning id'si so'raladi.
        var response = await actor.Client.GetAsync(
            $"/api/cash/shifts?cashierId={other.CashierId}&onlyWithVariance=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<CashShiftDto>>();
        Assert.NotNull(list);
        Assert.DoesNotContain(list, s => s.CashierId == other.CashierId);
        Assert.DoesNotContain(list, s => s.Id == other.ShiftId);
        Assert.All(list, s => Assert.Equal(actor.User.Id, s.CashierId));

        // Nazorat: admin AYNAN o'sha so'rovda o'sha smenani KO'RADI.
        var supervisor = await ActorAsync(Roles.Admin);
        var adminResponse = await supervisor.Client.GetAsync(
            $"/api/cash/shifts?cashierId={other.CashierId}&onlyWithVariance=true");

        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        var adminList = await adminResponse.Content.ReadFromJsonAsync<List<CashShiftDto>>();
        Assert.NotNull(adminList);
        Assert.Contains(adminList, s => s.Id == other.ShiftId && s.Variance == -10_000m);
    }

    // =====================================================================
    //  8-QATOR: "Record an expense" — ⛔ ✅ ✅ (endpoint hali YO'Q)
    // =====================================================================

    /// <summary>
    /// <b>DIQQAT — bu qatorning ✅ kataklari isbotlanmagan.</b> Chiqim
    /// endpoint'i yozilmagan (muzlatilgan klient shartnomasida
    /// <c>notImplemented('POST /admin/billing/expenses', 'P1-13')</c>),
    /// shuning uchun uchala rol ham 404 oladi.
    ///
    /// <para>
    /// Isbotlanadigan qism: bugun HECH KIM, jumladan kassir ham, chiqim
    /// yoza olmaydi — ya'ni §4.3 ning ⛔ katagi buzilmagan. Endpoint
    /// qo'shilgan kuni bu test qizil bo'ladi va uni yozgan odam matritsani
    /// yangilashi kerak bo'ladi. Ataylab shunday: "unutilgan RBAC" jimgina
    /// o'tib ketmasin.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(Roles.Cashier, HttpStatusCode.NotFound)]
    [InlineData(Roles.Admin, HttpStatusCode.NotFound)]
    [InlineData(Roles.SuperAdmin, HttpStatusCode.NotFound)]
    public async Task Qator8_Chiqim_yozish(string role, HttpStatusCode expected)
    {
        var actor = await ActorAsync(role);
        var note = "P1-22 chiqim sinovi " + Guid.NewGuid().ToString("N")[..8];

        var response = await actor.Client.PostAsJsonAsync("/api/admin/billing/expenses", new
        {
            onDate = "2026-09-12",
            category = "utilities",
            amount = 4_200_000m,
            note,
        });

        Assert.Equal(expected, response.StatusCode);

        await using var db = NewDb();
        Assert.False(await db.Expenses.AsNoTracking().AnyAsync(e => e.Note == note),
            "Chiqim qatori yozildi — endpoint paydo bo'libdi, SPEC §4.3 ning 8-qatorini "
            + "haqiqiy status kodlari bilan qayta yozing (kassir → 403).");
    }

    // =====================================================================
    //  Darvozaning o'zi: token yo'q / muddati o'tgan / notanish rol
    // =====================================================================

    /// <summary>
    /// Token'siz so'rov moliya endpoint'iga UMUMAN kirmasligi kerak.
    /// Uchala asosiy marshrut ham tekshiriladi: bittasida <c>[Authorize]</c>
    /// unutilsa, qolgani yashil bo'lib uni yashirardi.
    /// </summary>
    [Theory]
    [InlineData("/api/cash/payments")]
    [InlineData("/api/cash/shifts/open")]
    [InlineData("/api/admin/billing/discounts")]
    public async Task Tokensiz_soov_401(string path)
    {
        using var client = AnonymousClient();

        var response = await client.PostAsJsonAsync(path, new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Muddati o'tgan token — 401. JWT standart clock skew'i 5 daqiqa,
    /// shuning uchun -10 daqiqa beriladi.
    /// </summary>
    [Fact]
    public async Task Muddati_otgan_token_401()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        var expired = fixture.Api.TokenFor(
            Roles.Cashier, user.Id, user.FullName, user.Email, lifetime: TimeSpan.FromMinutes(-10));
        using var client = ClientWithToken(expired);

        var response = await client.GetAsync("/api/cash/shifts/current");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// <c>staff</c> — SPEC §4.3 da bunday ustun YO'Q. Yangi moliya
    /// yuzasining har bir amali unga yopiq bo'lishi kerak (fail-closed):
    /// eski "finance" ruxsat kaliti bu yerga o'tmaydi.
    /// </summary>
    [Theory]
    [InlineData("/api/cash/payments")]
    [InlineData("/api/cash/shifts/open")]
    [InlineData("/api/admin/billing/subscriptions")]
    [InlineData("/api/admin/billing/discounts")]
    public async Task Staff_yangi_moliya_yuzasiga_kira_olmaydi_403(string path)
    {
        var actor = await ActorAsync(Roles.Staff, "finance");

        var response = await actor.Client.PostAsJsonAsync(path, new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Matritsa MA'LUMOTI bilan haqiqiy endpoint javobi bir-biriga MOS
    /// kelishini tekshiradi. Yuqoridagi kataklar HTTP javobini o'lchaydi,
    /// bu esa <see cref="FinanceMatrix"/> o'sha javobni AYTIB turganini —
    /// ya'ni jadval va kod ajralib ketmaganini.
    /// </summary>
    [Theory]
    [InlineData(FinanceAction.AcceptPayment, Roles.Cashier, true)]
    [InlineData(FinanceAction.AcceptPayment, Roles.Admin, true)]
    [InlineData(FinanceAction.AcceptPayment, Roles.SuperAdmin, true)]
    [InlineData(FinanceAction.ReversePayment, Roles.Cashier, false)]
    [InlineData(FinanceAction.ReversePayment, Roles.Admin, true)]
    [InlineData(FinanceAction.ReversePayment, Roles.SuperAdmin, true)]
    [InlineData(FinanceAction.EditOrDeletePayment, Roles.Cashier, false)]
    [InlineData(FinanceAction.EditOrDeletePayment, Roles.Admin, false)]
    [InlineData(FinanceAction.EditOrDeletePayment, Roles.SuperAdmin, false)]
    [InlineData(FinanceAction.ManageSubscriptions, Roles.Cashier, false)]
    [InlineData(FinanceAction.ManageSubscriptions, Roles.Admin, true)]
    [InlineData(FinanceAction.ManageSubscriptions, Roles.SuperAdmin, true)]
    [InlineData(FinanceAction.GrantDiscount, Roles.Cashier, false)]
    [InlineData(FinanceAction.GrantDiscount, Roles.Admin, true)]
    [InlineData(FinanceAction.GrantDiscount, Roles.SuperAdmin, true)]
    [InlineData(FinanceAction.ManageOwnShift, Roles.Cashier, true)]
    [InlineData(FinanceAction.ManageOwnShift, Roles.Admin, true)]
    [InlineData(FinanceAction.ManageOwnShift, Roles.SuperAdmin, true)]
    [InlineData(FinanceAction.ViewVarianceReport, Roles.Cashier, false)]
    [InlineData(FinanceAction.ViewVarianceReport, Roles.Admin, true)]
    [InlineData(FinanceAction.ViewVarianceReport, Roles.SuperAdmin, true)]
    [InlineData(FinanceAction.RecordExpense, Roles.Cashier, false)]
    [InlineData(FinanceAction.RecordExpense, Roles.Admin, true)]
    [InlineData(FinanceAction.RecordExpense, Roles.SuperAdmin, true)]
    public void Matritsa_malumoti_SPEC_43_jadvalining_aynan_ozi(
        FinanceAction action, string role, bool allowed) =>
        Assert.Equal(allowed, FinanceMatrix.IsAllowed(action, role));

    // =====================================================================
    //  Test ma'lumoti
    // =====================================================================

    private sealed record Actor(AppUser User, HttpClient Client, Guid ShiftId);

    private sealed record OtherCashier(string CashierId, Guid ShiftId, Guid PaymentId, decimal PaymentAmount);

    /// <summary>Smena xatosining javob shakli (`CashShiftsController.Failure`).</summary>
    private sealed record ShiftErrorDto(string Code, string Message);

    private async Task<Actor> ActorAsync(string role, params string[] permissions)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role, permissions: permissions);
        return new Actor(user, ClientFor(user), Guid.Empty);
    }

    /// <summary>Rol uchun foydalanuvchi + uning O'Z ochiq smenasi (HTTP orqali ochiladi).</summary>
    private async Task<Actor> ActorWithOpenShiftAsync(string role, decimal openingFloat = 0m)
    {
        var actor = await ActorAsync(role);

        var response = await actor.Client.PostAsJsonAsync(
            "/api/cash/shifts/open", new { openingFloat });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var shift = await response.Content.ReadFromJsonAsync<CashShiftDto>();
        Assert.NotNull(shift);
        return actor with { ShiftId = shift.Id };
    }

    /// <summary>
    /// BOSHQA kassir qabul qilgan to'lov — storno testlari uchun. Ikki
    /// qavatli nazorat (SPEC §4.5) o'z to'lovini storno qilishni taqiqlaydi,
    /// shuning uchun original AYNAN boshqa odamniki bo'lishi shart.
    /// </summary>
    private async Task<OtherCashier> PaymentByAnotherCashierAsync(decimal amount)
    {
        var cashier = await ActorWithOpenShiftAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();
        var invoiceId = await NewInvoiceAsync(studentId, TuitionCategoryId, amount);

        var response = await cashier.Client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId,
            amount,
            method = PaymentMethod.Cash,
            allocations = new[] { new { invoiceId, amount } },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payment = await response.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(payment);
        return new OtherCashier(cashier.User.Id, cashier.ShiftId, payment.Id, amount);
    }

    /// <summary>
    /// Boshqa kassirning YOPILGAN, nomuvofiqligi bor smenasi — 7-qator uchun.
    /// <paramref name="shortfall"/> — kassada kam chiqqan summa, ya'ni
    /// <c>variance = -shortfall</c>.
    /// </summary>
    private async Task<OtherCashier> ShiftClosedWithVarianceAsync(decimal shortfall)
    {
        var payment = await PaymentByAnotherCashierAsync(200_000m);

        // Smenani AYNAN o'sha kassir yopadi — chunki §4.3 ga ko'ra u buni qila oladi.
        await using var db = NewDb();
        var cashier = await db.Users.AsNoTracking().SingleAsync(u => u.Id == payment.CashierId);
        using var client = ClientFor(cashier);

        var response = await client.PostAsJsonAsync(
            $"/api/cash/shifts/{payment.ShiftId}/close",
            new { countedCash = 200_000m - shortfall, note = "Kam chiqdi" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var closed = await response.Content.ReadFromJsonAsync<CashShiftDto>();
        Assert.Equal(-shortfall, closed?.Variance);

        return payment;
    }

    private async Task<string> NewStudentAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var student = new Student { FullName = $"RBAC o'quvchi {suffix}", ClassName = "1-A" };

        await using var db = NewDb();
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return student.Id;
    }

    private async Task<Guid> NewInvoiceAsync(string studentId, Guid categoryId, decimal amount)
    {
        var invoice = new Invoice
        {
            StudentId = studentId,
            CategoryId = categoryId,
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
    //  DI bo'shlig'i (P1-15 gacha) — fayl boshidagi izohga qarang
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
                        services.AddScoped<ISubscriptionService, SubscriptionService>();
                        services.AddScoped<IDiscountService, DiscountService>();
                    }));
        }
    }

    private HttpClient NewClient() => Wired.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private HttpClient AnonymousClient() => NewClient();

    private HttpClient ClientWithToken(string token)
    {
        var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient ClientFor(AppUser user) =>
        ClientWithToken(fixture.Api.TokenFor(user.Role, user.Id, user.FullName, user.Email));
}
