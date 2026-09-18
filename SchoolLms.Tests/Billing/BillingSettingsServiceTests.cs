using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

// ===========================================================================
//  MOLIYA SOZLAMALARI — F14.01 (finance-parity.md §2.14.3)
// ===========================================================================
//
//  QAMROV
//  ------
//   1) HTTP darvoza: kassir/xodim/o'qituvchi bo'limga umuman kirmaydi — hatto
//      "finance" ruxsat kaliti bilan ham (§2.14.3: "not plain finance").
//   2) Validatsiya SERVERDA: diapazon (1..28), tartib (overdue >= due),
//      manfiy chegara — frontend buni takrorlaydi, lekin himoya bu yerda.
//   3. Ikki qavatli nazorat (SPEC §4.5): chegarani FAQAT direktor o'zgartiradi;
//      admin uni joriy qiymati bilan qaytarib yuborsa (forma shunday yuboradi)
//      — bloklanmaydi. Rad etish ATOMIK: boshqa ikkita maydon ham yozilmaydi.
//   4) Audit: haqiqiy o'zgarish `audit_log` ga tushadi.
//   5) "Oldinga qarab, orqaga emas": chegara o'zgarganda ALLAQACHON yozilgan
//      `expenses` qatoriga JISMONAN tegilmaydi. (Hisob-faktura tomoni —
//      `payment_due_day` global hisoblashni skanerlaydi — ALOHIDA, izolyatsiya
//      qilingan bazada: <see cref="BillingSettingsAccrualTests"/>.)
//
//  Bu yerdagi testlar UMUMIY bazada (fixture.Database) yuradi — xuddi
//  `ExpensesTests` / `ExpenseUiContractTests` kabi: ular ham bitta qatorli
//  `billing_settings` ni o'zgartiradi va MAJBURAN qaytaradi. Xavfsiz, chunki
//  hech biri butun bazani skanerlamaydi (faqat o'z ID'lari bo'yicha o'qiydi).
// ===========================================================================
[Collection(SchoolLmsCollection.Name)]
public class BillingSettingsServiceTests(ApiFixture fixture)
{
    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    private static BillingSettingsService Service(AppDbContext db) =>
        new(db, new AuditService(db, new HttpContextAccessor()));

    private static async Task<string> AddUserAsync(AppDbContext db, string role)
    {
        var user = new AppUser
        {
            FullName = $"F14.01 {role}",
            Role = role,
            Email = $"{role}.f1401.{Guid.NewGuid():N}",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Testdan keyin sozlamani ANIQ oldingi holatga qaytaradi (yangi DbContext bilan).</summary>
    private async Task RestoreAsync(BillingSettings original)
    {
        await using var db = NewDb();
        var row = await db.BillingSettings.FirstAsync();
        row.PaymentDueDay = original.PaymentDueDay;
        row.OverdueAfterDay = original.OverdueAfterDay;
        row.ExpenseApprovalThreshold = original.ExpenseApprovalThreshold;
        row.UpdatedAt = original.UpdatedAt;
        row.UpdatedBy = original.UpdatedBy;
        await db.SaveChangesAsync();
    }

    // =================================================================
    //  1. HTTP darvoza (SPEC §4.3 — moliya "orqa ofisi")
    // =================================================================

    /// <summary>
    /// `staff` "finance" ruxsat kalitiga ega bo'lsa ham bu YETARLI EMAS
    /// (finance-parity.md §2.14.3: "not plain finance"). Darvoza klass
    /// darajasidagi <c>[Authorize(Roles = Roles.FinanceStaff)]</c> — faqat
    /// admin va direktor. Bu filtr kontroller QURILISHIDAN OLDIN ishlaydi,
    /// shuning uchun test `IBillingSettingsService` DI'da hali ro'yxatdan
    /// o'tmagan bo'lsa ham to'g'ri natija beradi.
    /// </summary>
    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Cashier)]
    [InlineData(Roles.Teacher)]
    public async Task Settings_endpointi_admin_va_direktordan_boshqasiga_yopiq(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "finance");

