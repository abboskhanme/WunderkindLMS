using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Qarzdorlar bilan ISHLASH (docs/modules/existing-module-gaps.md §3.5):
/// rangli holat ma'lumotnomasi, amallar tarixi, hisoblanadigan joriy holat,
/// yumshoq o'chirish va beshinchi anomaliya — buzilgan to'lov va'dasi.
///
/// <para>
/// <b>Testlar ikki guruhga bo'lingan</b> (<see cref="FinanceReportsTests"/>
/// dagi sabab bilan). RUXSAT va JSON shakli — umumiy bazada, HTTP orqali:
/// ular boshqa testlarning qatorlariga befarq. MANTIQ — har biri O'ZINING
/// toza bazasida: "buzilgan va'da" butun jadval bo'ylab qaraydi, ya'ni
/// qo'shni testning bitta qarzdori ham natijani o'zgartirardi.
/// </para>
/// <para>
/// <b>Sanalar 2007-yil.</b> Boshqa moliya testlari 2001–2003 va 2025 bilan
/// yozadi — kesishmasin. Va'da sanalari esa BUGUNGA nisbatan hisoblanadi:
/// "buzilgan va'da" ta'rifining o'zi bugungi kunga bog'liq.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class DebtorWorkflowTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>
    /// Shu klass yaratgan bazalarning ulanish satrlari.
    ///
    /// <para>
    /// <b>Nega kerak.</b> Har test o'z bazasini oladi, ya'ni o'z ulanish
    /// hovuzini ham. Hovuz tozalanmasa tugagan testning ulanishlari ochiq
    /// qolib, konteynerdagi <c>max_connections</c> ni yeb qo'yadi va keyingi
    /// test klasslari <c>53300</c> bilan yiqiladi — o'z aybi bilan emas.
    /// Bir xil izoh <c>AllocationTests</c> va <c>CashDayTests</c> da ham bor.
    /// </para>
    /// </summary>
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));

        return Task.CompletedTask;
    }

    private const string Statuses = "/api/admin/finance/debtor-statuses";
    private const string Workflow = "/api/admin/finance/debtors/workflow";
    private const string BrokenPromises = "/api/admin/finance/debtors/broken-promises";

    private static readonly string[] ReadEndpoints = [Statuses, Workflow, BrokenPromises];

    /// <summary>Seed qatori (<c>parity_wave2_seed.sql</c>) — "To'lash va'da qilindi".</summary>
    private static readonly Guid PromisedStatusId = new("00000000-0000-0000-0000-0000000000d2");

    // =====================================================================
    //  1. RUXSAT — SPEC §4.3
    // =====================================================================

    /// <summary>
    /// <b>Asosiy ruxsat mezoni.</b> Kassir qarzdorlar ish oqimini NA KO'RADI,
    /// NA YOZADI. §4.3: moliya hisobotlari — admin va direktor. "Kim
    /// to'lamayapti" ro'yxati kassaning kundalik ishiga kerak emas, va yozish
    /// o'qishdan tor bo'lishi kerak edi — bu yerda ikkovi ham yopiq.
    /// </summary>
    [Fact]
    public async Task Kassir_qarzdorlar_ish_oqimiga_kira_olmaydi_403()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);

        foreach (var url in ReadEndpoints)
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);

        // Yozish ham — ro'yxatni ko'ra olmaydigan odam unga yozib ham bo'lmaydi.
        var status = await client.PostAsJsonAsync(Statuses, new { name = "Kassir holati" });
        Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);

        var action = await client.PostAsJsonAsync(
            "/api/admin/finance/debtors/any-student/actions", new { comment = "Bog'landim" });
        Assert.Equal(HttpStatusCode.Forbidden, action.StatusCode);

        var delete = await client.DeleteAsync($"/api/admin/finance/debtor-actions/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);

        var retire = await client.DeleteAsync($"{Statuses}/{PromisedStatusId}");
        Assert.Equal(HttpStatusCode.Forbidden, retire.StatusCode);
    }

    /// <summary>
    /// "finance" ruxsat kaliti bor xodim ham yopiq: §4.3 jadvalida "staff"
    /// ustuni umuman yo'q. Menyuni yashirish yetarli emas.
    /// </summary>
    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Teacher)]
    public async Task Xodim_va_oqituvchi_ham_kira_olmaydi_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "finance");

        foreach (var url in ReadEndpoints)
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
    }

    /// <summary>Token'siz so'rov — 401 (403 emas): kim so'rayotgani noma'lum.</summary>
    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();

        foreach (var url in ReadEndpoints)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_direktor_200_oladi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);

        foreach (var url in ReadEndpoints)
        {
            var response = await client.GetAsync(url);
            Assert.True(response.IsSuccessStatusCode,
                $"{url} → {(int)response.StatusCode} {response.StatusCode}");
        }
    }

    // =====================================================================
    //  2. HTTP shartnomasi — frontend AYNAN shu nomlarga bog'lanadi
    // =====================================================================

    /// <summary>
    /// "Amal qo'shish" oynasining to'liq yo'li: POST → tarix → ro'yxat
    /// ustunlari. Maydon nomlari camelCase va TS tiplari bilan bir xil
    /// bo'lishi shart — nom o'zgarsa ekran jimgina bo'sh qolardi.
    ///
    /// <para>
    /// <b>Kim yozgani so'rov tanasidan olinmaydi</b> (SPEC §4.4): quyida
    /// tanada <c>createdBy</c> umuman yo'q, javobda esa u token egasiga
    /// teng bo'lishi kerak.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Amal_qoshish_va_tarix_JSON_shakli_togri()
    {
        var (admin, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        using var client = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Admin, admin.Id, admin.FullName, admin.Email));

        var studentId = $"dw-http-{Guid.NewGuid():N}"[..20];
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(NewStudent(studentId, "Qarzdor Test", "7-B"));
            await db.SaveChangesAsync();
        });

        var promised = AppClock.Today.AddDays(7);
        var created = await client.PostAsJsonAsync(
            $"/api/admin/finance/debtors/{studentId}/actions",
            new
            {
                comment = "Ota-onaga qo'ng'iroq qilindi",
                statusId = PromisedStatusId,
                promisedOn = promised.ToString("yyyy-MM-dd"),
            });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(studentId, body.GetProperty("studentId").GetString());
        Assert.Equal("Ota-onaga qo'ng'iroq qilindi", body.GetProperty("comment").GetString());
        Assert.Equal(PromisedStatusId, body.GetProperty("statusId").GetGuid());
        Assert.Equal("To'lash va'da qilindi", body.GetProperty("statusName").GetString());
        Assert.Equal("#FF9500", body.GetProperty("statusColor").GetString());
        Assert.Equal(promised.ToString("yyyy-MM-dd"), body.GetProperty("promisedOn").GetString());
        Assert.False(body.GetProperty("promiseOverdue").GetBoolean());
        Assert.Equal(admin.Id, body.GetProperty("createdBy").GetString());
        Assert.Equal(admin.FullName, body.GetProperty("createdByName").GetString());

        // Tarix — o'sha qator.
        var history = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/finance/debtors/{studentId}/actions");
        Assert.Single(history.EnumerateArray());

        // Ro'yxat ustunlari — joriy holat shu amaldan hisoblanadi.
        var rows = await client.GetFromJsonAsync<JsonElement>(Workflow);
        var row = rows.EnumerateArray()
            .Single(r => r.GetProperty("studentId").GetString() == studentId);

        Assert.Equal("To'lash va'da qilindi", row.GetProperty("statusName").GetString());
        Assert.Equal("Qarzdor Test", row.GetProperty("fullName").GetString());
        Assert.Equal(1, row.GetProperty("actionCount").GetInt32());
        Assert.False(row.GetProperty("promiseBroken").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("lastActionAt").GetString()));
    }

    /// <summary>Izohsiz amal — 400 va mashina o'qiydigan kod.</summary>
    [Fact]
    public async Task Izohsiz_amal_400_beradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var studentId = $"dw-noc-{Guid.NewGuid():N}"[..20];
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(NewStudent(studentId, "Izohsiz Test", "5-A"));
            await db.SaveChangesAsync();
        });

        var response = await client.PostAsJsonAsync(
            $"/api/admin/finance/debtors/{studentId}/actions", new { comment = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("comment_required", error.GetProperty("code").GetString());
    }

    // =====================================================================
    //  3. JORIY HOLAT — HISOBLANADI, saqlanmaydi
    // =====================================================================

    /// <summary>
    /// §3.5: joriy holat = eng oxirgi TIRIK amalning holati. Saqlangan ustun
    /// yo'q, shuning uchun yangi amal yozilishi bilan holat o'zgaradi —
    /// hech qanday "yangilash" qadamisiz.
    /// </summary>
    [Fact]
    public async Task Joriy_holat_eng_oxirgi_amaldan_hisoblanadi()
    {
        await using var db = await NewDbAsync("status");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var studentId = await SeedStudentAsync(db, "Holat Test", "8-A");

        var service = new DebtorWorkflowService(db);
        var catalog = await service.StatusesAsync(ct: default);
        var contacted = catalog.Single(s => s.Name == "Bog'lanildi");
        var promisedStatus = catalog.Single(s => s.Name == "To'lash va'da qilindi");

        await service.AddActionAsync(
            studentId, new CreateDebtorActionRequest("Birinchi qo'ng'iroq", contacted.Id), actorId);
        var second = await service.AddActionAsync(
            studentId,
            new CreateDebtorActionRequest("Ikkinchi suhbat", promisedStatus.Id, AppClock.Today.AddDays(5)),
            actorId);

        var row = Assert.Single(await service.RowsAsync());

        Assert.Equal(promisedStatus.Id, row.StatusId);
        Assert.Equal("To'lash va'da qilindi", row.StatusName);
        Assert.Equal("Ikkinchi suhbat", row.LastComment);
        // Postgres `timestamptz` MIKROSEKUNDgacha aniq, .NET esa 100 ns —
        // bazaga borib kelgan lahza aynan teng bo'lmaydi, yaqin bo'ladi.
        Assert.NotNull(row.LastActionAt);
        Assert.True((row.LastActionAt!.Value - second.CreatedAt).Duration() < TimeSpan.FromSeconds(1),
            $"Oxirgi amal vaqti kutilganidan uzoq: {row.LastActionAt} ≠ {second.CreatedAt}");
        Assert.Equal(AppClock.Today.AddDays(5), row.PromisedOn);
        Assert.Equal(2, row.ActionCount);
        Assert.False(row.PromiseBroken);
    }

    /// <summary>
    /// <b>Regressiya (§2.2 F2.01).</b> "O'zgartirilmasin" tanlangan amal
    /// (<c>status_id = null</c>) holatni O'ZGARTIRMAYDI — u shunchaki izoh
    /// yoki yangi va'da sanasi. Ilgari ro'yxat eng oxirgi amalning
    /// <c>StatusId</c> sini olardi, ya'ni izoh yozilishi bilan "Holat"
    /// ustuni JIMGINA bo'shab qolardi va qarzdor "hali hech kim bog'lanmagan"
    /// bo'lib ko'rinardi.
    ///
    /// <para>
    /// Joriy holat = eng oxirgi tirik amal <b>holati bilan</b>; oxirgi izoh
    /// va oxirgi amal vaqti esa baribir ENG OXIRGI qatordan keladi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Izoh_uchun_yozilgan_amal_joriy_holatni_ochirmaydi()
    {
        await using var db = await NewDbAsync("keepstatus");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var studentId = await SeedStudentAsync(db, "Holat Saqlanadi", "8-V");

        var service = new DebtorWorkflowService(db);
        var promisedStatus = (await service.StatusesAsync())
            .Single(s => s.Name == "To'lash va'da qilindi");

        await service.AddActionAsync(
            studentId,
            new CreateDebtorActionRequest("To'layman dedi", promisedStatus.Id, AppClock.Today.AddDays(6)),
            actorId);

        // Holat tanlanmagan amal — "O'zgartirilmasin" (DebtorActionModal.tsx).
        await service.AddActionAsync(
            studentId, new CreateDebtorActionRequest("Yana bir marta eslatildi", null), actorId);

        var row = Assert.Single(await service.RowsAsync());

        Assert.Equal(promisedStatus.Id, row.StatusId);
        Assert.Equal("To'lash va'da qilindi", row.StatusName);
        Assert.Equal("#FF9500", row.StatusColor);

        // Oxirgi amal — baribir eng oxirgi qator.
        Assert.Equal("Yana bir marta eslatildi", row.LastComment);
        Assert.Equal(2, row.ActionCount);
        Assert.Equal(AppClock.Today.AddDays(6), row.PromisedOn);
    }

    /// <summary>
    /// Oxirgi amal o'chirilsa (yumshoq), joriy holat AVVALGISIGA qaytadi —
    /// aynan shu xossa "holat hisoblanadi" degan qarorning qiymati. Saqlangan
    /// ustun bo'lganida u eski qiymatda qotib qolardi.
    /// </summary>
    [Fact]
    public async Task Ochirilgan_amal_joriy_holatdan_chiqadi()
    {
        await using var db = await NewDbAsync("undo");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var studentId = await SeedStudentAsync(db, "Qaytish Test", "8-B");

        var service = new DebtorWorkflowService(db);
        var catalog = await service.StatusesAsync();
        var contacted = catalog.Single(s => s.Name == "Bog'lanildi");
        var noAnswer = catalog.Single(s => s.Name == "Javob bermayapti");

        await service.AddActionAsync(
            studentId, new CreateDebtorActionRequest("Birinchi qo'ng'iroq", contacted.Id), actorId);
        var mistake = await service.AddActionAsync(
            studentId, new CreateDebtorActionRequest("Xato yozildi", noAnswer.Id), actorId);

        await service.DeleteActionAsync(mistake.Id, actorId);

        var row = Assert.Single(await service.RowsAsync());
        Assert.Equal(contacted.Id, row.StatusId);
        Assert.Equal("Birinchi qo'ng'iroq", row.LastComment);
        Assert.Equal(1, row.ActionCount);

        // Tarixda ham ko'rinmaydi...
        var history = await service.ActionsAsync(studentId);
        Assert.Single(history);
        Assert.DoesNotContain(history, a => a.Id == mistake.Id);
    }

    /// <summary>
    /// §3.5 ning eng qat'iy talabi: amal HECH QACHON o'chirilmaydi, faqat
    /// <c>deleted_at</c> qo'yiladi. Nima va'da qilingani uch yildan keyin
    /// ham bazada turishi kerak.
    /// </summary>
    [Fact]
    public async Task Amal_bazadan_ochirilmaydi_faqat_belgilanadi()
    {
        await using var db = await NewDbAsync("soft");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var studentId = await SeedStudentAsync(db, "O'chirish Test", "9-A");

        var service = new DebtorWorkflowService(db);
        var action = await service.AddActionAsync(
            studentId,
            new CreateDebtorActionRequest("15-sentabrga va'da berdi", null, AppClock.Today.AddDays(3)),
            actorId);

        await service.DeleteActionAsync(action.Id, actorId);

        var row = await db.DebtorActions.AsNoTracking().SingleAsync(a => a.Id == action.Id);
        Assert.NotNull(row.DeletedAt);
        // Va'da MATNI ham, SANASI ham joyida qoladi.
        Assert.Equal("15-sentabrga va'da berdi", row.Comment);
        Assert.Equal(AppClock.Today.AddDays(3), row.PromisedOn);

        // Audit izi — kim va qachon o'chirgani.
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(
            a => a.EntityType == AuditService.EntityDebtorAction
                 && a.EntityId == action.Id.ToString("D") && a.Action == "delete");
        Assert.Equal(actorId, audit.ActorId);
        Assert.Equal(studentId, audit.StudentId);

        // Ikkinchi marta o'chirib bo'lmaydi — 409.
        var repeat = await Assert.ThrowsAsync<BillingRuleException>(
            () => service.DeleteActionAsync(action.Id, actorId));
        Assert.Equal(BillingFault.Conflict, repeat.Fault);
        Assert.Equal("action_already_deleted", repeat.Code);
    }

    // =====================================================================
    //  4. HOLAT MA'LUMOTNOMASI
    // =====================================================================

    /// <summary>
    /// Katalog migratsiya bilan to'rtta qator olib keladi (§3.5 seed'i), va
    /// holat O'CHIRILMAYDI — katalogdan CHIQARILADI.
    /// </summary>
    [Fact]
    public async Task Holat_ochirilmaydi_katalogdan_chiqariladi()
    {
        await using var db = await NewDbAsync("catalog");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var studentId = await SeedStudentAsync(db, "Katalog Test", "6-A");

        var service = new DebtorWorkflowService(db);
        var seeded = await service.StatusesAsync();
        Assert.Equal(4, seeded.Count);
        Assert.Contains(seeded, s => s.Id == PromisedStatusId && s.Color == "#FF9500");

        // Chiqarilgan holat ESKI amalda ko'rinib turaveradi.
        var action = await service.AddActionAsync(
            studentId, new CreateDebtorActionRequest("Va'da oldik", PromisedStatusId), actorId);

        var retired = await service.RetireStatusAsync(PromisedStatusId, actorId);
        Assert.False(retired.IsActive);

        Assert.DoesNotContain(await service.StatusesAsync(), s => s.Id == PromisedStatusId);
        Assert.Contains(await service.StatusesAsync(includeInactive: true), s => s.Id == PromisedStatusId);
        Assert.True(await db.DebtorStatuses.AsNoTracking().AnyAsync(s => s.Id == PromisedStatusId));

        var history = await service.ActionsAsync(studentId);
        Assert.Equal("To'lash va'da qilindi", Assert.Single(history).StatusName);
        Assert.Equal(action.Id, history[0].Id);

        // YANGI amalda esa tanlab bo'lmaydi.
        var rejected = await Assert.ThrowsAsync<BillingRuleException>(
            () => service.AddActionAsync(
                studentId, new CreateDebtorActionRequest("Yana bir urinish", PromisedStatusId), actorId));
        Assert.Equal(BillingFault.Invalid, rejected.Fault);
        Assert.Equal("status_inactive", rejected.Code);
    }

    /// <summary>
    /// Nom unikal — ikkita bir xil holat ro'yxatni ikkiga bo'lib yuborardi.
    /// Kafolat BAZADA (<c>ix_debtor_statuses_name</c>), bu yerda esa u
    /// o'zbekcha 409 ga aylantiriladi.
    /// </summary>
    [Fact]
    public async Task Takroriy_holat_nomi_409()
    {
        await using var db = await NewDbAsync("dup");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var service = new DebtorWorkflowService(db);

        await service.CreateStatusAsync(
            new SaveDebtorStatusRequest("Sudga berildi", "#FF3B30", "Ariza topshirildi", 10), actorId);

        var duplicate = await Assert.ThrowsAsync<BillingRuleException>(
            () => service.CreateStatusAsync(new SaveDebtorStatusRequest("Sudga berildi"), actorId));

        Assert.Equal(BillingFault.Conflict, duplicate.Fault);
        Assert.Equal("status_name_taken", duplicate.Code);

        // Rang formati ham tekshiriladi.
        var badColor = await Assert.ThrowsAsync<BillingRuleException>(
            () => service.CreateStatusAsync(new SaveDebtorStatusRequest("Qizil", "red"), actorId));
        Assert.Equal("invalid_color", badColor.Code);
    }

    // =====================================================================
    //  5. BUZILGAN VA'DA — beshinchi anomaliya
    // =====================================================================

    /// <summary>
    /// Ta'rif: va'da sanasi o'tib ketgan VA qarz hali ochiq. To'rt holat
    /// bitta testda, chunki ular BITTA shartning to'rtta tomoni:
    /// <list type="number">
    ///   <item>qarzi bor + o'tib ketgan va'da → BUZILGAN;</item>
    ///   <item>qarzi yo'q (to'lab bo'lgan) + o'tib ketgan va'da → yo'q;</item>
    ///   <item>qarzi bor + kelasi sanaga va'da → yo'q;</item>
    ///   <item>qarzi bor + o'tib ketgan, lekin O'CHIRILGAN va'da → yo'q.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task Buzilgan_vada_faqat_qarz_ochiq_bolganda_topiladi()
    {
        await using var db = await NewDbAsync("promise");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var (cashierId, shiftId) = await SeedCashDeskAsync(db);
        var tuition = await CategoryIdAsync(db, "tuition");

        var broken = await SeedStudentAsync(db, "Buzgan Bola", "7-A");
        var paid = await SeedStudentAsync(db, "To'lagan Bola", "7-A");
        var future = await SeedStudentAsync(db, "Kutilayotgan Bola", "7-A");
        var deleted = await SeedStudentAsync(db, "O'chirilgan Va'da", "7-A");

        var month = new DateOnly(2007, 9, 1);
        var brokenInvoice = NewInvoice(broken, tuition, month, 1_000_000m);
        var paidInvoice = NewInvoice(paid, tuition, month, 800_000m);
        var futureInvoice = NewInvoice(future, tuition, month, 500_000m);
        var deletedInvoice = NewInvoice(deleted, tuition, month, 400_000m);
        db.Invoices.AddRange(brokenInvoice, paidInvoice, futureInvoice, deletedInvoice);
        await db.SaveChangesAsync();

        // "To'lagan bola" qarzini yopadi — va'dasi o'tib ketgan bo'lsa ham
        // u ro'yxatga TUSHMASLIGI kerak.
        await PayAsync(db, paid, cashierId, shiftId, 800_000m, [(paidInvoice.Id, 800_000m)]);

        var service = new DebtorWorkflowService(db);
        var overdue = AppClock.Today.AddDays(-3);

        await service.AddActionAsync(
            broken, new CreateDebtorActionRequest("Uchinchi kuni to'layman dedi", PromisedStatusId, overdue),
            actorId);
        await service.AddActionAsync(
            paid, new CreateDebtorActionRequest("To'layman dedi", PromisedStatusId, overdue), actorId);
        await service.AddActionAsync(
            future, new CreateDebtorActionRequest("Kelasi haftaga", PromisedStatusId, AppClock.Today.AddDays(5)),
            actorId);
        var mistake = await service.AddActionAsync(
            deleted, new CreateDebtorActionRequest("Xato sana", PromisedStatusId, overdue), actorId);
        await service.DeleteActionAsync(mistake.Id, actorId);

        var promises = await new BrokenPromiseScan(db).FindAsync();

        var row = Assert.Single(promises);
        Assert.Equal(broken, row.StudentId);
        Assert.Equal("Buzgan Bola", row.FullName);
        Assert.Equal(overdue, row.PromisedOn);
        Assert.Equal(3, row.DaysLate);
        Assert.Equal(1_000_000m, row.Debt);
        Assert.Equal("To'lash va'da qilindi", row.StatusName);

        // Ro'yxat ustunidagi bayroq ham xuddi shu shartni beradi.
        var rows = await service.RowsAsync();
        Assert.True(rows.Single(r => r.StudentId == broken).PromiseBroken);
        Assert.False(rows.Single(r => r.StudentId == paid).PromiseBroken);
        Assert.False(rows.Single(r => r.StudentId == future).PromiseBroken);
        // O'chirilgan amaldan keyin bu o'quvchida TIRIK amal qolmadi — ya'ni
        // ko'rsatadigan holat ham yo'q va qator umuman chiqmaydi.
        Assert.DoesNotContain(rows, r => r.StudentId == deleted);
    }

    /// <summary>
    /// Keyinroq kelishilgan sana eskisini BEKOR QILADI: "kecha buzilgan,
    /// bugun qayta kelishilgan" holat buzilgan bo'lib qolmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Yangi_vada_eskisini_bekor_qiladi()
    {
        await using var db = await NewDbAsync("renew");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var tuition = await CategoryIdAsync(db, "tuition");
        var studentId = await SeedStudentAsync(db, "Qayta Kelishildi", "9-B");

        db.Invoices.Add(NewInvoice(studentId, tuition, new DateOnly(2007, 10, 1), 600_000m));
        await db.SaveChangesAsync();

        var service = new DebtorWorkflowService(db);
        var scan = new BrokenPromiseScan(db);

        await service.AddActionAsync(
            studentId,
            new CreateDebtorActionRequest("Birinchi va'da", PromisedStatusId, AppClock.Today.AddDays(-5)),
            actorId);

        Assert.Single(await scan.FindAsync());

        await service.AddActionAsync(
            studentId,
            new CreateDebtorActionRequest("Qayta kelishildi", PromisedStatusId, AppClock.Today.AddDays(4)),
            actorId);

        Assert.Empty(await scan.FindAsync());

        // Va'da sanasi ro'yxatda YANGISI bo'lib turadi.
        var row = Assert.Single(await service.RowsAsync());
        Assert.Equal(AppClock.Today.AddDays(4), row.PromisedOn);
        Assert.False(row.PromiseBroken);
    }

    /// <summary>
    /// §3.5: buzilgan va'da direktor paneliga BESHINCHI <c>AnomalyKind</c>
    /// sifatida — mavjud mashina orqali (<c>GET /admin/finance/flags</c> ayni
    /// shu metodni chaqiradi).
    ///
    /// <para>
    /// <b>Qator YOZILMAYDI.</b> Test buni ham tekshiradi:
    /// <c>finance_anomaly_flags</c> bo'sh qoladi. Bayroq hisoblanadi, chunki
    /// buzilgan va'da — hodisa emas, holat (va bazadagi
    /// <c>ck_finance_anomaly_flags_kind</c> uni baribir qabul qilmaydi).
    /// </para>
    /// </summary>
    [Fact]
    public async Task Buzilgan_vada_beshinchi_anomaliya_bolib_chiqadi()
    {
        await using var db = await NewDbAsync("flag");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var tuition = await CategoryIdAsync(db, "tuition");
        var studentId = await SeedStudentAsync(db, "Anomaliya Bolasi", "10-A");

        db.Invoices.Add(NewInvoice(studentId, tuition, new DateOnly(2007, 11, 1), 750_000m));
        await db.SaveChangesAsync();

        var promised = AppClock.Today.AddDays(-10);
        await new DebtorWorkflowService(db).AddActionAsync(
            studentId, new CreateDebtorActionRequest("To'layman dedi", PromisedStatusId, promised), actorId);

        var flags = await new AnomalyService(db).ListAsync(unresolved: true);

        var flag = Assert.Single(flags.Items);
        Assert.Equal(AnomalyKind.BrokenPromise, flag.Kind);
        Assert.Equal("Buzilgan to'lov va'dasi", flag.KindLabel);
        Assert.Equal(AnomalyRefType.DebtorAction, flag.RefType);
        Assert.Equal(750_000m, flag.Amount);
        Assert.Null(flag.ResolvedAt);
        Assert.Contains("Anomaliya Bolasi", flag.Summary);
        Assert.Contains(promised.ToString("dd.MM.yyyy"), flag.Summary);

        Assert.Equal(1, flags.Unresolved);
        Assert.Equal(750_000m, flags.UnresolvedAmount);

        // Turlar kesimi — endi BESHTA qator, va beshinchisi sanaladi.
        Assert.Equal(AnomalyKind.All.Count, flags.ByKind.Count);
        var kind = Assert.Single(flags.ByKind, k => k.Kind == AnomalyKind.BrokenPromise);
        Assert.Equal(1, kind.Unresolved);

        // Eng muhimi: JADVALGA hech narsa yozilmadi.
        Assert.Empty(await db.FinanceAnomalyFlags.AsNoTracking().ToListAsync());

        // Tungi tekshiruv ham bu turni YARATMAYDI.
        var scan = await new AnomalyService(db).ScanAsync();
        Assert.DoesNotContain(AnomalyKind.BrokenPromise, scan.CreatedByKind.Keys);
        Assert.Empty(await db.FinanceAnomalyFlags.AsNoTracking()
            .Where(f => f.Kind == AnomalyKind.BrokenPromise).ToListAsync());
    }

    /// <summary>
    /// Modul PULGA TEGMAYDI (SPEC §4.1): amal yozish `payments`,
    /// `payment_allocations` va `ledger_entries` ni o'zgartirmaydi. Bu shart
    /// kodni o'qib emas, o'lchab tekshiriladi.
    /// </summary>
    [Fact]
    public async Task Amal_yozish_pul_jadvallariga_tegmaydi()
    {
        await using var db = await NewDbAsync("money");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var (cashierId, shiftId) = await SeedCashDeskAsync(db);
        var tuition = await CategoryIdAsync(db, "tuition");
        var studentId = await SeedStudentAsync(db, "Pul Test", "4-A");

        var invoice = NewInvoice(studentId, tuition, new DateOnly(2007, 12, 1), 900_000m);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        await PayAsync(db, studentId, cashierId, shiftId, 400_000m, [(invoice.Id, 400_000m)]);

        var before = await MoneyFingerprintAsync(db);

        var service = new DebtorWorkflowService(db);
        var action = await service.AddActionAsync(
            studentId,
            new CreateDebtorActionRequest("Qolganini keyingi oyda", PromisedStatusId, AppClock.Today.AddDays(2)),
            actorId);
        await service.DeleteActionAsync(action.Id, actorId);
        await service.RetireStatusAsync(PromisedStatusId, actorId);

        Assert.Equal(before, await MoneyFingerprintAsync(db));
    }

    /// <summary>
    /// Terishdagi xato: "2226-yil" deb yozilgan va'da hech qachon o'tib
    /// ketmaydi, ya'ni qarzdor ro'yxatdan JIMGINA chiqib ketardi.
    /// </summary>
    [Fact]
    public async Task Haqiqatga_oxshamagan_vada_sanasi_400()
    {
        await using var db = await NewDbAsync("range");
        var actorId = await SeedUserAsync(db, Roles.Admin);
        var studentId = await SeedStudentAsync(db, "Sana Test", "3-A");

        var service = new DebtorWorkflowService(db);
        var error = await Assert.ThrowsAsync<BillingRuleException>(
            () => service.AddActionAsync(
                studentId,
                new CreateDebtorActionRequest("Kelasi asrda", null, AppClock.Today.AddYears(200)),
                actorId));

        Assert.Equal(BillingFault.Invalid, error.Fault);
        Assert.Equal("promise_out_of_range", error.Code);
    }

    // =====================================================================
    //  Test ma'lumoti
    // =====================================================================

    /// <summary>Pul jadvallarining "barmoq izi" — o'zgarmaganini isbotlash uchun.</summary>
    private static async Task<(int Payments, int Allocations, int Ledger, decimal PaidTotal)>
        MoneyFingerprintAsync(AppDbContext db) => (
            await db.Payments.AsNoTracking().CountAsync(),
            await db.PaymentAllocations.AsNoTracking().CountAsync(),
            await db.LedgerEntries.AsNoTracking().CountAsync(),
            await db.PaymentAllocations.AsNoTracking().SumAsync(a => (decimal?)a.Amount) ?? 0m);

    /// <summary>
    /// Toza, migratsiya qo'llangan baza — shablondan nusxa (~100 ms).
    /// "Buzilgan va'da" butun jadval bo'ylab qaraydi, ya'ni umumiy bazada
    /// qo'shni testning qarzdori natijani o'zgartirib yuborardi.
    /// </summary>
    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("debtorwf_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static async Task<string> SeedUserAsync(AppDbContext db, string role)
    {
        var user = new AppUser
        {
            FullName = $"Test {role}",
            Role = role,
            Email = $"{role}.{Guid.NewGuid():N}",
        };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Kassir va uning ochiq smenasi — to'lov yozish uchun ikkovi ham SHART (SPEC §4.2).</summary>
    private static async Task<(string CashierId, Guid ShiftId)> SeedCashDeskAsync(AppDbContext db)
    {
        var cashierId = await SeedUserAsync(db, Roles.Cashier);
        var shift = new CashShift
        {
            CashierId = cashierId,
            OpenedAt = AppClock.NowInstant,
            OpeningFloat = 0m,
            Status = CashShiftStatus.Open,
        };
        db.CashShifts.Add(shift);
        await db.SaveChangesAsync();
        return (cashierId, shift.Id);
    }

    private static async Task<string> SeedStudentAsync(AppDbContext db, string fullName, string className)
    {
        var id = $"dw-{Guid.NewGuid():N}"[..16];
        db.Students.Add(NewStudent(id, fullName, className));
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> CategoryIdAsync(AppDbContext db, string code) =>
        await db.FeeCategories.AsNoTracking().Where(c => c.Code == code).Select(c => c.Id).SingleAsync();

    private static Student NewStudent(string id, string fullName, string className) => new()
    {
        Id = id,
        FullName = fullName,
        LastName = "Familiya",
        FirstName = "Ism",
        MiddleName = "Otasi",
        BirthDate = "2015-01-01",
        Address = "Toshkent",
        Gender = "male",
        ParentFullName = "Ota-ona",
        ParentLastName = "Familiya",
        ParentFirstName = "Ism",
        ParentMiddleName = "Otasi",
        ParentPhone = "+998901112233",
        ClassName = className,
        EnrollmentDate = "2007-09-01",
    };

    private static Invoice NewInvoice(
        string studentId, Guid categoryId, DateOnly periodMonth, decimal amount, decimal discount = 0m) => new()
        {
            StudentId = studentId,
            CategoryId = categoryId,
            PeriodMonth = periodMonth,
            Amount = amount,
            Discount = discount,
            DueOn = periodMonth.AddDays(9),
            Status = InvoiceStatus.Open,
            CreatedAt = AppClock.NowInstant,
        };

    /// <summary>To'lov + taqsimotlar (chek raqami smena ichida uzluksiz — SPEC §4.2).</summary>
    private static async Task PayAsync(
        AppDbContext db, string studentId, string cashierId, Guid shiftId, decimal amount,
        (Guid InvoiceId, decimal Amount)[] allocations)
    {
        var lastReceipt = await db.Payments
            .Where(p => p.CashShiftId == shiftId)
            .MaxAsync(p => (long?)p.ReceiptNo) ?? 0;

        var payment = new Payment
        {
            ReceiptNo = lastReceipt + 1,
            StudentId = studentId,
            Amount = amount,
            Method = PaymentMethod.Cash,
            CashShiftId = shiftId,
            CashierId = cashierId,
            ReceivedAt = AppClock.NowInstant,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        foreach (var (invoiceId, allocated) in allocations)
            db.PaymentAllocations.Add(new PaymentAllocation
            {
                PaymentId = payment.Id,
                InvoiceId = invoiceId,
                Amount = allocated,
            });

        await db.SaveChangesAsync();
    }
}
