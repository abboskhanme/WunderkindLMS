using System.Net;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// <c>cashier</c> roli (P1-04) va SPEC §4.3 rol matritsasi (P1-06).
///
/// <para>
/// Bu testlar HTTP endpoint'larni tekshirmaydi — moliya endpoint'lari hali
/// yozilmagan (Faza 1.C). Ular ikkita boshqa narsani qoplaydi: (1) yangi rol
/// token bekor qilish mexanizmiga haqiqatan ulanganmi; (2) §4.3 jadvali
/// MA'LUMOT sifatida to'g'ri yozilganmi. To'liq RBAC matritsasi haqiqiy
/// so'rovlar bilan P1-22 da sinaladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class CashierRoleTests(ApiFixture fixture)
{
    // -----------------------------------------------------------------
    //  Token bekor qilish (P1-04, Program.cs OnTokenValidated)
    // -----------------------------------------------------------------

    /// <summary>
    /// ENG MUHIM TEST SHU FAYLDA. <c>cashier</c> P1-04 gacha
    /// <c>OnTokenValidated</c> dagi <c>else { blocked = false; }</c> tarmog'iga
    /// tushardi — ya'ni O'CHIRILGAN kassir eski tokeni bilan pul qabul qilishda
    /// davom etardi. Imzo va muddat to'g'ri bo'lsa ham, bazada qatori yo'q
    /// kassir 401 olishi SHART.
    /// </summary>
    [Fact]
    public async Task Bazada_qatori_yoq_kassir_tokeni_401()
    {
        var token = fixture.Api.TokenFor(Roles.Cashier, "yoq-" + Guid.NewGuid().ToString("N"));
        using var client = fixture.Api.ClientWithToken(token);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Bazada qatori BOR kassir esa normal ishlashda davom etadi.</summary>
    [Fact]
    public async Task Haqiqiy_kassir_tokeni_ishlaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"role\":\"cashier\"", body);
    }

    // -----------------------------------------------------------------
    //  SPEC §4.3 matritsasi (P1-06)
    // -----------------------------------------------------------------

    /// <summary>
    /// Har bir <see cref="FinanceAction"/> uchun AYNAN bitta qoida bo'lishi shart.
    /// Yangi amal qo'shib, qoidasini yozishni unutgan odam shu yerda tutiladi —
    /// aks holda u jimgina "hech kimga mumkin emas" bo'lib qolardi (yopiq eshik,
    /// lekin sababi tushunarsiz).
    /// </summary>
    [Fact]
    public void Har_amal_uchun_aynan_bitta_qoida_bor()
    {
        foreach (var action in Enum.GetValues<FinanceAction>())
            Assert.Single(FinanceMatrix.Rules, r => r.Action == action);

        Assert.Equal(Enum.GetValues<FinanceAction>().Length, FinanceMatrix.Rules.Count);
    }

    /// <summary>Qoidalardagi rollar haqiqiy <see cref="Roles"/> qiymatlari bo'lsin (matn xatosi bo'lmasin).</summary>
    [Fact]
    public void Qoidalardagi_rollar_haqiqiy_rollar()
    {
        string[] known = [Roles.SuperAdmin, Roles.Admin, Roles.Cashier, Roles.Staff, Roles.Teacher, Roles.Student,
            Roles.FinanceDelegate];

        foreach (var rule in FinanceMatrix.Rules)
            foreach (var role in rule.Roles)
                Assert.Contains(role, known);
    }

    /// <summary>
    /// SPEC §4.3: "Edit or delete a payment — impossible for anyone".
    /// Bo'sh ro'yxat shu qoidani AYTIB turadi.
    /// </summary>
    [Fact]
    public void Tolovni_tahrirlash_hech_kimga_mumkin_emas()
    {
        Assert.Empty(FinanceMatrix.RolesFor(FinanceAction.EditOrDeletePayment));

        foreach (var role in new[] { Roles.SuperAdmin, Roles.Admin, Roles.Cashier, Roles.Staff })
            Assert.False(FinanceMatrix.IsAllowed(FinanceAction.EditOrDeletePayment, role));
    }

    /// <summary>
    /// SPEC §4.3 jadvalining kassir ustuni, TO'LIQ. Kassir faqat ikki narsani
    /// qila oladi: to'lov qabul qilish va o'z smenasini boshqarish.
    /// Qolgan HAMMA amal unga yopiq.
    /// </summary>
    [Theory]
    [InlineData(FinanceAction.AcceptPayment, true)]
    [InlineData(FinanceAction.ManageOwnShift, true)]
    [InlineData(FinanceAction.ReversePayment, false)]
    [InlineData(FinanceAction.EditOrDeletePayment, false)]
    [InlineData(FinanceAction.ManageSubscriptions, false)]
    [InlineData(FinanceAction.GrantDiscount, false)]
    [InlineData(FinanceAction.ApproveDiscount, false)]
    [InlineData(FinanceAction.ViewVarianceReport, false)]
    [InlineData(FinanceAction.RecordExpense, false)]
    [InlineData(FinanceAction.ApproveExpense, false)]
    [InlineData(FinanceAction.ViewBillingReports, false)]
    [InlineData(FinanceAction.ManageBillingSettings, false)]
    public void Kassir_ustuni_SPEC_43_ga_mos(FinanceAction action, bool allowed) =>
        Assert.Equal(allowed, FinanceMatrix.IsAllowed(action, Roles.Cashier));

    /// <summary>
    /// Chegirmani tasdiqlash — FAQAT direktor (mijoz javobi, SPEC §8.1 Q5:
    /// chegara yo'q, har qanday chegirma direktor tasdig'ini talab qiladi).
    /// Admin chegirma SO'RAY oladi, lekin TASDIQLAY olmaydi.
    /// </summary>
    [Fact]
    public void Chegirmani_faqat_direktor_tasdiqlaydi()
    {
        Assert.True(FinanceMatrix.IsAllowed(FinanceAction.GrantDiscount, Roles.Admin));
        Assert.False(FinanceMatrix.IsAllowed(FinanceAction.ApproveDiscount, Roles.Admin));
        Assert.True(FinanceMatrix.IsAllowed(FinanceAction.ApproveDiscount, Roles.SuperAdmin));
    }

    /// <summary>
    /// <c>staff</c> yangi moliya yuzasiga UMUMAN kirmaydi: SPEC §4.3 da bunday
    /// ustun yo'q. Eski xodim-ruxsat yo'li P1-21 gacha eski
    /// <c>FinanceController</c> da ishlashda davom etadi.
    /// </summary>
    [Fact]
    public void Staff_yangi_moliya_matritsasida_yoq()
    {
        foreach (var action in Enum.GetValues<FinanceAction>())
            Assert.False(FinanceMatrix.IsAllowed(action, Roles.Staff));

        Assert.DoesNotContain(Roles.Staff, Roles.FinanceStaff.Split(','));
    }

    /// <summary>Qoidasi yo'q amal uchun javob RAD ETISH bo'lishi kerak (fail-closed).</summary>
    [Fact]
    public void Nomalum_amal_rad_etiladi()
    {
        var notInTable = (FinanceAction)9999;

        Assert.Empty(FinanceMatrix.RolesFor(notInTable));
        Assert.False(FinanceMatrix.IsAllowed(notInTable, Roles.SuperAdmin));
    }
}
