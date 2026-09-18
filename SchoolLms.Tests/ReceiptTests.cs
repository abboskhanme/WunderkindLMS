using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// To'lov cheki: PDF va Telegramga yuborish (P1-12, SPEC §4.7).
///
/// <para>
/// Testlar ikki qatlamga bo'lingan. (1) <b>Bazasiz</b>: chek MAZMUNI va PDF
/// bayt oqimi — <c>ReceiptService.BuildModel</c> sof funksiya, <c>ReceiptDocument</c>
/// esa faqat modeldan chizadi, shuning uchun ularni Postgres'siz sinash mumkin va
/// shart. (2) <b>HTTP + baza</b>: marshrut, RBAC va Telegram yetkazish oqimi.
/// </para>
/// <para>
/// To'lov xizmati (P1-11) parallel yozilmoqda, shuning uchun bu yerda
/// <see cref="FakePaymentService"/> ishlatiladi — MUZLATILGAN
/// <see cref="IPaymentService"/> imzosiga tayangan holda. P1-11 tayyor bo'lgach
/// bu testlar o'zgarishsiz qoladi: ular chek kodini sinaydi, to'lov kodini emas.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ReceiptTests(ApiFixture fixture)
{
    // ===================================================================
    //  1-qatlam — chek mazmuni (bazasiz)
    // ===================================================================

    /// <summary>
    /// P1-12 qabul mezoni: <b>har toifa ALOHIDA qatorda</b>. Ota-ona pulining
    /// qaysi qismi o'qishga, qaysi qismi avtobusga ketganini ko'rishi shart.
    /// </summary>
    [Fact]
    public void Har_toifa_chekda_alohida_qatorda_chiqadi()
    {
        var payment = SamplePayment(allocations:
        [
            Allocation("tuition", "Maktab to'lovi", new DateOnly(2026, 9, 1), 1_000_000m),
            Allocation("bus", "Avtobus", new DateOnly(2026, 9, 1), 300_000m),
            Allocation("dormitory", "Yotoqxona", new DateOnly(2026, 9, 1), 700_000m),
        ], amount: 2_000_000m, unallocated: 0m);

        var model = ReceiptService.BuildModel(payment, School(), cancelledAt: null);

        Assert.Equal(3, model.Lines.Count);
        Assert.Contains(model.Lines, l => l.CategoryName == "Maktab to'lovi" && l.Amount == 1_000_000m);
        Assert.Contains(model.Lines, l => l.CategoryName == "Avtobus" && l.Amount == 300_000m);
        Assert.Contains(model.Lines, l => l.CategoryName == "Yotoqxona" && l.Amount == 700_000m);
    }

    /// <summary>
    /// Chekdagi qatorlar yig'indisi JAMIGA teng bo'lishi shart. Aks holda chekni
    /// o'qigan ota-ona farqni o'zi qidirib qolardi — va topa olmasdi.
    /// Avans (taqsimlanmagan qoldiq) shu sabab alohida qator bo'lib qo'shiladi.
    /// </summary>
    [Fact]
    public void Taqsimlanmagan_qoldiq_alohida_qator_bolib_qoshiladi_va_yigindi_jamiga_teng()
    {
        var payment = SamplePayment(allocations:
        [
            Allocation("tuition", "Maktab to'lovi", new DateOnly(2026, 9, 1), 1_000_000m),
        ], amount: 1_500_000m, unallocated: 500_000m);

        var model = ReceiptService.BuildModel(payment, School(), cancelledAt: null);

        Assert.Equal(2, model.Lines.Count);
        var advance = Assert.Single(model.Lines, l => l.CategoryName == ReceiptText.AdvanceLine);
        Assert.Equal(500_000m, advance.Amount);
        Assert.Null(advance.PeriodMonth);
        Assert.Equal(model.Total, model.Lines.Sum(l => l.Amount));
    }

    /// <summary>Storno qatorida taqsimot bo'lmaydi — qoldiq "avans" emas, qaytarilgan summa.</summary>
    [Fact]
    public void Storno_qatorining_chekida_qoldiq_qaytarilgan_summa_deb_yoziladi()
    {
        var payment = SamplePayment(allocations: [], amount: 400_000m, unallocated: 400_000m)
            with { ReversalOf = Guid.NewGuid() };

        var model = ReceiptService.BuildModel(payment, School(), cancelledAt: payment.ReceivedAt);

        var line = Assert.Single(model.Lines);
        Assert.Equal(ReceiptText.RefundLine, line.CategoryName);
        Assert.Equal(400_000m, line.Amount);
    }

    /// <summary>
    /// Chekda majburiy bo'lgan hamma narsa modelga tushdimi (P1-12): maktab nomi,
    /// chek raqami, sana-vaqt, o'quvchi, to'lov turi, jami, kassir.
    /// </summary>
    [Fact]
    public void Chek_modelida_majburiy_maydonlar_bor()
    {
        var payment = SamplePayment();

        var model = ReceiptService.BuildModel(payment, School(), cancelledAt: null);

        Assert.Equal("Wunderkind maktabi", model.SchoolName);
        Assert.Equal(payment.ReceiptNo, model.ReceiptNo);
        Assert.Equal(payment.ReceivedAt, model.ReceivedAt);
        Assert.Equal(payment.StudentName, model.StudentName);
        Assert.Equal(payment.Method, model.Method);
        Assert.Equal(payment.Amount, model.Total);
        Assert.Equal(payment.CashierName, model.CashierName);
        Assert.Null(model.CancelledAt);
    }

    /// <summary>SchoolMeta qatori yo'q bo'lsa ham chek chiqadi — nomsiz qolmaydi.</summary>
    [Fact]
    public void Maktab_malumoti_yoq_bolsa_chek_baribir_yasaladi()
    {
        var model = ReceiptService.BuildModel(SamplePayment(), school: null, cancelledAt: null);

        Assert.False(string.IsNullOrWhiteSpace(model.SchoolName));
        Assert.Null(model.SchoolAddress);
        Assert.Null(model.SchoolPhone);
    }

    /// <summary>
    /// Chekdagi matn — mijozga ko'rinadigan hujjat, shuning uchun formatlash
    /// tasodifga qoldirilmaydi: pul o'zbekcha ajratgich bilan, oy o'zbekcha nom
    /// bilan, to'lov turi kod emas, yorliq bilan chiqadi.
    /// </summary>
    [Fact]
    public void Chekdagi_matn_ozbekcha_formatlanadi()
    {
        Assert.Equal("1 250 000 so'm", ReceiptText.Money(1_250_000m));
        Assert.Equal("1 250 000,50 so'm", ReceiptText.Money(1_250_000.50m));
        Assert.Equal("0 so'm", ReceiptText.Money(0m));

        Assert.Equal("2026-yil sentyabr", ReceiptText.Period(new DateOnly(2026, 9, 1)));
        Assert.Equal("2026-yil yanvar", ReceiptText.Period(new DateOnly(2026, 1, 31)));
        Assert.Equal("—", ReceiptText.Period(null));

        Assert.Equal("Naqd pul", ReceiptText.MethodLabel(PaymentMethod.Cash));
        Assert.Equal("Bank kartasi (terminal)", ReceiptText.MethodLabel(PaymentMethod.Card));
        Assert.Equal("Bank o'tkazmasi", ReceiptText.MethodLabel(PaymentMethod.Transfer));
        Assert.Equal("Onlayn to'lov (Payme/Click/Uzum)", ReceiptText.MethodLabel(PaymentMethod.Online));
        // Noma'lum usul: bo'sh joy emas, xom qiymat chiqsin.
        Assert.Equal("paynet", ReceiptText.MethodLabel("paynet"));
    }

    /// <summary>
    /// Vaqt Toshkent mintaqasida ko'rsatiladi (SPEC §7): bazada UTC lahza,
    /// chekda esa ota-ona ko'rgan soat.
    /// </summary>
    [Fact]
    public void Chekdagi_vaqt_toshkent_mintaqasida()
    {
        // 2026-09-11 09:35 UTC = Toshkentda 14:35 (UTC+5).
        var instant = new DateTimeOffset(2026, 9, 11, 9, 35, 0, TimeSpan.Zero);

        Assert.Equal("11.09.2026 14:35", ReceiptText.DateTime(instant));
        Assert.Equal("11.09.2026", ReceiptText.Date(instant));
    }

    // ===================================================================
    //  2-qatlam — PDF bayt oqimi (bazasiz)
    // ===================================================================

    /// <summary>
    /// P1-12 qabul mezoni: chindan ham PDF chiqadimi. Uch tekshiruv —
    /// sarlavha (<c>%PDF</c>), fayl tugallanganmi (<c>%%EOF</c>) va hajmi
    /// nol emasmi.
    ///
    /// <para>
    /// Chek RAQAMI ham fayl ichidan qidiriladi. PDF matni siqilgan oqimda va
    /// shrift glif kodlarida yozilgani uchun uni oddiy qidiruv bilan topib
    /// bo'lmaydi — shu sabab raqam PDF metama'lumotidagi sarlavhaga ham
    /// yoziladi, u esa faylga ochiq ASCII satr bo'lib tushadi. Ya'ni chekni
    /// ochmasdan ham "bu qaysi chek" degan savolga javob bor.
    /// </para>
    /// </summary>
    [Fact]
    public void Chek_pdf_bayt_oqimi_haqiqiy_va_bosh_emas()
    {
        var model = ReceiptService.BuildModel(SamplePayment(receiptNo: 1042), School(), cancelledAt: null);

        var pdf = new ReceiptDocument(model).Render();

        Assert.NotEmpty(pdf);
        Assert.True(pdf.Length > 1000, $"PDF juda kichik: {pdf.Length} bayt");

        var raw = Encoding.ASCII.GetString(pdf);
        Assert.StartsWith("%PDF", raw, StringComparison.Ordinal);
        Assert.Contains("%%EOF", raw, StringComparison.Ordinal);
        Assert.Contains(ReceiptText.MetadataTitle(1042), raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// P1-12 qabul mezoni: storno qilingan to'lov cheki "BEKOR QILINGAN"
    /// shtampi va bekor qilingan sanasi bilan chiqadi.
    ///
    /// <para>
    /// Shtamp chekda KO'RINADIGAN qizil banner sifatida chiziladi; bu yerda esa
    /// u PDF metama'lumoti orqali tekshiriladi (ko'rinadigan matn siqilgan
    /// oqimda). Qo'shimcha dalil: storno cheki oddiy chekdan FARQ QILADI va
    /// undan kattaroq.
    /// </para>
    /// </summary>
    [Fact]
    public void Storno_chekida_bekor_qilingan_shtampi_va_sanasi_bor()
    {
        var payment = SamplePayment(receiptNo: 77);
        var cancelledAt = new DateTimeOffset(2026, 9, 12, 5, 20, 0, TimeSpan.Zero);

        var normal = new ReceiptDocument(
            ReceiptService.BuildModel(payment, School(), cancelledAt: null)).Render();
        var cancelled = new ReceiptDocument(
            ReceiptService.BuildModel(payment, School(), cancelledAt)).Render();

        var cancelledRaw = Encoding.ASCII.GetString(cancelled);
        Assert.Contains(ReceiptText.CancelledStamp, cancelledRaw, StringComparison.Ordinal);
        Assert.Contains("12.09.2026", cancelledRaw, StringComparison.Ordinal);

        // Oddiy chekda shtamp BO'LMASLIGI ham xuddi shunday muhim.
        Assert.DoesNotContain(ReceiptText.CancelledStamp,
            Encoding.ASCII.GetString(normal), StringComparison.Ordinal);
        Assert.True(cancelled.Length > normal.Length,
            "Storno cheki oddiy chekdan katta bo'lishi kerak (shtamp qo'shiladi).");
    }

    /// <summary>Uzun F.I.SH va ko'p toifali chek ham yiqilmasin (real ma'lumot har xil bo'ladi).</summary>
    [Fact]
    public void Uzun_ism_va_kop_toifali_chek_yasaladi()
    {
        var allocations = Enumerable.Range(1, 12)
            .Select(i => Allocation($"cat{i}", $"Toifa {i} — uzunroq nom bilan", new DateOnly(2026, i, 1), 100_000m))
            .ToList();
        var payment = SamplePayment(allocations: allocations, amount: 1_200_000m, unallocated: 0m)
            with { StudentName = "Abdurahmonov Abdusamadjon Abdulhakim o'g'li" };

        var pdf = new ReceiptDocument(
            ReceiptService.BuildModel(payment, School(), cancelledAt: null)).Render();

        Assert.True(pdf.Length > 1000);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 8), StringComparison.Ordinal);
    }

    // ===================================================================
    //  3-qatlam — HTTP: marshrut va RBAC
    // ===================================================================

    /// <summary>Chek — moliyaviy hujjat: token'siz umuman berilmaydi.</summary>
    [Fact]
    public async Task Token_siz_chek_pdf_i_berilmaydi()
    {
        using var client = fixture.Api.AnonymousClient();

        var response = await client.GetAsync($"/api/receipts/{Guid.NewGuid()}.pdf");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// SPEC §4.3: chek berish — kassir, admin, direktor ishi. O'qituvchi
    /// moliya jadvalida umuman yo'q, demak 403.
    /// </summary>
    [Fact]
    public async Task Oqituvchi_chek_pdf_ini_ola_olmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Teacher);

        var response = await client.GetAsync($"/api/receipts/{Guid.NewGuid()}.pdf");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Telegramga yuborish ham xuddi shu qatorga bog'langan.</summary>
    [Fact]
    public async Task Oqituvchi_chekni_telegramga_yubora_olmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Teacher);

        var response = await client.PostAsync($"/api/receipts/{Guid.NewGuid()}/telegram", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Kassir uchun endpoint HAQIQATAN PDF qaytaradi.
    ///
    /// <para>
    /// Bu test marshrutni ham tekshiradi: <c>{paymentId:guid}.pdf</c> — oxirida
    /// nuqta va kengaytma turgan shablon, uni noto'g'ri yozish 404 beradi va
    /// buni faqat haqiqiy so'rov ko'rsatadi.
    /// </para>
    /// <para>
    /// DI shu test uchun to'ldiriladi: <c>IReceiptService</c> ni <c>Program.cs</c>
    /// da P1-15 ro'yxatdan o'tkazadi (docs/PENDING_WIRING.md), <c>IPaymentService</c>
    /// esa P1-11 da yoziladi. Bu yerda ikkalasi ham shu test uchun qo'yiladi —
    /// ular ulangach test o'zgarishsiz ishlashda davom etadi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Kassir_chek_pdf_ini_ola_oladi()
    {
        var paymentId = Guid.NewGuid();
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        // F0.04: chek FAQAT shu kassirning O'ZI qabul qilgan to'lovi bo'lsa
        // ko'rinadi — shuning uchun payment.CashierId aynan shu foydalanuvchi.
        var payment = SamplePayment(receiptNo: 501) with { Id = paymentId, CashierId = user.Id };

        await using var factory = WithReceiptServices(new FakePaymentService(payment));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.Api.TokenFor(Roles.Cashier, user.Id, user.FullName, user.Email));

        var response = await client.GetAsync($"/api/receipts/{paymentId}.pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var pdf = await response.Content.ReadAsByteArrayAsync();
        Assert.True(pdf.Length > 1000);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 8), StringComparison.Ordinal);
        Assert.Contains(ReceiptText.MetadataTitle(501),
            Encoding.ASCII.GetString(pdf), StringComparison.Ordinal);
    }

    /// <summary>Mavjud bo'lmagan to'lov — 404, 500 emas.</summary>
    [Fact]
    public async Task Yoq_tolovning_cheki_404()
    {
        await using var factory = WithReceiptServices(new FakePaymentService());
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.Api.TokenFor(Roles.Cashier, user.Id, user.FullName, user.Email));

        var response = await client.GetAsync($"/api/receipts/{Guid.NewGuid()}.pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// finance-parity.md F0.04 — kassir BOSHQA kassirning chekini ocha
    /// olmaydi: to'lov mavjud bo'lsa ham, unga tegishli emasligi sababli
    /// javob 404 (SPEC §4.3, "variance across cashiers" bilan bir xil
    /// mulohaza — chekning borligi ham ma'lumot, shuning uchun 403 emas).
    /// </summary>
    [Fact]
    public async Task Kassir_boshqa_kassirning_chek_pdf_ini_ola_olmaydi()
    {
        var paymentId = Guid.NewGuid();
        var payment = SamplePayment(receiptNo: 502) with { Id = paymentId, CashierId = "boshqa-kassir" };

        await using var factory = WithReceiptServices(new FakePaymentService(payment));
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.Api.TokenFor(Roles.Cashier, user.Id, user.FullName, user.Email));

        var response = await client.GetAsync($"/api/receipts/{paymentId}.pdf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// finance-parity.md F0.04 — admin va direktor CHEKLANMAGAN: har qanday
    /// kassirning chekini ko'ra oladi (SPEC §4.3, moliya hisobotlari qatori).
    /// </summary>
    [Fact]
    public async Task Admin_istalgan_kassirning_chek_pdf_ini_ola_oladi()
    {
        var paymentId = Guid.NewGuid();
        var payment = SamplePayment(receiptNo: 503) with { Id = paymentId, CashierId = "boshqa-kassir" };

        await using var factory = WithReceiptServices(new FakePaymentService(payment));
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.Api.TokenFor(Roles.Admin, user.Id, user.FullName, user.Email));

        var response = await client.GetAsync($"/api/receipts/{paymentId}.pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ===================================================================
    //  4-qatlam — Telegramga yetkazish
    // ===================================================================

    /// <summary>
    /// P1-12 qabul mezoni: <b>Telegrami ulanmagan ota-ona — ogohlantirish,
    /// xato emas.</b> Xizmat `false` qaytaradi va HECH QANDAY istisno tashlamaydi.
    /// </summary>
    [Fact]
    public async Task Telegrami_yoq_ota_ona_xato_bermaydi()
    {
        var payment = SamplePayment() with { StudentId = "yoq-" + Guid.NewGuid().ToString("N") };

        await using var db = NewDb();
        var delivered = await NewService(db, new FakePaymentService(payment))
            .SendToGuardianAsync(payment.Id);

        Assert.False(delivered);
    }

    /// <summary>
    /// ENG MUHIM TEST SHU FAYLDA. Telegram yetkazib bera olmasa ham
    /// <b>to'lov hech qachon orqaga qaytmaydi</b>: xizmat istisno tashlamaydi,
    /// hech narsani o'chirmaydi, faqat `false` qaytaradi va jurnalga yozadi.
    ///
    /// <para>
    /// Bu yerda bot sozlanmagan (testda token bo'sh) — ota-ona ro'yxatdan
    /// o'tgan bo'lsa ham yuborib bo'lmaydi. Pul qabul qilingan, chek
    /// yetkazilmagan: bu ikki xil hodisa va ularni aralashtirish mumkin emas.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Telegram_yubora_olmasa_ham_tolov_orqaga_qaytmaydi()
    {
        var payment = SamplePayment() with { StudentId = await NewRegisteredStudentAsync() };

        await using var db = NewDb();
        var service = NewService(db, new FakePaymentService(payment));

        // Istisno tashlamasligi — assert'ning O'ZI: tashlasa test yiqiladi va
        // aynan shu narsa P1-11 dagi to'lov tranzaksiyasini orqaga qaytarardi.
        var delivered = await service.SendToGuardianAsync(payment.Id);

        Assert.False(delivered);
    }

    /// <summary>
    /// P1-12 qabul mezoni: yuborish muvaffaqiyatsiz bo'lsa <b>BIR MARTA</b>
    /// qayta uriniladi — kam emas (bir martalik tarmoq uzilishi chekni yo'qotib
    /// yuborardi), ko'p emas (kassir so'rov tugashini kutib turadi).
    ///
    /// <para>
    /// Telegram javob bermayotgani soxta HTTP ishlov beruvchi bilan taqlid
    /// qilinadi: u har so'rovga 500 qaytaradi va chaqiruvlarni sanaydi.
    /// Kutilgan natija — AYNAN 2 ta chaqiruv (asosiy + bitta qayta urinish).
    /// </para>
    /// </summary>
    [Fact]
    public async Task Telegram_javob_bermasa_aynan_bir_marta_qayta_uriniladi()
    {
        var payment = SamplePayment() with { StudentId = await NewRegisteredStudentAsync() };
        var handler = new AlwaysFailingHandler();

        await using var db = NewDb();
        var service = NewService(db, new FakePaymentService(payment), FailingTelegram(handler));

        var delivered = await service.SendToGuardianAsync(payment.Id);

        Assert.False(delivered);
        Assert.Equal(2, handler.Calls);
    }

    /// <summary>Mavjud bo'lmagan to'lovda ham yuborish istisno tashlamaydi (P1-11 oqimini buzmaydi).</summary>
    [Fact]
    public async Task Yoq_tolovni_yuborish_istisno_tashlamaydi()
    {
        await using var db = NewDb();

        var delivered = await NewService(db, new FakePaymentService())
            .SendToGuardianAsync(Guid.NewGuid());

        Assert.False(delivered);
    }

    // ===================================================================
    //  Yordamchilar
    // ===================================================================

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    /// <summary>
    /// Chek xizmati — haqiqiy baza bilan. <paramref name="telegram"/> berilmasa,
    /// ilovaning o'z <see cref="TelegramService"/> nusxasi ishlatiladi; testda
    /// uning tokeni bo'sh (ApiFactory shunday qo'yadi), ya'ni tarmoqqa hech
    /// qanday so'rov ketmaydi.
    /// </summary>
    private ReceiptService NewService(
        AppDbContext db, IPaymentService payments, TelegramService? telegram = null) =>
        new(payments,
            db,
            telegram ?? fixture.Api.Services.GetRequiredService<TelegramService>(),
            fixture.Api.Services.GetRequiredService<ILogger<ReceiptService>>());

    /// <summary>
    /// Tokeni QO'YILGAN (ya'ni "sozlangan") TelegramService — lekin barcha HTTP
    /// so'rovlari <paramref name="handler"/> ga tushadi, ya'ni tarmoqqa chiqmaydi.
    /// </summary>
    private TelegramService FailingTelegram(AlwaysFailingHandler handler)
    {
        var service = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(),
            new SingleHandlerHttpClientFactory(handler),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        service.Set("test-token", "test_bot");
        return service;
    }

    /// <summary>Telegramda ro'yxatdan o'tgan ota-onasi bor yangi o'quvchi id'si.</summary>
    private async Task<string> NewRegisteredStudentAsync()
    {
        var studentId = "stu-" + Guid.NewGuid().ToString("N")[..8];

        await fixture.Api.WithDbAsync(async db =>
        {
            db.TelegramRegistrations.Add(new TelegramRegistration
            {
                StudentId = studentId,
                ChatId = Random.Shared.NextInt64(100_000, 999_999_999),
                ParentName = "Test ota-ona",
                Phone = "998900000000",
            });
            await db.SaveChangesAsync();
        });

        return studentId;
    }

    /// <summary>
    /// Ilovani chek xizmatlari ulangan holda ko'taradi. P1-15 aynan shu ikki
    /// qatorni <c>Program.cs</c> ga qo'shadi (docs/PENDING_WIRING.md).
    /// </summary>
    private WebApplicationFactory<SchoolLms.Server.Controllers.AuthController> WithReceiptServices(
        IPaymentService payments) =>
        fixture.Api.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(payments);
            services.AddScoped<IReceiptService, ReceiptService>();
        }));

    private static SchoolMeta School() => new()
    {
        Name = "Wunderkind maktabi",
        Address = "Toshkent sh., Chilonzor t.",
        Phone = "+998 90 000 00 00",
    };

    private static PaymentAllocationDto Allocation(
        string code, string name, DateOnly period, decimal amount) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), code, name, period, amount);

    private static PaymentDto SamplePayment(
        long receiptNo = 1,
        List<PaymentAllocationDto>? allocations = null,
        decimal amount = 1_000_000m,
        decimal unallocated = 0m)
    {
        allocations ??=
        [
            Allocation("tuition", "Maktab to'lovi", new DateOnly(2026, 9, 1), amount - unallocated),
        ];

        return new PaymentDto(
            Id: Guid.NewGuid(),
            ReceiptNo: receiptNo,
            StudentId: "stu-1",
            StudentName: "Abdullayev Jasur G'ayrat o'g'li",
            Amount: amount,
            Method: PaymentMethod.Cash,
            CashShiftId: Guid.NewGuid(),
            CashierId: "usr-1",
            CashierName: "Karimova Dilnoza",
            Note: null,
            ReceivedAt: new DateTimeOffset(2026, 9, 11, 9, 35, 0, TimeSpan.Zero),
            ReversalOf: null,
            ReversedBy: null,
            Unallocated: unallocated,
            Allocations: allocations);
    }

    /// <summary>Har so'rovga 500 qaytaradi va chaqiruvlar sonini sanaydi.</summary>
    private sealed class AlwaysFailingHandler : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    /// <summary>Har doim bitta (soxta) ishlov beruvchiga ulangan klient beradi.</summary>
    private sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>
    /// P1-11 o'rniga: faqat <see cref="GetAsync"/> ishlaydi, qolgan metodlar
    /// ATAYLAB yiqiladi — chek xizmati ulardan birortasini chaqirsa, bu test
    /// darhol ko'rsatadi (chek hech qachon pul yozmaydi).
    /// </summary>
    private sealed class FakePaymentService(params PaymentDto[] payments) : IPaymentService
    {
        public Task<PaymentDto?> GetAsync(Guid paymentId, CancellationToken ct = default) =>
            Task.FromResult(payments.FirstOrDefault(p => p.Id == paymentId));

        public Task<PaymentDto> AcceptAsync(
            AcceptPaymentRequest request, string cashierId, CancellationToken ct = default) =>
            throw new NotSupportedException("Chek xizmati to'lov qabul qilmaydi.");

        public Task<PaymentDto> ReverseAsync(
            Guid paymentId, string reason, string approverId, Guid? cashBoxId = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException("Chek xizmati storno qilmaydi.");

        public Task<IReadOnlyList<PaymentDto>> ListAsync(PaymentQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException("Chek xizmati to'lovlar ro'yxatini o'qimaydi.");

        public Task<IReadOnlyList<AllocationSuggestionDto>> SuggestAllocationAsync(
            string studentId, decimal amount, CancellationToken ct = default) =>
            throw new NotSupportedException("Chek xizmati taqsimot taklif qilmaydi.");
    }
}
