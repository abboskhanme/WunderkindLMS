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
/// Kassa smenasi butunligi — P1-24, SPEC §4.2, §4.3, §4.6.
///
/// <para>
/// <b>Bu fayl HTTP orqali boradi.</b> <c>CashShiftServiceTests</c> (P1-10)
/// xizmat qatlamini sinaydi; bu yerda tekshiriladigan narsalar esa faqat
/// so'rov yo'lida ko'rinadi: rol darvozasi, egalik chegarasi, xato tanasidagi
/// <c>code</c>, va — eng muhimi — <b>rad etilgandan keyin bazada nima
/// qolgani</b>. "403 qaytdi" degan tasdiqning o'zi yetarli emas: agar
/// endpoint 403 qaytarib, smenani baribir yopib qo'ysa, faqat status kodini
/// tekshiradigan test buni ko'rmaydi.
/// </para>
///
/// <para>
/// To'lovlar HAQIQIY <see cref="PaymentService"/> orqali qabul qilinadi
/// (qo'lda yozilgan qatorlar bilan emas), ya'ni Z-hisobot AYNAN ilova
/// yozadigan ma'lumot ustida hisoblanadi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class CashShiftTests(ApiFixture fixture)
{
    /// <summary>Xato javobining shakli: <c>{ "code": "...", "message": "..." }</c>.</summary>
    private sealed record ErrorBody(string Code, string Message);

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  1) Ikkinchi ochiq smena → 409
    // =====================================================================

    /// <summary>
    /// SPEC §4.2: bitta kassirda bir vaqtda BITTA ochiq smena.
    ///
    /// <para>
    /// Status kodidan tashqari ikki narsa tekshiriladi: xato tanasidagi
    /// mashina kodi (<c>shift_already_open</c> — frontend shunga qarab
    /// xabar chiqaradi) va BIRINCHI smenaning tegilmagani. Ikkinchi ochish
    /// urinishi birinchisining <c>opening_float</c> ini o'zgartirib qo'ysa
    /// yoki uni yangisi bilan almashtirsa, faqat status kodini tekshiradigan
    /// test buni sezmasdi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Kassirda_ikkinchi_ochiq_smena_409_va_birinchisi_tegilmaydi()
    {
        var (cashier, client) = await ClientAsync(Roles.Cashier);

        var first = await client.PostAsJsonAsync("/api/cash/shifts/open", new { openingFloat = 150_000m });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var opened = (await first.Content.ReadFromJsonAsync<CashShiftDto>())!;
        Assert.Equal(CashShiftStatus.Open, opened.Status);
        Assert.Equal(150_000m, opened.OpeningFloat);
        Assert.Equal(cashier.Id, opened.CashierId);
        Assert.Null(opened.Variance);

        var second = await client.PostAsJsonAsync("/api/cash/shifts/open", new { openingFloat = 999_000m });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = (await second.Content.ReadFromJsonAsync<ErrorBody>())!;
        Assert.Equal(CashShiftError.AlreadyOpen, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));

        // Bazada — AYNAN bitta ochiq smena, va u BIRINCHISI.
        await using var db = NewDb();
        var open = await db.CashShifts.AsNoTracking()
            .Where(s => s.CashierId == cashier.Id && s.Status == CashShiftStatus.Open)
            .ToListAsync();

        var only = Assert.Single(open);
        Assert.Equal(opened.Id, only.Id);
        Assert.Equal(150_000m, only.OpeningFloat);   // 999 000 yozilib ketmadi
    }

    /// <summary>
    /// Ikki so'rov AYNAN BIR VAQTDA kelsa ham ikkinchi smena ochilmaydi.
    ///
    /// <para>
    /// Xizmatdagi "ochiq smena bormi" tekshiruvi bu holatni ushlay olmaydi:
    /// ikkala so'rov ham "yo'q" deb ko'radi. Yagona kafolat — bazadagi qisman
    /// unikal indeks <c>ux_cash_shifts_one_open_per_cashier</c>. Ketma-ket
    /// yuborilgan ikki so'rov bu qatlamga umuman yetib bormaydi, shuning uchun
    /// indeks tushib qolsa yuqoridagi test yashil qolaverardi — bu esa qizil
    /// bo'ladi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Ikki_parallel_open_sorovidan_faqat_bittasi_otadi()
    {
        var (cashier, client) = await ClientAsync(Roles.Cashier);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 2).Select(async _ =>
        {
            await start.Task;
            return await client.PostAsJsonAsync("/api/cash/shifts/open", new { openingFloat = 0m });
        }).ToList();

        start.SetResult();
        var responses = await Task.WhenAll(attempts);

        var accepted = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var rejected = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        Assert.Single(accepted);
        var conflict = Assert.Single(rejected);
        Assert.Equal(CashShiftError.AlreadyOpen,
            (await conflict.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        var winner = (await accepted[0].Content.ReadFromJsonAsync<CashShiftDto>())!;

        await using var db = NewDb();
        var open = await db.CashShifts.AsNoTracking()
            .Where(s => s.CashierId == cashier.Id && s.Status == CashShiftStatus.Open)
            .ToListAsync();

        Assert.Equal(winner.Id, Assert.Single(open).Id);
    }

    // =====================================================================
    //  2) O'zganing smenasini yopish → 403
    // =====================================================================

    /// <summary>
    /// SPEC §4.2: kassir o'zganing smenasini yopa olmaydi.
    ///
    /// <para>
    /// Rad etishdan keyin smena OCHIQ qolishi shart. Agar endpoint 403
    /// qaytarib, smenani baribir yopib qo'ysa — begona odam kassirning kunini
    /// yopib, unga nomuvofiqlik yozib qo'ya olardi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Ozganing_smenasini_yopishga_urinish_403_va_smena_ochiq_qoladi()
    {
        var (owner, ownerClient) = await ClientAsync(Roles.Cashier);
        var (_, strangerClient) = await ClientAsync(Roles.Cashier);

        var shift = await OpenShiftAsync(ownerClient);

        var response = await strangerClient.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close", new { countedCash = 0m, note = "men yopdim" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<ErrorBody>())!;
        Assert.Equal(CashShiftError.NotYourShift, error.Code);

        // Smena TEGILMAGAN: hali ham ochiq, sanalgan naqd yo'q, yopgan odam yo'q.
        await using var db = NewDb();
        var stored = await db.CashShifts.AsNoTracking().FirstAsync(s => s.Id == shift.Id);
        Assert.Equal(CashShiftStatus.Open, stored.Status);
        Assert.Equal(owner.Id, stored.CashierId);
        Assert.Null(stored.CountedCash);
        Assert.Null(stored.ExpectedCash);
        Assert.Null(stored.ClosedAt);
        Assert.Null(stored.ClosedBy);
        Assert.Null(stored.Variance);
    }

    /// <summary>
    /// Yuqoridagi 403 UMUMIY TAQIQ EMAS: admin (va direktor) o'zganing
    /// smenasini yopa oladi, va <c>closed_by</c> da AYNAN u qoladi (SPEC §4.2).
    ///
    /// <para>
    /// Bu testsiz "hammaga 403" degan xato ham yashil bo'lib o'tardi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Admin_ozganing_smenasini_yopa_oladi_va_closed_by_da_qoladi()
    {
        var (owner, ownerClient) = await ClientAsync(Roles.Cashier);
        var (admin, adminClient) = await ClientAsync(Roles.Admin);

        var shift = await OpenShiftAsync(ownerClient, openingFloat: 40_000m);

        var response = await adminClient.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close",
            new { countedCash = 40_000m, note = "kassir smenani yopmasdan ketib qoldi" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var closed = (await response.Content.ReadFromJsonAsync<CashShiftDto>())!;
        Assert.Equal(CashShiftStatus.Closed, closed.Status);
        Assert.Equal(owner.Id, closed.CashierId);          // egasi o'zgarmadi
        Assert.Equal(admin.FullName, closed.ClosedByName); // yopgani ko'rinib turadi
        Assert.Equal(40_000m, closed.ExpectedCash);
        Assert.Equal(0m, closed.Variance);

        await using var db = NewDb();
        var stored = await db.CashShifts.AsNoTracking().FirstAsync(s => s.Id == shift.Id);
        Assert.Equal(admin.Id, stored.ClosedBy);
        Assert.Equal(owner.Id, stored.CashierId);
    }

    // =====================================================================
    //  3) variance = counted − expected, va uni YOZIB BO'LMAYDI
    // =====================================================================

    /// <summary>
    /// SPEC §4.2: <c>variance = counted_cash − expected_cash</c>.
    /// <c>expected_cash</c> LEDGER'dan hisoblanadi va so'rovdan QABUL QILINMAYDI.
    ///
    /// <para>
    /// Uch holat: kam chiqdi (manfiy), ortiq chiqdi (musbat) va to'g'ri
    /// chiqdi (nol). Nol alohida kerak — "variance har doim hisoblanadi"
    /// bilan "variance faqat farq bo'lganda yoziladi" ni ajratadi.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(280_000, -20_000)]  // kam sanadi
    [InlineData(325_000, 25_000)]   // ortiq sanadi (mijozning qaytimi qolib ketgan)
    [InlineData(300_000, 0)]        // aniq
    public async Task Variance_sanalgan_minus_kutilgan_ga_teng(int counted, int expectedVariance)
    {
        var (cashier, client) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(client, openingFloat: 50_000m);
        var scene = await SceneAsync(amounts: [250_000m]);

        await AcceptAsync(client, scene, 0, PaymentMethod.Cash);

        var response = await client.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close", new { countedCash = (decimal)counted, note = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var closed = (await response.Content.ReadFromJsonAsync<CashShiftDto>())!;

        // Kutilgan naqd = ochilish qoldig'i + naqd to'lov. Kassir bermaydi — server hisoblaydi.
        Assert.Equal(300_000m, closed.ExpectedCash);
        Assert.Equal((decimal)counted, closed.CountedCash);
        Assert.Equal((decimal)expectedVariance, closed.Variance);
        Assert.Equal(closed.CountedCash - closed.ExpectedCash, closed.Variance);

        // Baza ham xuddi shunday hisoblagan (generated column).
        await using var db = NewDb();
        var stored = await db.CashShifts.AsNoTracking().FirstAsync(s => s.Id == shift.Id);
        Assert.Equal((decimal)expectedVariance, stored.Variance);
        Assert.Equal(stored.CountedCash - stored.ExpectedCash, stored.Variance);
    }

    /// <summary>
    /// <b>Nomuvofiqlikni to'g'ridan-to'g'ri YOZIB BO'LMAYDI.</b> <c>variance</c> —
    /// <c>generated always as (counted_cash - expected_cash) stored</c> ustuni,
    /// unga <c>UPDATE</c> SQLSTATE <b>428C9</b> bilan rad etiladi.
    ///
    /// <para>
    /// So'rov ILOVA ROLI (<c>app_rw</c>) ulanishi bilan yuboriladi — ya'ni
    /// so'rov yo'lidagi kod qaysi huquqlar bilan ishlasa, aynan o'shalar bilan.
    /// <c>RequireRealAppRw()</c> ATAYLAB chaqirilmagan: bu kafolat GRANT'ga
    /// emas, ustunning turiga tayanadi va jadval EGASI uchun ham amal qiladi
    /// (owner <c>REVOKE</c> ni chetlab o'tadi, generated column'ni esa yo'q).
    /// Ya'ni test P1-02/P1-22 dan oldin ham, keyin ham bir xil ma'noga ega.
    /// <c>payments</c> / <c>ledger_entries</c> ustidagi GRANT'ga tayanadigan
    /// taqiq — P1-22 ning ishi.
    /// </para>
    /// <para>
    /// Nazorat tekshiruvi ham bor: o'sha ulanish bilan ODDIY ustunni yangilash
    /// O'TADI. Busiz test "app_rw ga cash_shifts butunlay yopiq" degan
    /// butunlay boshqa sabab tufayli ham yashil bo'lib ketardi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Variance_ustuniga_togridan_togri_UPDATE_rad_etiladi()
    {
        var (cashier, client) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(client);
        var scene = await SceneAsync(amounts: [300_000m]);
        await AcceptAsync(client, scene, 0, PaymentMethod.Cash);

        var closeResponse = await client.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close", new { countedCash = 250_000m, note = "50 ming yetishmadi" });
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        Assert.Equal(-50_000m, (await closeResponse.Content.ReadFromJsonAsync<CashShiftDto>())!.Variance);

        await using var connection = new NpgsqlConnection(fixture.Database.AppRwConnectionString);
        await connection.OpenAsync();

        foreach (var sql in new[]
                 {
                     "UPDATE cash_shifts SET variance = 0 WHERE id = @id",
                     "UPDATE cash_shifts SET variance = variance WHERE id = @id",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", shift.Id);

            var pg = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal("428C9", pg.SqlState);
        }

        // NAZORAT: shu ulanish oddiy ustunni yangilay OLADI — demak yuqoridagi
        // rad etish aynan generated column tufayli, huquq yetishmasligi tufayli emas.
        await using (var control = new NpgsqlCommand(
                         "UPDATE cash_shifts SET closed_by = closed_by WHERE id = @id", connection))
        {
            control.Parameters.AddWithValue("id", shift.Id);
            Assert.Equal(1, await control.ExecuteNonQueryAsync());
        }

        // Nomuvofiqlik o'sha-o'sha.
        await using var db = NewDb();
        var stored = await db.CashShifts.AsNoTracking().FirstAsync(s => s.Id == shift.Id);
        Assert.Equal(-50_000m, stored.Variance);
        Assert.Equal(250_000m, stored.CountedCash);
        Assert.Equal(300_000m, stored.ExpectedCash);
    }

    /// <summary>
    /// Sanalgan naqd ikki kasrgacha yaxlitlanadi (<c>numeric(14,2)</c>), va
    /// nomuvofiqlik AYNAN yaxlitlangan qiymatdan hisoblanadi. Aks holda
    /// hisobotdagi tiyinlar kunlar davomida bir-biriga qo'shilib, hech kim
    /// tushuntira olmaydigan farq berardi.
    /// </summary>
    [Fact]
    public async Task Sanalgan_naqd_ikki_kasrgacha_yaxlitlanadi()
    {
        var (_, client) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(client, openingFloat: 100_000m);

        var response = await client.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close", new { countedCash = 100_000.567m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var closed = (await response.Content.ReadFromJsonAsync<CashShiftDto>())!;

        Assert.Equal(100_000.57m, closed.CountedCash);
        Assert.Equal(100_000m, closed.ExpectedCash);
        Assert.Equal(0.57m, closed.Variance);
    }

    // =====================================================================
    //  4) Z-hisobot
    // =====================================================================

    /// <summary>
    /// <b>Z-hisobotning usullar kesimi = SHU smena to'lovlarining usul
    /// bo'yicha yig'indisi</b> (SPEC §4.6).
    ///
    /// <para>
    /// Kutilgan qiymatlar IKKI YO'L bilan tekshiriladi: (1) testda qo'lda
    /// yozilgan sonlar bilan — bu asosiy himoya; (2) bazadan MUSTAQIL
    /// hisoblangan guruhlash bilan — bu esa hisobot ilova xotirasidagi
    /// biror keshdan emas, aynan yozilgan qatorlardan yig'ilishini tasdiqlaydi.
    /// Faqat ikkinchisi bo'lsa, ikkala tomon bir xil xatoni takrorlashi
    /// mumkin edi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Z_hisobot_usullar_kesimi_smena_tolovlari_yigindisiga_teng()
    {
        var (cashier, client) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(client, openingFloat: 60_000m);
        var scene = await SceneAsync(
            amounts: [500_000m, 250_000m, 300_000m, 150_000m, 90_000m]);

        await AcceptAsync(client, scene, 0, PaymentMethod.Cash);
        await AcceptAsync(client, scene, 1, PaymentMethod.Cash);
        await AcceptAsync(client, scene, 2, PaymentMethod.Card);
        await AcceptAsync(client, scene, 3, PaymentMethod.Transfer);
        await AcceptAsync(client, scene, 4, PaymentMethod.Online);

        var response = await client.GetAsync($"/api/cash/shifts/{shift.Id}/z-report");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<ZReportDto>())!;

        // --- Usullar: to'rttasi ham, tartibi barqaror ---
        Assert.Equal(PaymentMethod.All.ToList(), report.ByMethod.Select(r => r.Method).ToList());

        AssertMethod(report, PaymentMethod.Cash, count: 2, amount: 750_000m);
        AssertMethod(report, PaymentMethod.Card, count: 1, amount: 300_000m);
        AssertMethod(report, PaymentMethod.Transfer, count: 1, amount: 150_000m);
        AssertMethod(report, PaymentMethod.Online, count: 1, amount: 90_000m);

        // --- O'sha yig'indi, bazadan MUSTAQIL hisoblangan ---
        await using var db = NewDb();
        var grouped = await db.Payments.AsNoTracking()
            .Where(p => p.CashShiftId == shift.Id)
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Count = g.Count(), Total = g.Sum(p => p.Amount) })
            .ToListAsync();

        Assert.Equal(4, grouped.Count);
        foreach (var row in grouped)
        {
            var reported = report.ByMethod.Single(r => r.Method == row.Method);
            Assert.Equal(row.Count, reported.Count);
            Assert.Equal(row.Total, reported.Amount);
        }

        // Hech bir to'lov tushib qolmadi va ortiqchasi qo'shilmadi.
        Assert.Equal(5, report.ByMethod.Sum(r => r.Count));
        Assert.Equal(1_290_000m, report.ByMethod.Sum(r => r.Amount));
        Assert.Equal(0, report.ReversalsCount);

        // --- Chek oralig'i ---
        Assert.Equal(1L, report.ReceiptFrom);
        Assert.Equal(5L, report.ReceiptTo);

        // --- Smena sarlavhasi: naqd va naqdsiz alohida ---
        Assert.Equal(5, report.Shift.PaymentsCount);
        Assert.Equal(750_000m, report.Shift.CashTotal);
        Assert.Equal(540_000m, report.Shift.NonCashTotal);
        Assert.Equal(60_000m, report.Shift.OpeningFloat);

        // --- Toifalar kesimi: hamma pul `tuition` ga taqsimlangan ---
        var tuition = Assert.Single(report.ByCategory);
        Assert.Equal("tuition", tuition.CategoryCode);
        Assert.Equal(1_290_000m, tuition.Amount);

        // --- Yopilgandan keyin: kutilgan naqd = ochilish qoldig'i + NAQD qatori ---
        var close = await client.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close", new { countedCash = 810_000m });
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        var closed = (await close.Content.ReadFromJsonAsync<CashShiftDto>())!;

        Assert.Equal(810_000m, closed.ExpectedCash);   // 60 000 + 750 000, karta/o'tkazma/onlayn KIRMAYDI
        Assert.Equal(0m, closed.Variance);
    }

    /// <summary>
    /// Storno tasdiqlovchining O'Z smenasiga tushadi va o'sha smenaning
    /// usullar kesimida MANFIY ko'rinadi (summa bazada musbat, ma'nosi —
    /// "pul qaytdi").
    ///
    /// <para>
    /// Bu qoidani belgi (ishora) bilan tekshirish shart: agar hisobot storno'ni
    /// oddiy to'lov kabi qo'shsa, kun oxirida kassa ikki barobar ko'p pul
    /// olgandek ko'rinardi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Storno_tasdiqlovchining_Z_hisobotida_manfiy_korinadi()
    {
        var (cashier, cashierClient) = await ClientAsync(Roles.Cashier);
        var cashierShift = await OpenShiftAsync(cashierClient);
        var scene = await SceneAsync(amounts: [500_000m]);
        var payment = await AcceptAsync(cashierClient, scene, 0, PaymentMethod.Cash);

        // Storno tasdiqlovchining O'Z ochiq smenasiga yoziladi (SPEC §4.5 —
        // ikki qavatli nazorat: kassir o'z to'lovini o'zi storno qila olmaydi).
        var (_, adminClient) = await ClientAsync(Roles.Admin);
        var adminShift = await OpenShiftAsync(adminClient);

        var reverse = await adminClient.PostAsJsonAsync(
            $"/api/admin/payments/{payment.Id}/reverse", new { reason = "kassir summani xato kiritgan" });
        Assert.Equal(HttpStatusCode.OK, reverse.StatusCode);

        var storno = (await reverse.Content.ReadFromJsonAsync<PaymentDto>())!;
        Assert.Equal(payment.Id, storno.ReversalOf);
        Assert.Equal(adminShift.Id, storno.CashShiftId);
        Assert.Equal(500_000m, storno.Amount);

        // Tasdiqlovchining Z-hisoboti: bitta naqd qator, MANFIY.
        var response = await adminClient.GetAsync($"/api/cash/shifts/{adminShift.Id}/z-report");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<ZReportDto>())!;

        AssertMethod(report, PaymentMethod.Cash, count: 1, amount: -500_000m);
        AssertMethod(report, PaymentMethod.Card, count: 0, amount: 0m);
        Assert.Equal(1, report.ReversalsCount);
        Assert.Equal(-500_000m, report.Shift.CashTotal);

        // Kassirning smenasida esa ASL to'lov o'z o'rnida qoladi — storno uni
        // O'CHIRMAYDI (SPEC §4.1: tuzatish faqat qarshi yozuv bilan).
        var ownReport = (await (await cashierClient
            .GetAsync($"/api/cash/shifts/{cashierShift.Id}/z-report"))
            .Content.ReadFromJsonAsync<ZReportDto>())!;

        AssertMethod(ownReport, PaymentMethod.Cash, count: 1, amount: 500_000m);
        Assert.Equal(0, ownReport.ReversalsCount);
        Assert.Equal(1L, ownReport.ReceiptFrom);
        Assert.Equal(1L, ownReport.ReceiptTo);
    }

    /// <summary>
    /// Z-hisobot SMENA CHEGARASIDAN chiqmaydi: ikkinchi kassirning pullari
    /// birinchisining hisobotiga qo'shilmaydi va aksincha.
    ///
    /// <para>
    /// Chegara buzilsa har ikkala kassir ham boshqasining pulini "topshirishi"
    /// kerak bo'lardi — nomuvofiqlik hisoboti butunlay ma'nosiz bo'lardi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Z_hisobot_boshqa_smenaning_tolovlarini_qoshmaydi()
    {
        var (firstCashier, firstClient) = await ClientAsync(Roles.Cashier);
        var firstShift = await OpenShiftAsync(firstClient);
        var firstScene = await SceneAsync(amounts: [120_000m]);
        await AcceptAsync(firstClient, firstScene, 0, PaymentMethod.Cash);

        var (secondCashier, secondClient) = await ClientAsync(Roles.Cashier);
        var secondShift = await OpenShiftAsync(secondClient);
        var secondScene = await SceneAsync(amounts: [77_000m, 33_000m]);
        await AcceptAsync(secondClient, secondScene, 0, PaymentMethod.Cash);
        await AcceptAsync(secondClient, secondScene, 1, PaymentMethod.Card);

        var first = (await (await firstClient.GetAsync($"/api/cash/shifts/{firstShift.Id}/z-report"))
            .Content.ReadFromJsonAsync<ZReportDto>())!;
        var second = (await (await secondClient.GetAsync($"/api/cash/shifts/{secondShift.Id}/z-report"))
            .Content.ReadFromJsonAsync<ZReportDto>())!;

        AssertMethod(first, PaymentMethod.Cash, count: 1, amount: 120_000m);
        AssertMethod(first, PaymentMethod.Card, count: 0, amount: 0m);
        Assert.Equal(1, first.Shift.PaymentsCount);
        Assert.Equal(firstCashier.Id, first.Shift.CashierId);

        AssertMethod(second, PaymentMethod.Cash, count: 1, amount: 77_000m);
        AssertMethod(second, PaymentMethod.Card, count: 1, amount: 33_000m);
        Assert.Equal(2, second.Shift.PaymentsCount);
        Assert.Equal(secondCashier.Id, second.Shift.CashierId);

        // Har ikkala smenada chek raqami 1 dan boshlanadi — ular mustaqil.
        Assert.Equal(1L, first.ReceiptFrom);
        Assert.Equal(1L, second.ReceiptFrom);
        Assert.Equal(2L, second.ReceiptTo);
    }

    /// <summary>To'lovsiz smenaning hisoboti ham to'liq jadval beradi — bo'sh javob emas.</summary>
    [Fact]
    public async Task Bosh_smenaning_Z_hisoboti_nollar_bilan_qaytadi()
    {
        var (_, client) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(client, openingFloat: 25_000m);

        var report = (await (await client.GetAsync($"/api/cash/shifts/{shift.Id}/z-report"))
            .Content.ReadFromJsonAsync<ZReportDto>())!;

        Assert.Equal(PaymentMethod.All.ToList(), report.ByMethod.Select(r => r.Method).ToList());
        Assert.All(report.ByMethod, r =>
        {
            Assert.Equal(0, r.Count);
            Assert.Equal(0m, r.Amount);
        });
        Assert.Empty(report.ByCategory);
        Assert.Null(report.ReceiptFrom);
        Assert.Null(report.ReceiptTo);
        Assert.Equal(0, report.ReversalsCount);
        Assert.Equal(25_000m, report.Shift.OpeningFloat);
        Assert.Null(report.Shift.Variance);
    }

    // =====================================================================
    //  5) Rol darvozasi (SPEC §4.3) — har fe'l uchun alohida
    // =====================================================================

    /// <summary>
    /// SPEC §4.3: kassa yuzasi kassir/admin/direktordan boshqasiga YOPIQ.
    ///
    /// <para>
    /// Har bir fe'l alohida tekshiriladi (GET current, POST open, POST close,
    /// GET z-report, GET list): unutilgan atribut faqat bitta endpointda
    /// bo'lishi mumkin, va aynan o'sha jimgina ochiq qolardi.
    /// </para>
    /// <para>
    /// Rad javobi MA'LUMOT SIZDIRMASLIGI ham tekshiriladi: 403 tanasida
    /// smenaning id'si yoki qoldig'i ko'rinmasin.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]
    [InlineData("parent")]
    public async Task Kassa_endpointlari_begona_rolga_403(string role)
    {
        var (cashier, cashierClient) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(cashierClient, openingFloat: 111_000m);

        var (_, client) = await ClientAsync(role);

        var responses = new (string Verb, HttpResponseMessage Response)[]
        {
            ("GET current", await client.GetAsync("/api/cash/shifts/current")),
            ("POST open", await client.PostAsJsonAsync("/api/cash/shifts/open", new { openingFloat = 0m })),
            ("POST close", await client.PostAsJsonAsync(
                $"/api/cash/shifts/{shift.Id}/close", new { countedCash = 0m })),
            ("GET z-report", await client.GetAsync($"/api/cash/shifts/{shift.Id}/z-report")),
            ("GET list", await client.GetAsync("/api/cash/shifts")),
        };

        foreach (var (verb, response) in responses)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                $"{verb} — '{role}' roli uchun 403 kutilgan edi, {(int)response.StatusCode} keldi.");

            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(shift.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("openingFloat", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("111000", body, StringComparison.Ordinal);
        }

        // Begona rol hech narsa yaratmadi ham.
        await using var db = NewDb();
        Assert.Equal(1, await db.CashShifts.AsNoTracking()
            .CountAsync(s => s.CashierId == cashier.Id));
    }

    /// <summary>
    /// Token'siz — 401; muddati o'tgan token bilan ham — 401, lekin
    /// <c>WWW-Authenticate</c> sarlavhasida SABAB ko'rinadi. Ikkalasini
    /// farqlash muhim: "token yo'q" bilan "token eskirgan" mijoz uchun
    /// butunlay boshqa xatti-harakat (login vs jimgina yangilash).
    /// </summary>
    [Fact]
    public async Task Kassa_endpointlari_tokensiz_va_eskirgan_token_bilan_401()
    {
        var (cashier, cashierClient) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(cashierClient);

        string[] paths =
        [
            "/api/cash/shifts/current",
            "/api/cash/shifts",
            $"/api/cash/shifts/{shift.Id}/z-report",
        ];

        using var anonymous = fixture.Api.AnonymousClient();
        foreach (var path in paths)
        {
            var response = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
            Assert.DoesNotContain(shift.Id.ToString(), await response.Content.ReadAsStringAsync(),
                StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/cash/shifts/open", new { openingFloat = 0m })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync(
                $"/api/cash/shifts/{shift.Id}/close", new { countedCash = 0m })).StatusCode);

        // Muddati o'tgan token. JWT standart clock skew'i 5 daqiqa — -10 daqiqa yetarli.
        var expired = fixture.Api.TokenFor(
            Roles.Cashier, cashier.Id, cashier.FullName, cashier.Email,
            lifetime: TimeSpan.FromMinutes(-10));

        using var stale = fixture.Api.ClientWithToken(expired);

        var staleResponse = await stale.GetAsync("/api/cash/shifts/current");
        Assert.Equal(HttpStatusCode.Unauthorized, staleResponse.StatusCode);

        var challenge = staleResponse.Headers.WwwAuthenticate.ToString();
        Assert.Contains("invalid_token", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expired", challenge, StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================
    //  6) Izolyatsiya / IDOR
    // =====================================================================

    /// <summary>
    /// Kassir BOSHQA kassirning Z-hisobotini ko'ra olmaydi (SPEC §4.3) —
    /// va rad javobi hisobotning bir bo'lagini ham sizdirmaydi.
    ///
    /// <para>
    /// Hisobot xizmatda AVVAL yig'iladi, egalik esa keyin tekshiriladi
    /// (controller izohi). Shuning uchun javob tanasida yig'ilgan ma'lumot
    /// tasodifan qolib ketmasligi alohida tekshiriladi: summa, kassir ismi,
    /// chek raqami — hech biri bo'lmasin.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Kassir_boshqa_kassirning_Z_hisobotini_kora_olmaydi()
    {
        var (owner, ownerClient) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(ownerClient);
        var scene = await SceneAsync(amounts: [432_100m]);
        await AcceptAsync(ownerClient, scene, 0, PaymentMethod.Cash);

        // Egasi ko'radi.
        var mine = await ownerClient.GetAsync($"/api/cash/shifts/{shift.Id}/z-report");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal(432_100m,
            (await mine.Content.ReadFromJsonAsync<ZReportDto>())!.Shift.CashTotal);

        // Begona kassir — ko'rmaydi.
        var (_, strangerClient) = await ClientAsync(Roles.Cashier);
        var response = await strangerClient.GetAsync($"/api/cash/shifts/{shift.Id}/z-report");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<ErrorBody>())!;
        Assert.Equal(CashShiftError.NotYourShift, error.Code);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("432100", body, StringComparison.Ordinal);
        Assert.DoesNotContain(owner.FullName, body, StringComparison.Ordinal);
        Assert.DoesNotContain("receiptFrom", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ro'yxatda kassir FAQAT o'zining smenalarini ko'radi — so'rovda
    /// ATAYLAB o'zganing <c>cashierId</c> si yozilgan bo'lsa ham.
    ///
    /// <para>
    /// Bu 403 emas (controller filtrni jimgina toraytiradi), shuning uchun
    /// tekshiruv status kodida emas: begona smenaning id'si javobda BO'LMASLIGI
    /// kerak. Filtr olib tashlansa test aynan shu yerda qiziradi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Kassir_royxatda_boshqaning_smenasini_kormaydi()
    {
        var (other, otherClient) = await ClientAsync(Roles.Cashier);
        var otherShift = await OpenShiftAsync(otherClient, openingFloat: 777_000m);

        var (mine, myClient) = await ClientAsync(Roles.Cashier);
        var myShift = await OpenShiftAsync(myClient, openingFloat: 10_000m);

        var response = await myClient.GetAsync($"/api/cash/shifts?cashierId={other.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = (await response.Content.ReadFromJsonAsync<List<CashShiftDto>>())!;
        Assert.NotEmpty(list);
        Assert.All(list, s => Assert.Equal(mine.Id, s.CashierId));
        Assert.Contains(list, s => s.Id == myShift.Id);
        Assert.DoesNotContain(list, s => s.Id == otherShift.Id);

        // Admin esa ikkalasini ham ko'radi — filtr "hammaga yopiq" emas.
        var (_, adminClient) = await ClientAsync(Roles.Admin);
        var all = (await (await adminClient.GetAsync("/api/cash/shifts"))
            .Content.ReadFromJsonAsync<List<CashShiftDto>>())!;

        Assert.Contains(all, s => s.Id == myShift.Id);
        Assert.Contains(all, s => s.Id == otherShift.Id);
    }

    /// <summary>
    /// Mavjud bo'lmagan smena — 404, 500 emas va boshqa smenaning ma'lumoti emas.
    /// </summary>
    [Fact]
    public async Task Mavjud_bolmagan_smena_uchun_404()
    {
        var (_, client) = await ClientAsync(Roles.Cashier);
        var ghost = Guid.NewGuid();

        var report = await client.GetAsync($"/api/cash/shifts/{ghost}/z-report");
        Assert.Equal(HttpStatusCode.NotFound, report.StatusCode);
        Assert.Equal(CashShiftError.NotFound,
            (await report.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        var close = await client.PostAsJsonAsync(
            $"/api/cash/shifts/{ghost}/close", new { countedCash = 0m });
        Assert.Equal(HttpStatusCode.NotFound, close.StatusCode);
        Assert.Equal(CashShiftError.NotFound,
            (await close.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    // =====================================================================
    //  7) So'rov validatsiyasi va holat qoidalari
    // =====================================================================

    /// <summary>
    /// Manfiy ochilish qoldig'i rad etiladi (400) va smena YARATILMAYDI.
    /// Bazada ham <c>ck_cash_shifts_opening_float</c> bor — ikki qavat.
    /// </summary>
    [Fact]
    public async Task Manfiy_ochilish_qoldigi_400_va_smena_yaratilmaydi()
    {
        var (cashier, client) = await ClientAsync(Roles.Cashier);

        var response = await client.PostAsJsonAsync("/api/cash/shifts/open", new { openingFloat = -1m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CashShiftError.InvalidOpeningFloat,
            (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        await using var db = NewDb();
        Assert.Equal(0, await db.CashShifts.AsNoTracking().CountAsync(s => s.CashierId == cashier.Id));
    }

    /// <summary>Manfiy sanalgan naqd rad etiladi va smena OCHIQ qoladi.</summary>
    [Fact]
    public async Task Manfiy_sanalgan_naqd_400_va_smena_ochiq_qoladi()
    {
        var (_, client) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close", new { countedCash = -5m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CashShiftError.InvalidCountedCash,
            (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        await using var db = NewDb();
        var stored = await db.CashShifts.AsNoTracking().FirstAsync(s => s.Id == shift.Id);
        Assert.Equal(CashShiftStatus.Open, stored.Status);
        Assert.Null(stored.CountedCash);
    }

    /// <summary>
    /// <c>countedCash</c> son emas (yoki umuman yo'q) bo'lsa — 400, jimgina 0 emas.
    /// Nol HAQIQIY qiymat (naqd to'lovsiz smena), shuning uchun "sanamadim" bilan
    /// "sanadim, nol chiqdi" ni farqlash SPEC §4.2 talabi.
    /// </summary>
    [Theory]
    [InlineData("{\"countedCash\":\"salom\"}")]
    [InlineData("{\"countedCash\":null}")]
    [InlineData("{\"countedCash\":true}")]
    [InlineData("{\"note\":\"sanamadim\"}")]
    [InlineData("{}")]
    public async Task Notogri_countedCash_400_va_smena_ochiq_qoladi(string json)
    {
        var (_, client) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(client);

        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"/api/cash/shifts/{shift.Id}/close", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CashShiftError.InvalidCountedCash,
            (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        await using var db = NewDb();
        Assert.Equal(CashShiftStatus.Open,
            (await db.CashShifts.AsNoTracking().FirstAsync(s => s.Id == shift.Id)).Status);
    }

    /// <summary>
    /// Satr ichidagi son QABUL QILINADI ("150000" — JS'ning odatiy chiqishi).
    /// Bu shartnomaning bir qismi; qat'iylashtirilsa kassa ekrani buziladi.
    /// </summary>
    [Fact]
    public async Task Satr_korinishidagi_countedCash_qabul_qilinadi()
    {
        var (_, client) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(client, openingFloat: 150_000m);

        using var content = new StringContent(
            "{\"countedCash\":\"150000\"}", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"/api/cash/shifts/{shift.Id}/close", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var closed = (await response.Content.ReadFromJsonAsync<CashShiftDto>())!;
        Assert.Equal(150_000m, closed.CountedCash);
        Assert.Equal(0m, closed.Variance);
    }

    /// <summary>
    /// Yopilgan smenani QAYTA yopib bo'lmaydi (409) — birinchi yopilishdagi
    /// sanalgan naqd va nomuvofiqlik o'zgarmaydi. Bu <c>variance</c> ni
    /// "qayta yopish" orqali to'g'irlashning oldini oladi.
    /// </summary>
    [Fact]
    public async Task Yopilgan_smenani_qayta_yopish_409_va_raqamlar_ozgarmaydi()
    {
        var (cashier, client) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(client);
        var scene = await SceneAsync(amounts: [100_000m]);
        await AcceptAsync(client, scene, 0, PaymentMethod.Cash);

        var first = await client.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close", new { countedCash = 90_000m });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(-10_000m, (await first.Content.ReadFromJsonAsync<CashShiftDto>())!.Variance);

        // Ikkinchi urinish — "nomuvofiqlikni nolga tenglashtirish".
        var second = await client.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close", new { countedCash = 100_000m });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(CashShiftError.AlreadyClosed,
            (await second.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        await using var db = NewDb();
        var stored = await db.CashShifts.AsNoTracking().FirstAsync(s => s.Id == shift.Id);
        Assert.Equal(90_000m, stored.CountedCash);
        Assert.Equal(-10_000m, stored.Variance);
    }

    /// <summary>
    /// Smena yopilgach unga YANGI to'lov qabul qilinmaydi (409): chek raqami
    /// berilsa, allaqachon chop etilgan Z-hisobot orqadan yolg'on bo'lib qolardi.
    /// </summary>
    [Fact]
    public async Task Yopilgan_smenaga_yangi_tolov_qabul_qilinmaydi()
    {
        var (cashier, client) = await ClientAsync(Roles.Cashier);
        var shift = await OpenShiftAsync(client);
        var scene = await SceneAsync(amounts: [100_000m, 50_000m]);
        await AcceptAsync(client, scene, 0, PaymentMethod.Cash);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close", new { countedCash = 100_000m })).StatusCode);

        var response = await client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId = scene.StudentIds[1],
            amount = 50_000m,
            method = PaymentMethod.Cash,
            allocations = new[] { new { invoiceId = scene.InvoiceIds[1], amount = 50_000m } },
        });

        // Ochiq smena yo'q — bitta ham pul qatori yozilmaydi.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("no_open_shift",
            (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        await using var db = NewDb();
        Assert.Equal(1, await db.Payments.AsNoTracking().CountAsync(p => p.CashShiftId == shift.Id));
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static void AssertMethod(ZReportDto report, string method, int count, decimal amount)
    {
        var row = report.ByMethod.Single(r => r.Method == method);
        Assert.Equal(count, row.Count);
        Assert.Equal(amount, row.Amount);
    }

    /// <summary>Bitta smena uchun o'quvchilar, hisob-fakturalar va ularning summalari.</summary>
    /// <summary>
    /// STORNO PULI TASDIQLOVCHINING SMENASIDAN CHIQADI, KASSIRNIKIDAN EMAS.
    ///
    /// <para>
    /// <c>PaymentService.ReverseAsync</c> storno <c>payments</c> qatorini
    /// tasdiqlovchining ochiq smenasiga yozadi — "pul bugun, uning kassasidan
    /// chiqadi". Jurnal tomoni ham SHU smenaga tushishi shart, chunki
    /// <c>ExpectedCashAsync</c> smena satrlarini <c>ref_id ∈ (shu smenaning
    /// to'lovlari)</c> bo'yicha topadi.
    /// </para>
    ///
    /// <para>
    /// Ilgari ko'zgu satr ORIGINALNING <c>ref_id</c> sini saqlab qolardi, ya'ni
    /// pul qaytarilishi kassirning smenasidan yechilardi. O'lchangan natija:
    /// kassir +500 000 ortiqcha pul bilan (aslida pul javonida turibdi),
    /// tasdiqlovchining kassasi esa nol farq bilan yopilardi. Bu test aynan
    /// shuni ushlab turadi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Storno_puli_kassirning_emas_tasdiqlovchining_smenasidan_yechiladi()
    {
        const decimal amount = 500_000m;
        const decimal approverFloat = 500_000m;

        var scene = await SceneAsync([amount]);
        var (_, cashier) = await ClientAsync(Roles.Cashier);
        var (_, approver) = await ClientAsync(Roles.Admin);

        var cashierShift = await OpenShiftAsync(cashier);
        var payment = await AcceptAsync(cashier, scene, 0, PaymentMethod.Cash);

        // Tasdiqlovchi O'Z smenasini ochadi — pul aynan shu javondan qaytadi.
        var approverShift = await OpenShiftAsync(approver, approverFloat);

        var reversal = await approver.PostAsJsonAsync(
            $"/api/admin/payments/{payment.Id}/reverse",
            new { reason = "Kassir xato summa kiritdi" });
        Assert.Equal(HttpStatusCode.OK, reversal.StatusCode);

        // Kassirning javonida pul QOLDI: u hech narsa qaytargani yo'q.
        var cashierClose = await cashier.PostAsJsonAsync(
            $"/api/cash/shifts/{cashierShift.Id}/close",
            new { countedCash = amount, note = (string?)null });
        Assert.Equal(HttpStatusCode.OK, cashierClose.StatusCode);
        var closedCashier = (await cashierClose.Content.ReadFromJsonAsync<CashShiftDto>())!;

        // Tasdiqlovchining javonidan pul CHIQDI: 500 000 float − 500 000 qaytim.
        var approverClose = await approver.PostAsJsonAsync(
            $"/api/cash/shifts/{approverShift.Id}/close",
            new { countedCash = 0m, note = (string?)null });
        Assert.Equal(HttpStatusCode.OK, approverClose.StatusCode);
        var closedApprover = (await approverClose.Content.ReadFromJsonAsync<CashShiftDto>())!;

        Assert.Equal(cashierShift.Id, closedCashier.Id);
        Assert.Equal(approverShift.Id, closedApprover.Id);

        // ASOSIY TASDIQ: ikkala smena ham NOL farq bilan yopiladi.
        Assert.Equal(amount, closedCashier.ExpectedCash);
        Assert.Equal(0m, closedCashier.Variance);

        Assert.Equal(0m, closedApprover.ExpectedCash);
        Assert.Equal(0m, closedApprover.Variance);
    }

    private sealed record Scene(List<string> StudentIds, List<Guid> InvoiceIds, decimal[] Amounts);

    /// <summary>
    /// Har to'lovga O'Z o'quvchisi va O'Z hisob-fakturasi: shunda test
    /// taqsimot qoldig'i qoidasiga emas, smena hisobotiga qaraydi.
    /// Ma'lumot OWNER ulanishi bilan tayyorlanadi.
    /// </summary>
    private async Task<Scene> SceneAsync(decimal[] amounts)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var studentIds = new List<string>(amounts.Length);
        var invoiceIds = new List<Guid>(amounts.Length);

        await fixture.Api.WithDbAsync(async db =>
        {
            var categoryId = await db.FeeCategories.AsNoTracking()
                .Where(c => c.Code == "tuition").Select(c => c.Id).SingleAsync();

            var month = new DateOnly(AppClock.Today.Year, AppClock.Today.Month, 1);

            for (var i = 0; i < amounts.Length; i++)
            {
                var student = new Student
                {
                    FullName = $"Smena o'quvchisi {suffix}-{i}",
                    LastName = "Smena",
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
                    Amount = amounts[i],
                    Discount = 0m,
                    DueOn = month.AddDays(9),
                    Status = InvoiceStatus.Open,
                    CreatedAt = AppClock.NowInstant,
                };
                db.Invoices.Add(invoice);

                studentIds.Add(student.Id);
                invoiceIds.Add(invoice.Id);
            }

            await db.SaveChangesAsync();
        });

        return new Scene(studentIds, invoiceIds, amounts);
    }

    /// <summary>To'lovni HTTP orqali qabul qiladi va DTO'ni qaytaradi.</summary>
    private static async Task<PaymentDto> AcceptAsync(
        HttpClient client, Scene scene, int index, string method)
    {
        var amount = scene.Amounts[index];

        var response = await client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId = scene.StudentIds[index],
            amount,
            method,
            allocations = new[] { new { invoiceId = scene.InvoiceIds[index], amount } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PaymentDto>())!;
    }

    private static async Task<CashShiftDto> OpenShiftAsync(HttpClient client, decimal openingFloat = 0m)
    {
        var response = await client.PostAsJsonAsync("/api/cash/shifts/open", new { openingFloat });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashShiftDto>())!;
    }

    // ---------------------------------------------------------------------
    //  HTTP klienti — ILOVANING O'Z DI grafi bilan
    // ---------------------------------------------------------------------
    //
    //  Bu yerda `WithWebHostBuilder` bilan xizmat ULANMAYDI. P1-15 dan keyin
    //  `ICashShiftService`, `IPaymentService` va `ILedgerService` `Program.cs`
    //  da ro'yxatdan o'tgan, ya'ni testda ularni qayta ro'yxatdan o'tkazish
    //  HAQIQIY simni yashirib qo'yardi: kimdir `Program.cs` dan o'sha qatorni
    //  olib tashlasa, test baribir yashil qolaverardi. Endi bunday regressiya
    //  shu yerda 500 bo'lib chiqadi.

    private async Task<(AppUser User, HttpClient Client)> ClientAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        var client = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));
        return (user, client);
    }
}
