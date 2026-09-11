using System.Net;
using System.Text.Json;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Pul aylanmasi grafi — <c>GET /api/admin/finance/money-flow</c> (P1-26).
///
/// <para>
/// <b>Bu yerdagi asosiy tekshiruv — BALANS.</b> Kiruvchi bog'lanishlar
/// yig'indisi = markaziy tugun qiymati = chiquvchi bog'lanishlar yig'indisi,
/// TIYINGACHA. Farq chiqsa bu "yaxlitlash" emas: halqa ekranda chiroyli
/// ko'rinib turib, noto'g'ri raqam ko'rsatayotgan bo'ladi — moliyadagi eng
/// yomon xato turi, chunki uni hech kim sezmaydi.
/// </para>
/// <para>
/// Sof funksiya (<see cref="MoneyFlowQueries.Compose"/>) bazasiz sinaladi —
/// shuning uchun noqulay summalarni (tiyinli, ko'p sonli) erkin tanlash
/// mumkin. Endpoint esa haqiqiy jurnal qatorlari bilan, HTTP orqali.
/// </para>
/// <para>
/// Testlar bitta umumiy bazani bo'lishadi, shuning uchun integratsiya testlari
/// <b>2001-yil</b> sanalarida ishlaydi: boshqa testlar (LedgerServiceTests)
/// bugungi sana bilan yozadi, davrlar kesishmaydi va natija barqaror bo'ladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class MoneyFlowTests(ApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    // =================================================================
    //  1. Sof arifmetika — bazasiz
    // =================================================================

    /// <summary>
    /// QABUL MEZONI. Noqulay, tiyinli summalarda ham kirim = markaz = chiqim.
    /// Raqamlar ataylab "chiroyli emas": 1 234 567.89 kabi qiymatlar
    /// <c>double</c> ishlatilgan joyda darrov farq chiqaradi.
    /// </summary>
    [Fact]
    public void Balans_tiyingacha_togri_keladi()
    {
        var flow = MoneyFlowQueries.Compose(
        [
            new(Accounts.RevenueTuition, LedgerDirection.Credit, 1_234_567.89m),
            new(Accounts.RevenueBus, LedgerDirection.Credit, 333_333.33m),
            new(Accounts.RevenueMeals, LedgerDirection.Credit, 77.77m),
            new(Accounts.ExpenseSalary, LedgerDirection.Debit, 999_999.99m),
            new(Accounts.ExpenseOther, LedgerDirection.Debit, 12_345.67m),
        ]);

        AssertBalanced(flow);

        var hub = Single(flow, MoneyFlowKind.Hub);
        Assert.Equal(1_234_567.89m + 333_333.33m + 77.77m, hub.Value);

        var net = Single(flow, MoneyFlowKind.Net);
        Assert.Equal("Sof foyda", net.Label);
        Assert.Equal(
            1_234_567.89m + 333_333.33m + 77.77m - 999_999.99m - 12_345.67m,
            net.Value);
    }

    /// <summary>
    /// Chiqim kirimdan katta bo'lsa, farq KIRUVCHI tomonga qo'yiladi
    /// ("qoplangan farq") — aks holda chiquvchi yig'indi markazdan katta
    /// bo'lib, halqa mantiqan buzilardi.
    /// </summary>
    [Fact]
    public void Kamomadda_farq_kiruvchi_tomonga_qoyiladi()
    {
        var flow = MoneyFlowQueries.Compose(
        [
            new(Accounts.RevenueTuition, LedgerDirection.Credit, 100_000.00m),
            new(Accounts.ExpenseSalary, LedgerDirection.Debit, 150_000.50m),
        ]);

        AssertBalanced(flow);

        var net = Single(flow, MoneyFlowKind.Net);
        Assert.Equal("Qoplangan farq", net.Label);
        Assert.Equal(50_000.50m, net.Value);

        // Yo'nalish: net -> hub (kiruvchi), teskarisi emas.
        Assert.Contains(flow.Links, l => l.Source == MoneyFlowQueries.NetId && l.Target == MoneyFlowQueries.HubId);
        Assert.DoesNotContain(flow.Links, l => l.Source == MoneyFlowQueries.HubId && l.Target == MoneyFlowQueries.NetId);

        Assert.Equal(150_000.50m, Single(flow, MoneyFlowKind.Hub).Value);
    }

    /// <summary>Kirim = chiqim bo'lsa "sof natija" tuguni umuman bo'lmaydi.</summary>
    [Fact]
    public void Kirim_chiqimga_teng_bolsa_net_tuguni_yoq()
    {
        var flow = MoneyFlowQueries.Compose(
        [
            new(Accounts.RevenueTuition, LedgerDirection.Credit, 400_000m),
            new(Accounts.ExpenseSalary, LedgerDirection.Debit, 400_000m),
        ]);

        AssertBalanced(flow);
        Assert.DoesNotContain(flow.Nodes, n => n.Kind == MoneyFlowKind.Net);
    }

    /// <summary>
    /// <c>cash</c>, <c>bank</c> va <c>receivable</c> — daromad/chiqimning
    /// ikkinchi oyog'i. Halqaga tushsa, bir xil pul ikki marta ko'rinardi.
    /// </summary>
    [Fact]
    public void Cash_bank_receivable_halqaga_tushmaydi()
    {
        var flow = MoneyFlowQueries.Compose(
        [
            new(Accounts.RevenueTuition, LedgerDirection.Credit, 500_000m),
            new(Accounts.Receivable, LedgerDirection.Debit, 500_000m),
            new(Accounts.Cash, LedgerDirection.Debit, 500_000m),
            new(Accounts.Bank, LedgerDirection.Debit, 500_000m),
        ]);

        AssertBalanced(flow);

        foreach (var account in new[] { Accounts.Cash, Accounts.Bank, Accounts.Receivable })
            Assert.DoesNotContain(flow.Nodes, n => n.Id == account);

        Assert.Equal(500_000m, Single(flow, MoneyFlowKind.Hub).Value);
    }

    /// <summary>
    /// Storno (teskari qator) daromadni KAMAYTIRADI, yangi tugun yaratmaydi —
    /// va balans baribir saqlanadi.
    /// </summary>
    [Fact]
    public void Storno_daromadni_kamaytiradi()
    {
        var flow = MoneyFlowQueries.Compose(
        [
            new(Accounts.RevenueTuition, LedgerDirection.Credit, 900_000m),
            new(Accounts.RevenueTuition, LedgerDirection.Debit, 150_000m),
        ]);

        AssertBalanced(flow);
        Assert.Equal(750_000m, Single(flow, MoneyFlowKind.Income).Value);
    }

    /// <summary>Nol qiymatli hisob halqani axlatlantirmaydi.</summary>
    [Fact]
    public void Nolga_teng_hisob_tugun_bermaydi()
    {
        var flow = MoneyFlowQueries.Compose(
        [
            new(Accounts.RevenueTuition, LedgerDirection.Credit, 200_000m),
            new(Accounts.RevenueBus, LedgerDirection.Credit, 50_000m),
            new(Accounts.RevenueBus, LedgerDirection.Debit, 50_000m),
        ]);

        AssertBalanced(flow);
        Assert.DoesNotContain(flow.Nodes, n => n.Id == Accounts.RevenueBus);
    }

    /// <summary>Ma'lumot yo'q davr — bo'sh graf (frontend "bo'sh holat" ko'rsatadi).</summary>
    [Fact]
    public void Bosh_davr_bosh_graf_beradi()
    {
        var flow = MoneyFlowQueries.Compose([]);

        Assert.Empty(flow.Nodes);
        Assert.Empty(flow.Links);
    }

    /// <summary>Har bir bog'lanishning ikkala uchi ham mavjud tugun bo'lishi shart.</summary>
    [Fact]
    public void Har_boglanish_mavjud_tugunlarga_ishora_qiladi()
    {
        var flow = MoneyFlowQueries.Compose(
        [
            new(Accounts.RevenueTuition, LedgerDirection.Credit, 800_000m),
            new(Accounts.RevenueDormitory, LedgerDirection.Credit, 120_000m),
            new(Accounts.ExpenseOther, LedgerDirection.Debit, 45_000m),
        ]);

        var ids = flow.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var link in flow.Links)
        {
            Assert.Contains(link.Source, ids);
            Assert.Contains(link.Target, ids);
        }
    }

    // =================================================================
    //  2. Endpoint — haqiqiy jurnal qatorlari bilan
    // =================================================================

    /// <summary>
    /// QABUL MEZONI. Jurnalga haqiqiy (balanslashgan) partiyalar yozilgach,
    /// endpoint qaytargan graf ham tiyingacha balanslashgan bo'ladi.
    /// </summary>
    [Fact]
    public async Task Endpoint_haqiqiy_jurnalda_balanslashgan_graf_qaytaradi()
    {
        var (actor, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var period = new DateOnly(2001, 3, 15);

        await fixture.Api.WithDbAsync(async db =>
        {
            var ledger = new LedgerService(db);

            // Hisob-faktura: debitorlik oshadi, daromad yoziladi.
            // Partiyaning hamma satri BIR XIL RefId ga ega bo'lishi shart (LedgerService qoidasi).
            var tuitionRef = Guid.NewGuid();
            await ledger.PostAsync(
            [
                new(Accounts.Receivable, LedgerDirection.Debit, 1_500_000.25m, LedgerRefType.Invoice, tuitionRef, period),
                new(Accounts.RevenueTuition, LedgerDirection.Credit, 1_500_000.25m, LedgerRefType.Invoice, tuitionRef, period),
            ], actor.Id);

            // Avtobus obunasi.
            var busRef = Guid.NewGuid();
            await ledger.PostAsync(
            [
                new(Accounts.Receivable, LedgerDirection.Debit, 220_000.75m, LedgerRefType.Invoice, busRef, period),
                new(Accounts.RevenueBus, LedgerDirection.Credit, 220_000.75m, LedgerRefType.Invoice, busRef, period),
            ], actor.Id);

            // Maosh to'landi: chiqim oshadi, kassa kamayadi.
            var salaryRef = Guid.NewGuid();
            await ledger.PostAsync(
            [
                new(Accounts.ExpenseSalary, LedgerDirection.Debit, 990_000.10m, LedgerRefType.Salary, salaryRef, period),
                new(Accounts.Cash, LedgerDirection.Credit, 990_000.10m, LedgerRefType.Salary, salaryRef, period),
            ], actor.Id);
        });

        var flow = await GetFlowAsync("2001-01-01", "2001-12-31");

        AssertBalanced(flow);
        Assert.Equal(1_720_001.00m, Single(flow, MoneyFlowKind.Hub).Value);
        Assert.Equal(730_000.90m, Single(flow, MoneyFlowKind.Net).Value);

        // `cash` va `receivable` grafga tushmagan bo'lsin.
        Assert.DoesNotContain(flow.Nodes, n => n.Id == Accounts.Cash || n.Id == Accounts.Receivable);
    }

    /// <summary>Davrdan tashqaridagi qatorlar hisobga olinmaydi.</summary>
    [Fact]
    public async Task Davr_filtri_ishlaydi()
    {
        var (actor, _) = await fixture.Api.SeedUserAsync(Roles.Admin);

        await fixture.Api.WithDbAsync(async db =>
        {
            var ledger = new LedgerService(db);
            var mealsRef = Guid.NewGuid();
            var day = new DateOnly(2002, 5, 10);
            await ledger.PostAsync(
            [
                new(Accounts.Receivable, LedgerDirection.Debit, 640_000m, LedgerRefType.Invoice, mealsRef, day),
                new(Accounts.RevenueMeals, LedgerDirection.Credit, 640_000m, LedgerRefType.Invoice, mealsRef, day),
            ], actor.Id);
        });

        var inside = await GetFlowAsync("2002-05-01", "2002-05-31");
        Assert.Contains(inside.Nodes, n => n.Id == Accounts.RevenueMeals);

        var outside = await GetFlowAsync("2002-06-01", "2002-06-30");
        Assert.Empty(outside.Nodes);
        Assert.Empty(outside.Links);
    }

    // =================================================================
    //  3. Ruxsat (SPEC §4.3 — ViewBillingReports)
    // =================================================================

    /// <summary>
    /// Kassir moliya hisobotlarini KO'RMAYDI. Bu shunchaki menyuni yashirish
    /// emas — endpoint ham 403 qaytaradi.
    /// </summary>
    [Fact]
    public async Task Kassir_403_oladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);

        var response = await client.GetAsync("/api/admin/finance/money-flow");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Xodim (staff) ham ko'rmaydi: SPEC §4.3 da bunday ustun yo'q.</summary>
    [Fact]
    public async Task Staff_403_oladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, "finance");

        var response = await client.GetAsync("/api/admin/finance/money-flow");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_direktor_200_oladi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);

        var response = await client.GetAsync("/api/admin/finance/money-flow");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Tokensiz_401_oladi()
    {
        using var client = fixture.Api.AnonymousClient();

        var response = await client.GetAsync("/api/admin/finance/money-flow");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Noto'g'ri sana jimgina "bugun" ga aylanmasin: erkin format moliyada
    /// bir oylik siljish degani, shuning uchun 400.
    /// </summary>
    [Fact]
    public async Task Notogri_sana_400_beradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync("/api/admin/finance/money-flow?from=01.02.2026");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Teskari davr (from > to) — xato, bo'sh natija emas.</summary>
    [Fact]
    public async Task Teskari_davr_400_beradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync("/api/admin/finance/money-flow?from=2026-05-01&to=2026-04-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =================================================================
    //  Yordamchilar
    // =================================================================

    private async Task<MoneyFlowDto> GetFlowAsync(string from, string to)
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync(
            $"/api/admin/finance/money-flow?from={from}&to={to}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<MoneyFlowDto>(body, Json)
               ?? throw new InvalidOperationException("Javob bo'sh keldi.");
    }

    /// <summary>
    /// Halqaning YAGONA qat'iy qoidasi: kiruvchi = markaz = chiquvchi,
    /// <c>decimal</c> taqqoslash bilan (ya'ni tiyingacha, dopusk yo'q).
    /// </summary>
    private static void AssertBalanced(MoneyFlowDto flow)
    {
        if (flow.Nodes.Count == 0)
        {
            Assert.Empty(flow.Links);
            return;
        }

        var hub = Single(flow, MoneyFlowKind.Hub);

        var incoming = flow.Links.Where(l => l.Target == MoneyFlowQueries.HubId).Sum(l => l.Value);
        var outgoing = flow.Links.Where(l => l.Source == MoneyFlowQueries.HubId).Sum(l => l.Value);

        Assert.Equal(hub.Value, incoming);
        Assert.Equal(hub.Value, outgoing);

        // Markazdan boshqa hech bir tugun "o'zidan o'ziga" ulanmasin va
        // bog'lanishlar faqat markaz orqali o'tsin.
        Assert.All(flow.Links, l =>
            Assert.True(l.Source == MoneyFlowQueries.HubId || l.Target == MoneyFlowQueries.HubId,
                $"'{l.Source}' -> '{l.Target}' markazni chetlab o'tmoqda."));

        // Har bog'lanish tugun qiymati bilan mos bo'lsin.
        var byId = flow.Nodes.ToDictionary(n => n.Id, n => n.Value, StringComparer.Ordinal);
        Assert.All(flow.Links, l =>
        {
            var leaf = l.Source == MoneyFlowQueries.HubId ? l.Target : l.Source;
            Assert.Equal(byId[leaf], l.Value);
        });
    }

    private static MoneyFlowNode Single(MoneyFlowDto flow, string kind) =>
        Assert.Single(flow.Nodes, n => n.Kind == kind);
}
