using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// To'lov toifalari, obunalar va chegirmalar (P1-08).
///
/// <para>
/// Uch qatlam tekshiriladi:
/// </para>
/// <list type="number">
///   <item><b>Arifmetika</b> — <c>DiscountService.ChargeFor</c> eski
///   <c>TuitionService.ChargeFor</c> bilan AYNAN bir xil natija berishi
///   ("moved, not rewritten"). Bu P1-23 ning oldindan to'lovi.</item>
///   <item><b>Qoida</b> — ustma-ust obuna 409, chegirma har doim <c>pending</c>,
///   o'zini o'zi tasdiqlash 403, tasdiq faqat direktorda. Har biri ILOVA
///   darajasida, eng muhimi esa BAZA darajasida ham.</item>
///   <item><b>Ruxsat</b> — SPEC §4.3: kassir bu yuzaga umuman kira olmaydi,
///   admin chegirma so'raydi lekin tasdiqlay olmaydi.</item>
/// </list>
///
/// <para>
/// Baza OWNER ulanishi bilan ochiladi (LedgerServiceTests bilan bir xil sabab):
/// test ma'lumotini tayyorlash uchun <c>users</c>/<c>students</c> ga yozish kerak.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class BillingCatalogTests(ApiFixture fixture)
{
    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // F1.06: SubscriptionService endi IInvoiceService ga ham tayanadi
    // (PreviewEndAsync, sof o'qish) — InvoiceService ning haqiqiy nusxasi
    // beriladi, InvoiceServiceTests.ServiceFor bilan bir xil naqsh.
    private static SubscriptionService Subscriptions(AppDbContext db) =>
        new(db, new AuditService(db, new HttpContextAccessor()), new InvoiceService(db, new LedgerService(db)));

    private static DiscountService Discounts(AppDbContext db) =>
        new(db, new AuditService(db, new HttpContextAccessor()));

    // ==================================================================
    //  1. Arifmetika — eski kod bilan bir xil (P1-08 qabul mezoni)
    // ==================================================================

    /// <summary>
    /// Eski <c>TuitionService.ChargeFor</c> va yangi <c>DiscountService.ChargeFor</c>
    /// AYNAN bir xil kirishlarda AYNAN bir xil chiqish berishi shart. Qabul mezoni
    /// "arifmetika KO'CHIRILADI, qayta yozilmaydi" deb turibdi — bu test o'sha
    /// gapni mashinaga tekshirtiradi, ko'z bilan solishtirishga qoldirmaydi.
    /// </summary>
    public static TheoryData<decimal, int, decimal> ChegirmaNamunalari => new()
    {
        // (oylik narx, foiz, aniq summa)
        { 1_500_000m, 0, 0m },            // chegirmasiz
        { 1_500_000m, 10, 0m },           // faqat foiz
        { 1_500_000m, 0, 200_000m },      // faqat summa
        { 1_500_000m, 15, 200_000m },     // ikkalasi — TARTIB muhim (avval foiz)
        { 1_500_000m, 100, 0m },          // to'liq bepul
        { 1_500_000m, 100, 500_000m },    // quyi chegara 0 (manfiy chiqmaydi)
        { 1_500_000m, 120, 0m },          // foiz 100 dan yuqori — qisiladi
        { 1_500_000m, -5, -100m },        // manfiy kirish — qisiladi
        { 0m, 50, 100_000m },             // narx 0
        { -1m, 50, 0m },                  // narx manfiy
        { 333_333m, 33, 77_777m },        // yaxlitlash
        { 1_234_567m, 7, 1_000_000m },    // summa foizdan keyingi qoldiqdan katta
    };

    [Theory]
    [MemberData(nameof(ChegirmaNamunalari))]
    public async Task ChargeFor_eski_TuitionService_bilan_bir_xil(decimal fee, int percent, decimal amount)
    {
        await using var db = NewDb();
        var service = Discounts(db);

        Assert.Equal(
            TuitionService.ChargeFor(fee, percent, amount),
            service.ChargeFor(fee, percent, amount));

        Assert.Equal(
            TuitionService.DiscountFor(fee, percent, amount),
            service.DiscountFor(fee, percent, amount));
    }

    /// <summary>
    /// Tartib: AVVAL foiz, KEYIN aniq summa. Teskari tartib boshqa natija berardi,
    /// shuning uchun uni alohida raqam bilan qotirib qo'yamiz.
    /// </summary>
    [Fact]
    public async Task Avval_foiz_keyin_summa_ayriladi()
    {
        await using var db = NewDb();
        var service = Discounts(db);

        // 1 000 000 − 10% = 900 000; 900 000 − 100 000 = 800 000.
        // (Teskari tartibda: (1 000 000 − 100 000) × 0.9 = 810 000 — boshqa raqam.)
        Assert.Equal(800_000m, service.ChargeFor(1_000_000m, 10m, 100_000m));
        Assert.Equal(200_000m, service.DiscountFor(1_000_000m, 10m, 100_000m));

        // Quyi chegara 0: chegirma narxdan oshsa ham manfiy qarz yozilmaydi.
        Assert.Equal(0m, service.ChargeFor(500_000m, 50m, 900_000m));
        Assert.Equal(500_000m, service.DiscountFor(500_000m, 50m, 900_000m));
    }

    /// <summary>
    /// Yangi ustun <c>numeric(5,2)</c> — kasrli foiz ham yoziladi. Eski <c>int</c>
    /// API buni ifodalay olmaydi, shuning uchun qiymat qo'lda tekshiriladi.
    /// </summary>
    [Fact]
    public async Task Kasrli_foiz_qollab_quvvatlanadi()
    {
        await using var db = NewDb();
        Assert.Equal(875_000m, Discounts(db).ChargeFor(1_000_000m, 12.5m, 0m));
    }

    // ==================================================================
    //  2. Obunalar
    // ==================================================================

    /// <summary>
    /// P1-08 qabul mezoni: o'quvchining BIRINCHI <c>tuition</c> obunasida oylik
    /// summa sinfning <c>MonthlyFee</c> qiymatidan to'ldiriladi (bugungi xulq
    /// bilan uzviylik — hozir oylik AYNAN sinf narxidan olinadi).
    /// </summary>
    [Fact]
    public async Task Birinchi_tuition_obunasi_sinf_narxidan_toldiriladi()
    {
        var actor = await NewUserAsync();
        var student = await NewStudentAsync(classFee: 1_500_000m);
        var tuition = await CategoryAsync(FeeCategoryCodes.Tuition);

        await using var db = NewDb();
        var service = Subscriptions(db);

        // (a) Forma uchun taklif — hech narsa yozilmaydi.
        var suggestion = await service.DefaultAmountAsync(student.Id, tuition.Id);
        Assert.Equal(1_500_000m, suggestion.MonthlyAmount);
        Assert.Equal(SubscriptionDefaultSource.ClassFee, suggestion.Source);

        // (b) Summa ko'rsatilmasa (0) — o'sha taklif YOZILADI.
        var created = await service.CreateAsync(
            new CreateSubscriptionRequest(student.Id, tuition.Id, 0m, null, new DateOnly(2026, 9, 1), null),
            actor);

        Assert.Equal(1_500_000m, created.MonthlyAmount);
        Assert.Equal(FeeCategoryCodes.Tuition, created.CategoryCode);
        Assert.True(created.IsActive);

        // (c) Ikkinchi obunada taklif YO'Q: narx endi obunaning o'zida yashaydi,
        //     sinf narxi o'zgarsa u bilan birga o'zgarmaydi.
        var second = await service.DefaultAmountAsync(student.Id, tuition.Id);
        Assert.Equal(0m, second.MonthlyAmount);
        Assert.Equal(SubscriptionDefaultSource.None, second.Source);
    }

    /// <summary>
    /// Toifaga o'zgarmas narx berilgan bo'lsa (abonement): taklif shu narx, obuna
    /// so'rovdagi summadan qat'i nazar SHU narx bilan ochiladi, keyin summani
    /// qo'lda boshqa qiymatga o'zgartirib bo'lmaydi — faqat toifa narxiga tenglashadi.
    /// </summary>
    [Fact]
    public async Task Narxi_belgilangan_toifada_summa_ozgarmas()
    {
        var actor = await NewUserAsync();
        var student = await NewStudentAsync();

        // Umumiy seed toifalariga narx qo'yilmaydi — parallel testlarga ta'sir qilardi.
        var category = new FeeCategory
        {
            Code = $"club_{Guid.NewGuid():N}"[..20],
            Name = "To'garak",
            MonthlyAmount = 300_000m,
        };
        await using (var seed = NewDb())
        {
            seed.FeeCategories.Add(category);
            await seed.SaveChangesAsync();
        }

        await using var db = NewDb();
        var service = Subscriptions(db);

        var suggestion = await service.DefaultAmountAsync(student.Id, category.Id);
        Assert.Equal(300_000m, suggestion.MonthlyAmount);
        Assert.Equal(SubscriptionDefaultSource.CategoryPrice, suggestion.Source);

        var created = await service.CreateAsync(
            new CreateSubscriptionRequest(student.Id, category.Id, 999_000m, null, new DateOnly(2026, 9, 1), null),
            actor);
        Assert.Equal(300_000m, created.MonthlyAmount);

        var ex = await Assert.ThrowsAsync<BillingRuleException>(() => service.UpdateAsync(
            created.Id, new UpdateSubscriptionRequest(250_000m, null, null), actor));
        Assert.Equal("amount_fixed", ex.Code);
        Assert.Equal(BillingFault.Conflict, ex.Fault);

        // Tafsilot o'zgarishi (summa o'sha-o'sha) — ruxsat.
        var updated = await service.UpdateAsync(
            created.Id, new UpdateSubscriptionRequest(300_000m, "Shaxmat", null), actor);
        Assert.Equal(300_000m, updated.MonthlyAmount);
        Assert.Equal("Shaxmat", updated.Detail);
    }

    /// <summary>Ko'rsatilgan summa har doim ustun: sinf narxi uni bosib ketmaydi.</summary>
    [Fact]
    public async Task Korsatilgan_summa_sinf_narxidan_ustun()
    {
        var actor = await NewUserAsync();
        var student = await NewStudentAsync(classFee: 1_500_000m);
        var bus = await CategoryAsync(FeeCategoryCodes.Bus);

        await using var db = NewDb();
        var created = await Subscriptions(db).CreateAsync(
            new CreateSubscriptionRequest(student.Id, bus.Id, 400_000m, "3-yo'nalish", new DateOnly(2026, 9, 1), null),
            actor);

        Assert.Equal(400_000m, created.MonthlyAmount);
        Assert.Equal("3-yo'nalish", created.Detail);
    }

    /// <summary>
    /// P1-08 qabul mezoni: bir xil (o'quvchi, toifa) uchun davri KESISHADIGAN
    /// ikkinchi obuna — 409. Aks holda accrual bir oyga ikkita narx ko'rib qolardi.
    /// </summary>
    [Fact]
    public async Task Davri_kesishadigan_ikkinchi_obuna_rad_etiladi()
    {
        var actor = await NewUserAsync();
        var student = await NewStudentAsync();
        var bus = await CategoryAsync(FeeCategoryCodes.Bus);

        await using var db = NewDb();
        var service = Subscriptions(db);

        await service.CreateAsync(
            new CreateSubscriptionRequest(student.Id, bus.Id, 400_000m, null, new DateOnly(2026, 9, 1), null),
            actor);

        // Ochiq (ends_on = null) obuna ustiga keyingi sanadan boshlanadigan yangisi.
        var ex = await Assert.ThrowsAsync<BillingRuleException>(() => service.CreateAsync(
            new CreateSubscriptionRequest(student.Id, bus.Id, 500_000m, null, new DateOnly(2027, 1, 1), null),
            actor));

        Assert.Equal("subscription_overlap", ex.Code);
        Assert.Equal(BillingFault.Conflict, ex.Fault);

        await using var check = NewDb();
        Assert.Equal(1, await check.StudentSubscriptions
            .CountAsync(s => s.StudentId == student.Id && s.CategoryId == bus.Id));
    }

    /// <summary>Boshqa TOIFA kesishish hisoblanmaydi: o'quvchi bir vaqtda avtobusga ham, ovqatga ham yozilishi normal.</summary>
    [Fact]
    public async Task Boshqa_toifadagi_obuna_kesishish_hisoblanmaydi()
    {
        var actor = await NewUserAsync();
        var student = await NewStudentAsync();
        var bus = await CategoryAsync(FeeCategoryCodes.Bus);
        var meals = await CategoryAsync(FeeCategoryCodes.Meals);

        await using var db = NewDb();
        var service = Subscriptions(db);

        await service.CreateAsync(
            new CreateSubscriptionRequest(student.Id, bus.Id, 400_000m, null, new DateOnly(2026, 9, 1), null), actor);
        await service.CreateAsync(
            new CreateSubscriptionRequest(student.Id, meals.Id, 600_000m, null, new DateOnly(2026, 9, 1), null), actor);

        var list = await service.ListAsync(new SubscriptionQuery(student.Id));
        Assert.Equal(2, list.Count);
    }

    /// <summary>Eskisi yopilgach yangisini (masalan qimmatroq yo'nalishni) ochish mumkin.</summary>
    [Fact]
    public async Task Eski_obuna_yopilgach_yangisini_ochish_mumkin()
    {
        var actor = await NewUserAsync();
        var student = await NewStudentAsync();
        var bus = await CategoryAsync(FeeCategoryCodes.Bus);

        await using var db = NewDb();
        var service = Subscriptions(db);

        var first = await service.CreateAsync(
            new CreateSubscriptionRequest(student.Id, bus.Id, 400_000m, null, new DateOnly(2026, 9, 1), null), actor);

        await service.EndAsync(first.Id, new EndSubscriptionRequest(new DateOnly(2026, 12, 31)), actor);

        var second = await service.CreateAsync(
            new CreateSubscriptionRequest(student.Id, bus.Id, 500_000m, null, new DateOnly(2027, 1, 1), null), actor);

        Assert.Equal(500_000m, second.MonthlyAmount);

        // Bugungi kunda (2026-09-11 dan keyin) faqat bittasi faol bo'lishi kerak emas —
        // muhimi ikkalasi ham saqlangani va davrlari kesishmagani.
        await using var check = NewDb();
        var rows = await check.StudentSubscriptions.AsNoTracking()
            .Where(s => s.StudentId == student.Id && s.CategoryId == bus.Id)
            .OrderBy(s => s.StartsOn).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2026, 12, 31), rows[0].EndsOn);
    }

    /// <summary>Arxivlangan o'quvchiga yangi obuna ochilmaydi — unga oylik ham hisoblanmaydi.</summary>
    [Fact]
    public async Task Arxivlangan_oquvchiga_obuna_ochilmaydi()
    {
        var actor = await NewUserAsync();
        var student = await NewStudentAsync(archived: true);
        var bus = await CategoryAsync(FeeCategoryCodes.Bus);

        await using var db = NewDb();
        var ex = await Assert.ThrowsAsync<BillingRuleException>(() => Subscriptions(db).CreateAsync(
            new CreateSubscriptionRequest(student.Id, bus.Id, 400_000m, null, new DateOnly(2026, 9, 1), null), actor));

        Assert.Equal("student_archived", ex.Code);
    }

    /// <summary>Narx o'zgarishi audit jurnaliga tushadi — "kim bu bolaning oyligini ko'tardi" savoli javobsiz qolmasin (SPEC §4.6).</summary>
    [Fact]
    public async Task Narx_ozgarishi_audit_jurnaliga_tushadi()
    {
        var actor = await NewUserAsync();
        var student = await NewStudentAsync();
        var bus = await CategoryAsync(FeeCategoryCodes.Bus);

        await using var db = NewDb();
        var service = Subscriptions(db);

        var created = await service.CreateAsync(
            new CreateSubscriptionRequest(student.Id, bus.Id, 400_000m, null, new DateOnly(2026, 9, 1), null), actor);

        var updated = await service.UpdateAsync(
            created.Id, new UpdateSubscriptionRequest(550_000m, "5-yo'nalish", null), actor);

        Assert.Equal(550_000m, updated.MonthlyAmount);

        await using var check = NewDb();
        var log = await check.AuditLogs.AsNoTracking()
            .Where(a => a.EntityId == created.Id.ToString() && a.Action == "update")
            .ToListAsync();

        var entry = Assert.Single(log);
        Assert.Contains("400 000", entry.Summary);
        Assert.Contains("550 000", entry.Summary);
    }

    // ==================================================================
    //  3. Chegirmalar — mijoz javobi (SPEC §8.1 Q5): CHEGARA YO'Q
    // ==================================================================

    /// <summary>
    /// Chegirma HAR DOIM <c>pending</c> holatda tug'iladi — summasi qanday
    /// bo'lishidan qat'i nazar. "Chegaradan past bo'lsa darhol amal qiladi"
    /// degan yo'l YO'Q (docs/TASKS.md dagi eski matn mijoz javobi bilan bekor
    /// qilingan).
    /// </summary>
    [Theory]
    [InlineData(5, 0)]              // juda kichik foiz
    [InlineData(0, 10_000)]         // juda kichik summa
    [InlineData(100, 0)]            // to'liq bepul
    [InlineData(50, 1_000_000)]     // katta chegirma
    public async Task Chegirma_har_doim_pending_holatda_yaratiladi(int percent, int amount)
    {
        var creator = await NewUserAsync();
        var student = await NewStudentAsync();

        await using var db = NewDb();
        var created = await Discounts(db).CreateAsync(
            new CreateDiscountRequest(student.Id, null, percent, amount, "Ko'p bolali oila", new DateOnly(2026, 9, 1), null),
            creator);

        Assert.Equal(DiscountStatus.Pending, created.Status);
        Assert.Null(created.ApprovedByName);
        Assert.Null(created.DecidedAt);

        await using var check = NewDb();
        var row = await check.Discounts.AsNoTracking().FirstAsync(d => d.Id == created.Id);
        Assert.Null(row.ApprovedBy);
        Assert.Equal(DiscountStatus.Pending, row.Status);
    }

    /// <summary>
    /// SPEC §4.5 — ikki qavatli nazorat: yaratuvchi o'zi tasdiqlay olmaydi.
    /// Qator <b>o'zgarmasdan</b> qolishi ham tekshiriladi: "403 qaytdi, lekin
    /// yozib ham qo'ydi" — eng yomon holat.
    /// </summary>
    [Fact]
    public async Task Ozi_soragan_chegirmani_ozi_tasdiqlay_olmaydi()
    {
        var director = await NewUserAsync(Roles.SuperAdmin);
        var student = await NewStudentAsync();

        await using var db = NewDb();
        var service = Discounts(db);

        // Direktorning O'ZI so'ragan chegirma — roli yetarli, lekin shaxsi o'sha.
        var created = await service.CreateAsync(
            new CreateDiscountRequest(student.Id, null, 20m, 0m, "Xodim farzandi", new DateOnly(2026, 9, 1), null),
            director);

        var ex = await Assert.ThrowsAsync<BillingRuleException>(() => service.ApproveAsync(created.Id, director));
        Assert.Equal("self_approval", ex.Code);
        Assert.Equal(BillingFault.Forbidden, ex.Fault);

        await using var check = NewDb();
        var row = await check.Discounts.AsNoTracking().FirstAsync(d => d.Id == created.Id);
        Assert.Equal(DiscountStatus.Pending, row.Status);
        Assert.Null(row.ApprovedBy);
        Assert.Null(row.DecidedAt);
    }

    /// <summary>
    /// Ilova tekshiruvi chetlab o'tilsa ham baza to'xtatadi:
    /// <c>ck_discounts_approver_differs</c> (P1-05). Bu qabul mezonining
    /// "and additionally fails at the DB check constraint" qismi.
    /// </summary>
    [Fact]
    public async Task Baza_ham_ozini_ozi_tasdiqlashni_bloklaydi()
    {
        var creator = await NewUserAsync();
        var student = await NewStudentAsync();

        await using var db = NewDb();
        var created = await Discounts(db).CreateAsync(
            new CreateDiscountRequest(student.Id, null, 30m, 0m, "Sinov", new DateOnly(2026, 9, 1), null),
            creator);

        // Xizmatni CHETLAB O'TIB, to'g'ridan-to'g'ri bazaga yozishga urinish.
        await using var direct = NewDb();
        var row = await direct.Discounts.FirstAsync(d => d.Id == created.Id);
        row.Status = DiscountStatus.Approved;
        row.ApprovedBy = row.CreatedBy;

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync());
        var pg = ex.InnerException as PostgresException;
        Assert.NotNull(pg);
        Assert.Equal("23514", pg!.SqlState);
        Assert.Equal("ck_discounts_approver_differs", pg.ConstraintName);

        await using var check = NewDb();
        var after = await check.Discounts.AsNoTracking().FirstAsync(d => d.Id == created.Id);
        Assert.Equal(DiscountStatus.Pending, after.Status);
    }

    /// <summary>Mijoz javobi (SPEC §8.1 Q5): tasdiq FAQAT direktorniki — admin tasdiqlay olmaydi.</summary>
    [Fact]
    public async Task Adminning_tasdigi_qabul_qilinmaydi()
    {
        var creator = await NewUserAsync();
        var otherAdmin = await NewUserAsync(Roles.Admin);
        var student = await NewStudentAsync();

        await using var db = NewDb();
        var service = Discounts(db);

        var created = await service.CreateAsync(
            new CreateDiscountRequest(student.Id, null, 25m, 0m, "Aka-uka chegirmasi", new DateOnly(2026, 9, 1), null),
            creator);

        var ex = await Assert.ThrowsAsync<BillingRuleException>(() => service.ApproveAsync(created.Id, otherAdmin));
        Assert.Equal("approver_not_director", ex.Code);
        Assert.Equal(BillingFault.Forbidden, ex.Fault);
    }

    /// <summary>Direktor tasdiqlagach chegirma amal qiladi: holat, tasdiqlovchi va qaror vaqti yoziladi.</summary>
    [Fact]
    public async Task Direktor_tasdiqlagach_chegirma_amal_qiladi()
    {
        var creator = await NewUserAsync();
        var director = await NewUserAsync(Roles.SuperAdmin);
        var student = await NewStudentAsync();

        await using var db = NewDb();
        var service = Discounts(db);

        var created = await service.CreateAsync(
            new CreateDiscountRequest(student.Id, null, 20m, 0m, "Ko'p bolali oila", new DateOnly(2026, 9, 1), null),
            creator);

        var approved = await service.ApproveAsync(created.Id, director);

        Assert.Equal(DiscountStatus.Approved, approved.Status);
        Assert.NotNull(approved.ApprovedByName);
        Assert.NotNull(approved.DecidedAt);

        await using var check = NewDb();
        var row = await check.Discounts.AsNoTracking().FirstAsync(d => d.Id == created.Id);
        Assert.Equal(director, row.ApprovedBy);
        Assert.NotEqual(row.CreatedBy, row.ApprovedBy);
        // `timestamptz` LAHZA saqlaydi — Postgres uni UTC sifatida qaytaradi.
        Assert.Equal(TimeSpan.Zero, row.DecidedAt!.Value.Offset);
    }

    /// <summary>Qaror bir marta: tasdiqlangan chegirmani qayta tasdiqlab ham, rad etib ham bo'lmaydi.</summary>
    [Fact]
    public async Task Qaror_qabul_qilingan_chegirmani_qayta_ozgartirib_bolmaydi()
    {
        var creator = await NewUserAsync();
        var director = await NewUserAsync(Roles.SuperAdmin);
        var otherDirector = await NewUserAsync(Roles.SuperAdmin);
        var student = await NewStudentAsync();

        await using var db = NewDb();
        var service = Discounts(db);

        var created = await service.CreateAsync(
            new CreateDiscountRequest(student.Id, null, 20m, 0m, "Sinov", new DateOnly(2026, 9, 1), null), creator);
        await service.ApproveAsync(created.Id, director);

        var ex = await Assert.ThrowsAsync<BillingRuleException>(() => service.ApproveAsync(created.Id, otherDirector));
        Assert.Equal("discount_already_decided", ex.Code);

        var ex2 = await Assert.ThrowsAsync<BillingRuleException>(
            () => service.RejectAsync(created.Id, otherDirector, "fikrimdan qaytdim"));
        Assert.Equal("discount_already_decided", ex2.Code);
    }

    /// <summary>
    /// Rad etilgan chegirma tarixda qoladi, lekin <c>approved</c> ro'yxatiga
    /// tushmaydi — accrual (P1-09) aynan o'sha ro'yxatdan o'qiydi. Rad etish
    /// sababi audit jurnalida saqlanadi (sxemada ustun yo'q).
    /// </summary>
    [Fact]
    public async Task Rad_etilgan_chegirma_hisob_kitobga_kirmaydi()
    {
        var creator = await NewUserAsync();
        var director = await NewUserAsync(Roles.SuperAdmin);
        var student = await NewStudentAsync();

        await using var db = NewDb();
        var service = Discounts(db);

        var created = await service.CreateAsync(
            new CreateDiscountRequest(student.Id, null, 40m, 0m, "Asossiz so'rov", new DateOnly(2026, 9, 1), null), creator);

        var rejected = await service.RejectAsync(created.Id, director, "Hujjat yetarli emas");
        Assert.Equal(DiscountStatus.Rejected, rejected.Status);

        var approved = await service.ListAsync(new DiscountQuery(student.Id, Status: DiscountStatus.Approved));
        Assert.Empty(approved);

        var all = await service.ListAsync(new DiscountQuery(student.Id));
        Assert.Single(all);

        await using var check = NewDb();
        var log = await check.AuditLogs.AsNoTracking()
            .Where(a => a.EntityId == created.Id.ToString() && a.Action == "update")
            .ToListAsync();
        Assert.Contains(log, a => a.Summary.Contains("Hujjat yetarli emas"));
    }

    /// <summary>
    /// <c>category_id = null</c> — chegirma BARCHA toifalarga tegishli, shuning
    /// uchun toifa bo'yicha filtrda ham ko'rinishi SHART. Aks holda accrual
    /// umumiy chegirmani ko'rmay qolardi va o'quvchi ortiqcha qarzdor bo'lardi.
    /// </summary>
    [Fact]
    public async Task Umumiy_chegirma_toifa_filtrida_ham_korinadi()
    {
        var creator = await NewUserAsync();
        var student = await NewStudentAsync();
        var bus = await CategoryAsync(FeeCategoryCodes.Bus);

        await using var db = NewDb();
        var service = Discounts(db);

        await service.CreateAsync(
            new CreateDiscountRequest(student.Id, null, 10m, 0m, "Umumiy", new DateOnly(2026, 9, 1), null), creator);

        var forBus = await service.ListAsync(new DiscountQuery(student.Id, bus.Id));
        var row = Assert.Single(forBus);
        Assert.Null(row.CategoryId);
        Assert.Null(row.CategoryCode);
    }

    /// <summary>Bo'sh chegirma (foiz ham, summa ham 0) — ma'nosiz yozuv, rad etiladi.</summary>
    [Fact]
    public async Task Bosh_chegirma_rad_etiladi()
    {
        var creator = await NewUserAsync();
        var student = await NewStudentAsync();

        await using var db = NewDb();
        var ex = await Assert.ThrowsAsync<BillingRuleException>(() => Discounts(db).CreateAsync(
            new CreateDiscountRequest(student.Id, null, 0m, 0m, "Sabab", new DateOnly(2026, 9, 1), null), creator));

        Assert.Equal("empty_discount", ex.Code);
    }

    /// <summary>Sabab majburiy (SPEC §4.3) — bo'sh sabab bilan chegirma yozilmaydi.</summary>
    [Fact]
    public async Task Chegirma_sababi_majburiy()
    {
        var creator = await NewUserAsync();
        var student = await NewStudentAsync();

        await using var db = NewDb();
        var ex = await Assert.ThrowsAsync<BillingRuleException>(() => Discounts(db).CreateAsync(
            new CreateDiscountRequest(student.Id, null, 20m, 0m, "   ", new DateOnly(2026, 9, 1), null), creator));

        Assert.Equal("reason_required", ex.Code);
    }

    // ==================================================================
    //  4. Ruxsat (SPEC §4.3) — HTTP darajasida
    // ==================================================================

    /// <summary>
    /// SPEC §4.3: kassirda "Change monthly fee" ham, "Grant a discount" ham
    /// YO'Q. Shuning uchun u bu yuzaning HECH BIR endpoint'iga kira olmaydi —
    /// o'qishga ham (o'qish huquqi beriladigan qator jadvalda umuman yo'q).
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/admin/billing/categories")]
    [InlineData("GET", "/api/admin/billing/subscriptions")]
    [InlineData("GET", "/api/admin/billing/discounts")]
    [InlineData("GET", "/api/admin/billing/discounts/pending")]
    [InlineData("POST", "/api/admin/billing/categories")]
    [InlineData("POST", "/api/admin/billing/subscriptions")]
    [InlineData("POST", "/api/admin/billing/discounts")]
    public async Task Kassir_moliya_malumotnomasiga_kira_olmaydi(string method, string url)
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);

        var response = method == "GET"
            ? await client.GetAsync(url)
            : await client.PostAsJsonAsync(url, new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Token'siz so'rov — 401 (403 emas: kim ekani umuman noma'lum).</summary>
    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();

        var response = await client.GetAsync("/api/admin/billing/categories");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Mijoz javobi (SPEC §8.1 Q5): admin chegirma SO'RAY oladi, lekin
    /// TASDIQLAY olmaydi. Bu endpoint darajasida ham (atribut), xizmat
    /// darajasida ham (rol tekshiruvi) yopiq.
    /// </summary>
    [Fact]
    public async Task Admin_chegirmani_tasdiqlay_olmaydi_403()
    {
        var (client, _) = await WiredClientAsync(Roles.Admin);
        using var _client = client;

        var response = await client.PostAsync($"/api/admin/billing/discounts/{Guid.NewGuid()}/approve", null);

        // 404 EMAS, 403: ruxsat filtri controller'gacha yetib bormaydi, ya'ni
        // ruxsatsiz odam chegirma bor-yo'qligini ham bilib ololmaydi.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ==================================================================
    //  5. To'liq oqim — HTTP
    // ==================================================================

    /// <summary>
    /// P1-08 ning eng muhim qabul mezoni HTTP chegarasida: chegirma so'rovi
    /// <b>409 emas</b>, 200 va <c>status = "pending"</c> qaytaradi (mijoz javobi
    /// tasdiq kutishni normal oqim deb belgilagan), keyin direktor uni
    /// tasdiqlaydi — va faqat shundan keyin u <c>approved</c> bo'ladi.
    /// </summary>
    [Fact]
    public async Task Chegirma_oqimi_sorov_pending_keyin_direktor_tasdigi()
    {
        var student = await NewStudentAsync();
        var (adminClient, _) = await WiredClientAsync(Roles.Admin);
        var (directorClient, _) = await WiredClientAsync(Roles.SuperAdmin);
        using var _admin = adminClient;
        using var _director = directorClient;

        var create = await adminClient.PostAsJsonAsync("/api/admin/billing/discounts",
            new CreateDiscountRequest(student.Id, null, 20m, 0m, "Ko'p bolali oila", new DateOnly(2026, 9, 1), null));

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var pending = await create.Content.ReadFromJsonAsync<DiscountDto>();
        Assert.NotNull(pending);
        Assert.Equal(DiscountStatus.Pending, pending!.Status);
        Assert.Null(pending.ApprovedByName);

        // Navbatda ko'rinadi.
        var queue = await directorClient.GetFromJsonAsync<List<DiscountDto>>("/api/admin/billing/discounts/pending");
        Assert.Contains(queue!, d => d.Id == pending.Id);

        var approve = await directorClient.PostAsync($"/api/admin/billing/discounts/{pending.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var approved = await approve.Content.ReadFromJsonAsync<DiscountDto>();
        Assert.Equal(DiscountStatus.Approved, approved!.Status);
        Assert.NotNull(approved.ApprovedByName);
    }

    /// <summary>
    /// Ustma-ust obuna HTTP darajasida ham 409 beradi va javob tanasida
    /// mashina o'qiydigan kod (<c>subscription_overlap</c>) hamda frontend
    /// ko'rsatadigan <c>message</c> bo'ladi.
    /// </summary>
    [Fact]
    public async Task Ustma_ust_obuna_HTTP_da_409_va_kod_qaytaradi()
    {
        var student = await NewStudentAsync();
        var bus = await CategoryAsync(FeeCategoryCodes.Bus);
        var (client, _) = await WiredClientAsync(Roles.Admin);
        using var _client = client;

        var request = new CreateSubscriptionRequest(student.Id, bus.Id, 400_000m, null, new DateOnly(2026, 9, 1), null);

        var first = await client.PostAsJsonAsync("/api/admin/billing/subscriptions", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/admin/billing/subscriptions", request);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var error = await second.Content.ReadFromJsonAsync<BillingErrorDto>();
        Assert.Equal("subscription_overlap", error!.Code);
        Assert.NotEmpty(error.Message);
    }

    /// <summary>
    /// SPEC §4.4 — shaxs SERVERDA aniqlanadi. Direktor o'zi so'ragan chegirmani
    /// o'zi tasdiqlay olmaydi: roli yetarli, lekin ikkinchi shaxs kerak (§4.5).
    /// </summary>
    [Fact]
    public async Task Direktor_oz_sorovini_HTTP_da_ham_tasdiqlay_olmaydi()
    {
        var student = await NewStudentAsync();
        var (client, _) = await WiredClientAsync(Roles.SuperAdmin);
        using var _client = client;

        var create = await client.PostAsJsonAsync("/api/admin/billing/discounts",
            new CreateDiscountRequest(student.Id, null, 15m, 0m, "Xodim farzandi", new DateOnly(2026, 9, 1), null));
        var created = await create.Content.ReadFromJsonAsync<DiscountDto>();

        var approve = await client.PostAsync($"/api/admin/billing/discounts/{created!.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
        var error = await approve.Content.ReadFromJsonAsync<BillingErrorDto>();
        Assert.Equal("self_approval", error!.Code);

        await using var check = NewDb();
        var row = await check.Discounts.AsNoTracking().FirstAsync(d => d.Id == created.Id);
        Assert.Equal(DiscountStatus.Pending, row.Status);
    }

    /// <summary>To'lov toifalari ro'yxati: migratsiya seed qilgan beshtasi joyida.</summary>
    [Fact]
    public async Task Toifalar_royxatida_seed_qilingan_beshtasi_bor()
    {
        var (client, _) = await WiredClientAsync(Roles.Admin);
        using var _client = client;

        var categories = await client.GetFromJsonAsync<List<FeeCategoryDto>>("/api/admin/billing/categories?activeOnly=true");

        Assert.NotNull(categories);
        foreach (var code in FeeCategoryCodes.Seeded)
            Assert.Contains(categories!, c => c.Code == code);
    }

    // ==================================================================
    //  Yordamchilar
    // ==================================================================

    private async Task<string> NewUserAsync(string role = Roles.Admin)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        return user.Id;
    }

    /// <summary>Har test o'z sinfini va o'quvchisini yaratadi — testlar bir bazani bo'lishadi.</summary>
    private async Task<Student> NewStudentAsync(decimal classFee = 1_500_000m, bool archived = false)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var schoolClass = new SchoolClass { Name = $"T-{suffix}", Grade = 8, MonthlyFee = classFee };
        var student = new Student
        {
            FullName = $"Test o'quvchi {suffix}",
            ClassName = schoolClass.Name,
            EnrollmentDate = "2026-09-01",
            IsArchived = archived,
        };

        await using var db = NewDb();
        db.Classes.Add(schoolClass);
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return student;
    }

    private async Task<FeeCategory> CategoryAsync(string code)
    {
        await using var db = NewDb();
        return await db.FeeCategories.AsNoTracking().FirstAsync(c => c.Code == code);
    }

    // ------------------------------------------------------------------
    //  DI bo'shlig'i (P1-15 gacha)
    // ------------------------------------------------------------------
    //
    //  `ISubscriptionService` va `IDiscountService` `Program.cs` da hali
    //  ro'yxatdan o'tmagan — u fayl P1-15 niki va unga tegilmaydi
    //  (docs/PENDING_WIRING.md). Ruxsat testlari busiz ham ishlaydi
    //  (authorization filtri controller YARATILGUNCHA ishlaydi), lekin to'liq
    //  oqimni HTTP orqali sinash uchun xizmatlar kerak.
    //
    //  Yechim: `WithWebHostBuilder` bilan HOSILA fabrika — ikkita qator DI
    //  qo'shiladi, boshqa hech narsa o'zgarmaydi (`ApiFactory.ConfigureWebHost`
    //  avval ishlaydi: o'sha baza, o'sha JWT kaliti, fon xizmatlari o'chiq).
    //  Umumiy fixture fayllariga TEGILMAYDI — parallel ishlayotgan boshqa
    //  vazifalar ularni ham o'zgartirayotgan bo'lishi mumkin.
    //
    //  P1-15 ikkita qatorni `Program.cs` ga qo'shgach, bu yordamchi o'rniga
    //  oddiy `fixture.Api.ClientAsAsync(...)` ishlatilsa bo'ladi.

    private static readonly Lock WireLock = new();
    private static WebApplicationFactory<AuthController>? _wired;

    private WebApplicationFactory<AuthController> WiredApi()
    {
        lock (WireLock)
        {
            return _wired ??= fixture.Api.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.AddScoped<ISubscriptionService, SubscriptionService>();
                    services.AddScoped<IDiscountService, DiscountService>();
                }));
        }
    }

    private async Task<(HttpClient Client, AppUser User)> WiredClientAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);

        var client = WiredApi().CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));

        return (client, user);
    }
}