        var getResponse = await client.GetAsync("/api/admin/billing/settings");
        Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);

        var putResponse = await client.PutAsJsonAsync(
            "/api/admin/billing/settings",
            new { paymentDueDay = 10, overdueAfterDay = 15, expenseApprovalThreshold = 5_000_000m });
        Assert.Equal(HttpStatusCode.Forbidden, putResponse.StatusCode);
    }

    [Fact]
    public async Task Settings_endpointi_anonim_foydalanuvchiga_401()
    {
        using var client = fixture.Api.AnonymousClient();

        var response = await client.GetAsync("/api/admin/billing/settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Direktor HAQIQIY HTTP so'rovi bilan sozlamani o'qiydi va yangilaydi.
    /// Bu <c>BillingCatalogController</c> haqiqatan quriladi degan ma'noni
    /// anglatadi — <see cref="BillingCatalogController.Settings"/> DI orqali
    /// emas, <c>db</c>/<c>audit</c> dan qo'lda qurilgani shu testda tasdiqlanadi
    /// (fayl boshidagi izoh: konstruktorga qo'shilmagan qasddan).
    /// </summary>
    [Fact]
    public async Task Settings_HTTP_orqali_oqiladi_va_yangilanadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);

        await using var db = NewDb();
        var original = await db.BillingSettings.AsNoTracking().FirstAsync();
        var newDueDay = original.PaymentDueDay == 5 ? 6 : 5;

        try
        {
            var getResponse = await client.GetAsync("/api/admin/billing/settings");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            var current = await getResponse.Content.ReadFromJsonAsync<BillingSettingsDto>();
            Assert.Equal(original.PaymentDueDay, current!.PaymentDueDay);

            var putResponse = await client.PutAsJsonAsync("/api/admin/billing/settings", new
            {
                paymentDueDay = newDueDay,
                overdueAfterDay = original.OverdueAfterDay,
                expenseApprovalThreshold = original.ExpenseApprovalThreshold,
            });
            Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
            var updated = await putResponse.Content.ReadFromJsonAsync<BillingSettingsDto>();
            Assert.Equal(newDueDay, updated!.PaymentDueDay);
        }
        finally
        {
            await RestoreAsync(original);
        }
    }

    /// <summary>
    /// Admin HTTP orqali chegarani o'zgartirmoqchi bo'lsa — 403 va aniq kod
    /// (<c>threshold_requires_director</c>), forma bilan bir xil tana bilan.
    /// </summary>
    [Fact]
    public async Task Settings_HTTP_admin_chegarani_ozgartirsa_403()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        await using var db = NewDb();
        var original = await db.BillingSettings.AsNoTracking().FirstAsync();
        var newThreshold = original.ExpenseApprovalThreshold + 1_000m;

        var putResponse = await client.PutAsJsonAsync("/api/admin/billing/settings", new
        {
            paymentDueDay = original.PaymentDueDay,
            overdueAfterDay = original.OverdueAfterDay,
            expenseApprovalThreshold = newThreshold,
        });

        Assert.Equal(HttpStatusCode.Forbidden, putResponse.StatusCode);
        var body = await putResponse.Content.ReadFromJsonAsync<BillingErrorDto>();
        Assert.Equal("threshold_requires_director", body!.Code);

        await using var check = NewDb();
        var after = await check.BillingSettings.AsNoTracking().FirstAsync();
        Assert.Equal(original.ExpenseApprovalThreshold, after.ExpenseApprovalThreshold);
    }

    // =================================================================
    //  2. GetAsync — joriy qatorni DTO'ga aylantiradi
    // =================================================================

    [Fact]
    public async Task GetAsync_joriy_qatorni_qaytaradi()
    {
        await using var reference = NewDb();
        var row = await reference.BillingSettings.AsNoTracking().FirstAsync();

        await using var db = NewDb();
        var dto = await Service(db).GetAsync();

        Assert.Equal(row.PaymentDueDay, dto.PaymentDueDay);
        Assert.Equal(row.OverdueAfterDay, dto.OverdueAfterDay);
        Assert.Equal(row.ExpenseApprovalThreshold, dto.ExpenseApprovalThreshold);
        Assert.Equal(row.UpdatedAt, dto.UpdatedAt);
    }

    // =================================================================
    //  3. Validatsiya SERVERDA (§2.14.3: "not only a form validator")
    // =================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(-1)]
    public async Task UpdateAsync_tolov_muddati_diapazondan_tashqari_rad_etiladi(int badDay)
    {
        await using var db = NewDb();
        var directorId = await AddUserAsync(db, Roles.SuperAdmin);
        var original = await db.BillingSettings.AsNoTracking().FirstAsync();

        var ex = await Assert.ThrowsAsync<BillingRuleException>(() => Service(db).UpdateAsync(
            new UpdateBillingSettingsRequest(badDay, 28, original.ExpenseApprovalThreshold),
            directorId, actorIsDirector: true));

        Assert.Equal(BillingFault.Invalid, ex.Fault);
        Assert.Equal("due_day_range", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public async Task UpdateAsync_muddati_otgan_kun_diapazondan_tashqari_rad_etiladi(int badDay)
    {
        await using var db = NewDb();
        var directorId = await AddUserAsync(db, Roles.SuperAdmin);
        var original = await db.BillingSettings.AsNoTracking().FirstAsync();

        var ex = await Assert.ThrowsAsync<BillingRuleException>(() => Service(db).UpdateAsync(
            new UpdateBillingSettingsRequest(1, badDay, original.ExpenseApprovalThreshold),
            directorId, actorIsDirector: true));

        Assert.Equal(BillingFault.Invalid, ex.Fault);
        Assert.Equal("overdue_day_range", ex.Code);
    }

    [Fact]
    public async Task UpdateAsync_muddati_otgan_kun_tolov_kunidan_kichik_rad_etiladi()
    {
        await using var db = NewDb();
        var directorId = await AddUserAsync(db, Roles.SuperAdmin);
        var original = await db.BillingSettings.AsNoTracking().FirstAsync();

        var ex = await Assert.ThrowsAsync<BillingRuleException>(() => Service(db).UpdateAsync(
            new UpdateBillingSettingsRequest(15, 10, original.ExpenseApprovalThreshold),
            directorId, actorIsDirector: true));

        Assert.Equal(BillingFault.Invalid, ex.Fault);
        Assert.Equal("overdue_before_due", ex.Code);
    }

    [Fact]
    public async Task UpdateAsync_manfiy_chegara_rad_etiladi()
    {
        await using var db = NewDb();
        var directorId = await AddUserAsync(db, Roles.SuperAdmin);
        var original = await db.BillingSettings.AsNoTracking().FirstAsync();

        var ex = await Assert.ThrowsAsync<BillingRuleException>(() => Service(db).UpdateAsync(
            new UpdateBillingSettingsRequest(original.PaymentDueDay, original.OverdueAfterDay, -1m),
            directorId, actorIsDirector: true));

        Assert.Equal(BillingFault.Invalid, ex.Fault);
        Assert.Equal("negative_threshold", ex.Code);
    }

    // =================================================================
    //  4. Ikki qavatli nazorat — chegara faqat direktorga (SPEC §4.5, F14.01)
    // =================================================================

    /// <summary>
    /// Admin (direktor emas) chegarani HAQIQATAN o'zgartirmoqchi bo'lsa — 403
    /// <c>threshold_requires_director</c>, VA hech narsa yozilmaydi: to'lov
    /// kuni ham, chegara ham bazada eskicha qoladi (rad etish ATOMIK — qisman
    /// yozuv yo'q, garchi ikkala maydon controller'dan bitta so'rovda kelsa ham).
    /// </summary>
    [Fact]
    public async Task UpdateAsync_admin_chegarani_ozgartira_olmaydi()
    {
        await using var db = NewDb();
        var adminId = await AddUserAsync(db, Roles.Admin);
        var original = await db.BillingSettings.AsNoTracking().FirstAsync();
        var newThreshold = original.ExpenseApprovalThreshold + 1_000m;
        var newDueDay = original.PaymentDueDay == 5 ? 6 : 5;

        var ex = await Assert.ThrowsAsync<BillingRuleException>(() => Service(db).UpdateAsync(
            new UpdateBillingSettingsRequest(newDueDay, 28, newThreshold), adminId, actorIsDirector: false));

        Assert.Equal(BillingFault.Forbidden, ex.Fault);
        Assert.Equal("threshold_requires_director", ex.Code);

        await using var check = NewDb();
        var after = await check.BillingSettings.AsNoTracking().FirstAsync();
        Assert.Equal(original.PaymentDueDay, after.PaymentDueDay);
        Assert.Equal(original.OverdueAfterDay, after.OverdueAfterDay);
        Assert.Equal(original.ExpenseApprovalThreshold, after.ExpenseApprovalThreshold);
    }

    /// <summary>
    /// Admin chegarani JORIY qiymati bilan qaytarib yuborsa (forma HAR DOIM
    /// shunday yuboradi — <c>billingCatalog.ts</c> ning <c>BillingSettingsInput</c>
    /// izohi) — direktorlik talab qilinmaydi, qolgan ikki maydon bemalol
    /// o'zgaradi.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_admin_chegarani_ozgarishsiz_qoldirsa_royxatdan_otadi()
    {
        await using var db = NewDb();
        var adminId = await AddUserAsync(db, Roles.Admin);
        var original = await db.BillingSettings.AsNoTracking().FirstAsync();
        var newDueDay = original.PaymentDueDay == 5 ? 6 : 5;
        var newOverdueDay = Math.Max(newDueDay, original.OverdueAfterDay == 20 ? 21 : 20);

        try
        {
            var dto = await Service(db).UpdateAsync(
                new UpdateBillingSettingsRequest(newDueDay, newOverdueDay, original.ExpenseApprovalThreshold),
                adminId, actorIsDirector: false);

            Assert.Equal(newDueDay, dto.PaymentDueDay);
            Assert.Equal(newOverdueDay, dto.OverdueAfterDay);
            Assert.Equal(original.ExpenseApprovalThreshold, dto.ExpenseApprovalThreshold);
        }
        finally
        {
            await RestoreAsync(original);
        }
    }

    /// <summary>Direktor chegarani o'zgartira oladi va o'zgarish audit jurnaliga tushadi.</summary>
    [Fact]
    public async Task UpdateAsync_direktor_chegarani_ozgartiradi_va_audit_yozadi()
    {
        await using var db = NewDb();
        var directorId = await AddUserAsync(db, Roles.SuperAdmin);
        var original = await db.BillingSettings.AsNoTracking().FirstAsync();
        var newThreshold = original.ExpenseApprovalThreshold + 500_000m;

        try
        {
            var dto = await Service(db).UpdateAsync(
                new UpdateBillingSettingsRequest(original.PaymentDueDay, original.OverdueAfterDay, newThreshold),
                directorId, actorIsDirector: true);

            Assert.Equal(newThreshold, dto.ExpenseApprovalThreshold);

            await using var check = NewDb();
            var row = await check.BillingSettings.AsNoTracking().FirstAsync();
            Assert.Equal(newThreshold, row.ExpenseApprovalThreshold);
            Assert.Equal(directorId, row.UpdatedBy);

            // Eslatma: `AuditService.Record` aktyorni HTTP kontekstidan oladi
            // (`IHttpContextAccessor`), `UpdateAsync` ga parametr sifatida
            // kelgan `actorId` esa faqat `settings.UpdatedBy` ga yoziladi (yuqorida
            // tekshirildi) — shu sabab bu yerda jurnal yozuvi `ActorId` emas,
            // MATN va turi bo'yicha topiladi (bu testda haqiqiy so'rov konteksti yo'q).
            //
            // `Timestamp` SONIYA aniqligida ("yyyy-MM-ddTHH:mm:ss") — umumiy bazada
            // shu klassning boshqa testlari HAM shu soniyada o'z audit yozuvini
            // qo'shishi mumkin, ya'ni "eng oxirgisi" tartiblash NOANIQ bo'lardi.
            // Shuning uchun ORDER BY o'rniga MATNNING O'ZI bo'yicha qidiramiz —
            // yangi chegara qiymati (5 000 000 dan farqli, testga xos) Summary'da
            // faqat SHU yozuvda bo'lishi kerak.
            var expectedMoney = AuditService.Money(newThreshold);
            var log = await check.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "BillingSettings" && a.Action == "update"
                    && a.Summary.Contains(expectedMoney))
                .FirstOrDefaultAsync();
            Assert.NotNull(log);
        }
        finally
        {
            await RestoreAsync(original);
        }
    }

    // =================================================================
    //  5. Oldinga qarab — orqaga emas: `expenses` jadvaliga tegmaydi
    // =================================================================

    /// <summary>
    /// Chegara o'zgarganda ALLAQACHON yozilgan chiqim qatoriga JISMONAN
    /// tegilmaydi — xizmat FAQAT <c>billing_settings</c> ni yangilaydi
    /// (<c>BillingSettingsService.cs</c> fayl boshidagi "OLDINGA QARAB —
    /// ORQAGA EMAS" izohi). Bu yerda o'zgarish yo'nalishi ataylab TESKARI
    /// (chegara PASAYTIRILADI) — agar xizmat chiqimlarga qandaydir "qayta
    /// baholash" o'tkazsa, aynan shu holatda eski chiqim "tasdiq talab
    /// qiladi" holatiga o'tib qolardi.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_expenses_jadvaliga_tegmaydi()
    {
        await using var seedDb = NewDb();
        var userId = await AddUserAsync(seedDb, Roles.Admin);
        var directorId = await AddUserAsync(seedDb, Roles.SuperAdmin);

        var expense = new Expense
        {
            OnDate = AppClock.Today,
            Category = "other",
            Amount = 123_456m,
            Note = "F14.01 forward-only tekshiruvi",
            CreatedBy = userId,
            CreatedAt = AppClock.NowInstant,
        };
        seedDb.Expenses.Add(expense);
        await seedDb.SaveChangesAsync();

        // Bazadan QAYTA o'qib olamiz (xotiradagi ob'ekt bilan emas): `timestamptz`
        // mikrosekund aniqligida saqlaydi, .NET esa tikda (100 ns) — to'g'ridan-to'g'ri
        // yozilgan qiymat bilan solishtirish INSERT'ning o'zidayoq soxta farq berardi.
        var before = await seedDb.Expenses.AsNoTracking().SingleAsync(e => e.Id == expense.Id);

        var original = await seedDb.BillingSettings.AsNoTracking().FirstAsync();
        // Ataylab PASAYTIRILADI — eski chiqim endi "chegaradan yuqori" bo'lib qolishi
        // mumkin bo'lgan yo'nalish, aynan shu holatda forward-only buzilishi ko'rinardi.
        var lowerThreshold = Math.Max(0m, expense.Amount - 1_000m);

        try
        {
            await using var settingsDb = NewDb();
            await Service(settingsDb).UpdateAsync(
                new UpdateBillingSettingsRequest(original.PaymentDueDay, original.OverdueAfterDay, lowerThreshold),
                directorId, actorIsDirector: true);

            await using var check = NewDb();
            var reloaded = await check.Expenses.AsNoTracking().SingleAsync(e => e.Id == expense.Id);
            Assert.Equal(before.OnDate, reloaded.OnDate);
            Assert.Equal(before.Category, reloaded.Category);
            Assert.Equal(before.Amount, reloaded.Amount);
            Assert.Equal(before.Note, reloaded.Note);
            Assert.Equal(before.CreatedBy, reloaded.CreatedBy);
            Assert.Equal(before.CreatedAt, reloaded.CreatedAt);
            Assert.Null(reloaded.ApprovedBy);
        }
        finally
        {
            await RestoreAsync(original);
        }
    }
}
