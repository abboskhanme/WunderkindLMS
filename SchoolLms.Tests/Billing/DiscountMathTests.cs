using Microsoft.AspNetCore.Http;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// Chegirma arifmetikasi (P1-23) — <b>ko'chirish xulqni saqlaganini ISBOTLAYDI</b>.
///
/// <para>
/// Bugun bitta formula UCH JOYDA yashaydi:
/// </para>
/// <list type="number">
///   <item><c>TuitionService.ChargeFor</c> — eski (legacy) kod, hozir ishlab
///   turgan hisob-kitobning etaloni;</item>
///   <item><c>DiscountMath.ChargeFor</c> — <c>InvoiceService</c> (P1-09)
///   accrual paytida AYNAN shuni chaqiradi;</item>
///   <item><c>DiscountService.ChargeFor</c> — P1-08 xizmatining O'Z nusxasi.</item>
/// </list>
///
/// <para>
/// <b>Nega uchalasi ham har testda tekshiriladi.</b> Mavjud
/// <c>BillingCatalogTests.ChargeFor_eski_TuitionService_bilan_bir_xil</c> faqat
/// "eski == yangi" ni solishtiradi. Ikkala tomon BIR XIL xato qilsa, u test
/// yashil qolaveradi va <c>DiscountMath</c> ni umuman ko'rmaydi. Shu sababli bu
/// yerdagi jadval har satrda KUTILGAN RAQAMNI ham qotirib qo'yadi: birorta
/// implementatsiya siljisa — aniq qaysi biri ekani ko'rinadi.
/// </para>
///
/// <para>
/// Kutilgan qiymatlar o'ylab topilmagan, <c>TuitionService.ChargeFor</c> ning
/// qoidasidan chiqarilgan: <c>fee ≤ 0 → 0</c>; foiz <c>[0..100]</c> ga qisiladi;
/// summa <c>[0..∞)</c> ga qisiladi; AVVAL foiz, KEYIN summa; quyi chegara 0;
/// oxirida <c>decimal.Round(x, 2)</c> (ya'ni o'rta nuqta JUFTGA yaxlitlanadi).
/// </para>
///
/// <para>
/// Baza OWNER ulanishi bilan ochiladi (<c>BillingCatalogTests</c> dagi sabab):
/// <c>DiscountService</c> konstruktorida <c>IAppDbContext</c> bor. Bu testlar
/// bazaga bitta ham so'rov yubormaydi — kontekst faqat xizmatni qurish uchun.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class DiscountMathTests(ApiFixture fixture)
{
    /// <summary>
    /// Tasodifiy solishtiruv uchun URUG'. QOTIRILGAN: yiqilgan test qayta
    /// yurganda AYNAN o'sha kirishlarni beradi. Xato xabarida ham chiqadi.
    /// </summary>
    private const int Seed = 20260912;

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    private DiscountService Discounts(AppDbContext db) =>
        new(db, new AuditService(db, new HttpContextAccessor()));

    // =================================================================
    //  1. Jadval: kirish → KUTILGAN raqam (uchala implementatsiya uchun)
    // =================================================================

    /// <param name="fee">Oylik narx (obuna summasi).</param>
    /// <param name="percent">Chegirma foizi (eski kodda <c>int</c>).</param>
    /// <param name="amount">Foizdan KEYIN ayriladigan aniq summa.</param>
    /// <param name="expectedCharge">To'lanishi kerak bo'lgan summa.</param>
    /// <param name="expectedDiscount">Chegirma summasi (<c>fee − charge</c>).</param>
    public static TheoryData<decimal, int, decimal, decimal, decimal> Jadval => new()
    {
        // ---- oddiy holatlar ----
        { 1_000_000m, 0, 0m, 1_000_000m, 0m },
        { 1_000_000m, 20, 0m, 800_000m, 200_000m },
        { 1_000_000m, 0, 50_000m, 950_000m, 50_000m },
        { 850_000m, 15, 0m, 722_500m, 127_500m },

        // ---- TARTIB: avval foiz, keyin summa ----
        // 1 000 000 − 20% = 800 000; 800 000 − 50 000 = 750 000.
        // (Teskari tartibda (1 000 000 − 50 000) × 0.8 = 760 000 chiqardi.)
        { 1_000_000m, 20, 50_000m, 750_000m, 250_000m },
        { 1_234_567m, 7, 1_000_000m, 148_147.31m, 1_086_419.69m },

        // ---- 100% → 0 ----
        { 700_000m, 100, 0m, 0m, 700_000m },
        { 1_000_000m, 100, 250_000m, 0m, 1_000_000m },

        // ---- 100% dan oshsa ham MANFIY emas ----
        { 1_000_000m, 90, 500_000m, 0m, 1_000_000m },
        { 500_000m, 50, 900_000m, 0m, 500_000m },

        // ---- chegaradan tashqari kirishlar qisiladi ----
        { 1_000_000m, 150, 0m, 0m, 1_000_000m },        // foiz > 100 → 100
        { 1_000_000m, -20, 0m, 1_000_000m, 0m },        // foiz < 0 → 0
        { 1_000_000m, 0, -50_000m, 1_000_000m, 0m },    // summa < 0 → 0

        // ---- narx 0 yoki manfiy ----
        { 0m, 50, 10_000m, 0m, 0m },
        { -1_000m, 50, 0m, 0m, 0m },

        // ---- 2 kasrga yaxlitlash ----
        // 999.99 × 67% = 669.9933 → 669.99
        { 999.99m, 33, 0m, 669.99m, 330.00m },
        // O'RTA NUQTA: 333.33 × 50% = 166.665 → JUFTGA (166.66), yuqoriga emas.
        { 333.33m, 50, 0m, 166.66m, 166.67m },
        // O'RTA NUQTA: 333.35 × 50% = 166.675 → JUFTGA (166.68).
        { 333.35m, 50, 0m, 166.68m, 166.67m },
    };

    [Theory]
    [MemberData(nameof(Jadval))]
    public async Task Uchala_implementatsiya_kutilgan_raqamni_beradi(
        decimal fee, int percent, decimal amount, decimal expectedCharge, decimal expectedDiscount)
    {
        await using var db = NewDb();
        var service = Discounts(db);

        Assert.Equal(expectedCharge, TuitionService.ChargeFor(fee, percent, amount));
        Assert.Equal(expectedCharge, DiscountMath.ChargeFor(fee, percent, amount));
        Assert.Equal(expectedCharge, service.ChargeFor(fee, percent, amount));

        Assert.Equal(expectedDiscount, TuitionService.DiscountFor(fee, percent, amount));
        Assert.Equal(expectedDiscount, DiscountMath.DiscountFor(fee, percent, amount));
        Assert.Equal(expectedDiscount, service.DiscountFor(fee, percent, amount));

        // Chegirma hech qachon narxdan oshmaydi (`ck_invoices_discount`) va
        // to'lanadigan summa hech qachon manfiy bo'lmaydi.
        Assert.InRange(expectedCharge, 0m, Math.Max(0m, fee));
        Assert.InRange(expectedDiscount, 0m, Math.Max(0m, fee));
    }

    // =================================================================
    //  2. Tartib — alohida qotiriladi
    // =================================================================

    /// <summary>
    /// "Avval foiz, keyin summa" — bu SHUNCHAKI izoh emas, pul farqi. Test
    /// to'g'ri raqamni tasdiqlaydi VA teskari tartibning raqamini rad etadi,
    /// aks holda tartib almashtirilsa ham jadval satri o'tib ketishi mumkin
    /// bo'lgan holatlar qolardi.
    /// </summary>
    [Fact]
    public async Task Avval_foiz_keyin_summa_tartibi_qotirilgan()
    {
        await using var db = NewDb();
        var service = Discounts(db);

        const decimal fee = 1_000_000m;
        const int percent = 20;
        const decimal amount = 50_000m;

        // Teskari tartib: (1 000 000 − 50 000) × 0.8 = 760 000.
        var teskari = decimal.Round((fee - amount) * (100m - percent) / 100m, 2);
        Assert.Equal(760_000m, teskari);

        foreach (var actual in new[]
                 {
                     TuitionService.ChargeFor(fee, percent, amount),
                     DiscountMath.ChargeFor(fee, percent, amount),
                     service.ChargeFor(fee, percent, amount),
                 })
        {
            Assert.Equal(750_000m, actual);
            Assert.NotEqual(teskari, actual);
        }
    }

    // =================================================================
    //  3. Kasrli foiz — eski `int` API ifodalay olmaydigan holat
    // =================================================================

    /// <summary>
    /// <c>discounts.percent</c> ustuni <c>numeric(5,2)</c>, ya'ni 12.5% yozish
    /// mumkin. Eski <c>TuitionService</c> ni bu yerda solishtirib bo'lmaydi
    /// (uning foizi <c>int</c>), shuning uchun IKKI YANGI nusxa bir-biri bilan
    /// va qotirilgan raqam bilan solishtiriladi — ular ajralib ketmasin.
    /// </summary>
    public static TheoryData<decimal, decimal, decimal, decimal, decimal> KasrliJadval => new()
    {
        // fee, foiz, summa, kutilgan to'lov, kutilgan chegirma
        { 1_000_000m, 12.5m, 0m, 875_000m, 125_000m },
        { 1_000_000m, 0.01m, 0m, 999_900m, 100m },
        { 1_000_000m, 99.99m, 0m, 100m, 999_900m },
        { 1_000_000m, 33.33m, 100_000m, 566_700m, 433_300m },
    };

    [Theory]
    [MemberData(nameof(KasrliJadval))]
    public async Task Kasrli_foiz_ikkala_yangi_nusxada_bir_xil(
        decimal fee, decimal percent, decimal amount, decimal expectedCharge, decimal expectedDiscount)
    {
        await using var db = NewDb();
        var service = Discounts(db);

        Assert.Equal(expectedCharge, DiscountMath.ChargeFor(fee, percent, amount));
        Assert.Equal(expectedCharge, service.ChargeFor(fee, percent, amount));
        Assert.Equal(expectedDiscount, DiscountMath.DiscountFor(fee, percent, amount));
        Assert.Equal(expectedDiscount, service.DiscountFor(fee, percent, amount));
    }

    // =================================================================
    //  4. Tasodifiy differensial solishtiruv
    // =================================================================

    /// <summary>
    /// Jadval faqat o'ylangan holatlarni ushlaydi. Bu test 5 000 ta tasodifiy
    /// kirishda uchala implementatsiyani solishtiradi va har birida
    /// invariantlarni tekshiradi:
    /// <list type="bullet">
    ///   <item>0 ≤ to'lov ≤ narx;</item>
    ///   <item>0 ≤ chegirma ≤ narx;</item>
    ///   <item>to'lov + chegirma = narx (narx 2 kasrli bo'lgani uchun aniq tenglik).</item>
    /// </list>
    /// URUG' qotirilgan (<see cref="Seed"/>) — yiqilish qayta ishlab chiqariladi
    /// va xato xabarida urug' bilan kirish qiymatlari ko'rinadi.
    /// </summary>
    [Fact]
    public async Task Tasodifiy_kirishlarda_uchala_nusxa_va_invariantlar_mos()
    {
        await using var db = NewDb();
        var service = Discounts(db);

        var rnd = new Random(Seed);

        for (var i = 0; i < 5_000; i++)
        {
            // 2 kasrli narx: 0.00 … 30 000 000.00 so'm. Har 20-chisi 0 yoki manfiy.
            var fee = i % 20 == 0
                ? (i % 40 == 0 ? 0m : -new decimal(rnd.Next(1, 100_000)) / 100m)
                : new decimal(rnd.Next(0, 3_000_000_00)) / 100m;

            // Foiz ATAYLAB chegaradan chiqadi: -20 … 140.
            var percent = rnd.Next(-20, 141);
            // Summa ham manfiy bo'lishi mumkin.
            var amount = new decimal(rnd.Next(-500_000_00, 2_000_000_00)) / 100m;

            var where = $"urug'={Seed}, qadam={i}: fee={fee}, percent={percent}, amount={amount}";

            var legacy = TuitionService.ChargeFor(fee, percent, amount);
            Assert.True(legacy == DiscountMath.ChargeFor(fee, percent, amount),
                $"DiscountMath.ChargeFor eski koddan farq qildi ({where}): "
                + $"{DiscountMath.ChargeFor(fee, percent, amount)} != {legacy}");
            Assert.True(legacy == service.ChargeFor(fee, percent, amount),
                $"DiscountService.ChargeFor eski koddan farq qildi ({where}): "
                + $"{service.ChargeFor(fee, percent, amount)} != {legacy}");

            var legacyDiscount = TuitionService.DiscountFor(fee, percent, amount);
            Assert.True(legacyDiscount == DiscountMath.DiscountFor(fee, percent, amount),
                $"DiscountMath.DiscountFor eski koddan farq qildi ({where}).");
            Assert.True(legacyDiscount == service.DiscountFor(fee, percent, amount),
                $"DiscountService.DiscountFor eski koddan farq qildi ({where}).");

            var cap = Math.Max(0m, fee);
            Assert.True(legacy >= 0m && legacy <= cap,
                $"To'lov chegaradan chiqdi: {legacy} ∉ [0, {cap}] ({where}).");
            Assert.True(legacyDiscount >= 0m && legacyDiscount <= cap,
                $"Chegirma chegaradan chiqdi: {legacyDiscount} ∉ [0, {cap}] ({where}).");
            Assert.True(legacy + legacyDiscount == cap,
                $"To'lov + chegirma narxga teng emas: {legacy} + {legacyDiscount} != {cap} ({where}).");
        }
    }
}
