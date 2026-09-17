using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

// ===========================================================================
//  O'QUVCHIGA PUL QAYTARISH — F1.05 (docs/modules/finance-parity.md §2.1.3,
//  §3.1 A3, §4 — slice S3).
// ===========================================================================
//
//  BU FAYLNI TASHKIL ETISH
//  ------------------------
//  1) Ruxsat darvozasi — kim so'raydi, kim tasdiqlaydi, "moliya ruxsatli
//     xodim" ham ikkalasini qila olmaydi (SPEC §4.3).
//  2) Pul yo'li — so'rov → tasdiq → jurnal (`debit receivable / credit
//     cash|bank`), avans kamayadi, naqd qaytarim smenadan chiqadi.
//  3) Ikki qavatli nazorat — SPEC §4.5: so'ragan o'zi tasdiqlay olmaydi.
//  4) STORNO — asl qator TEGILMAYDI, yangi (storno) qator so'raladi va
//     TASDIQLANADI; jurnal partiyasi teskari bo'ladi; sinov balansi HAR
//     IKKALA lahzada (qaytarimdan keyin va stornodan keyin) nolga teng.
//  5) MUVOFIQLIK — bitta qaytarimga ikkita BIR VAQTDAGI tasdiq BITTA
//     jurnal partiyasi qo'yishi kerak (SPEC §4: "6 concurrent approvals
//     producing 6 postings on a sibling feature" — bu yerda takrorlanmaydi).
//  6) BAZA DARAJASIDAGI QULF — `app_rw` `student_refunds` ni tahrirlay yoki
//     o'chira olmaydi (financial, REVOKE), va qaror BIR MARTA yoziladi
//     (`student_refunds_locked` trigger).
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class StudentRefundTests(ApiFixture fixture)
{
    private const string Refunds = "/api/admin/finance/refunds";
    private const string Shifts = "/api/cash/shifts";

    /// <summary>Huquq rad etilganda PostgreSQL qaytaradigan kod.</summary>
    private const string PermissionDenied = "42501";

    /// <summary><c>student_refunds_locked</c> trigger'ining ERRCODE'i.</summary>
    private const string CheckViolation = "23514";

    private sealed record ErrorBody(string Code, string Message);

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  1. RUXSAT DARVOZASI (SPEC §4.3) — kim so'raydi, kim tasdiqlaydi
    // =====================================================================

    /// <summary>
    /// Kassir, o'qituvchi va xodim (moliya ruxsati bor-yo'qligidan qat'i
    /// nazar — bu yerda klass darajasidagi ROL darvozasi, `AdminPermAttribute`
    /// emas) qaytarim bo'limiga UMUMAN kirmaydi: so'ray ham, tasdiqlay ham,
    /// ko'ra ham olmaydi. Mijoz talabi: "finance-permitted staff get neither".
    /// </summary>
    [Theory]
    [InlineData(Roles.Cashier)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Staff)]
    public async Task Kassir_oqituvchi_xodim_qaytarim_bolimiga_kira_olmaydi(string role)
    {
        var (_, client) = await ClientAsync(role);
        var studentId = await SeedStudentAsync();

        var request = await client.PostAsJsonAsync(Refunds, new
        {
            studentId, amount = 10_000m, method = PaymentMethod.Cash, reason = "Sinov",
        });
        Assert.Equal(HttpStatusCode.Forbidden, request.StatusCode);

        var list = await client.GetAsync(Refunds);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var approve = await client.PostAsJsonAsync($"{Refunds}/{Guid.NewGuid()}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);

        var reject = await client.PostAsJsonAsync(
            $"{Refunds}/{Guid.NewGuid()}/reject", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, reject.StatusCode);
    }

    /// <summary>Token'siz so'rov — 401, so'rash va tasdiqlash endpointlarida.</summary>
    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Refunds)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(
            Refunds, new { studentId = "x", amount = 1m, method = "cash", reason = "x" })).StatusCode);
    }

    /// <summary>
    /// Admin SO'RAY oladi, lekin TASDIQLAY olmaydi — bu direktorning amali
    /// (SPEC §4.5, finance-parity §2.1). Rol darvozasining o'zi (403), hali
    /// "o'zi so'ragan-tasdiqlagan" tekshiruviga yetmasdan.
    /// </summary>
    [Fact]
    public async Task Admin_soraydi_lekin_tasdiqlay_olmaydi()
    {
        var (_, admin) = await ClientAsync(Roles.Admin);
        var studentId = await SeedStudentWithAdvanceAsync(300_000m);

        var created = await RequestRefundAsync(admin, studentId, 100_000m, PaymentMethod.Card, "Ketdi");
        Assert.Equal(StudentRefundStatus.Pending, created.Status);

        var approve = await admin.PostAsJsonAsync($"{Refunds}/{created.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);

        var reject = await admin.PostAsJsonAsync($"{Refunds}/{created.Id}/reject", new { reason = "yo'q" });
        Assert.Equal(HttpStatusCode.Forbidden, reject.StatusCode);
    }

    // =====================================================================
    //  2. PUL YO'LI — so'rov → tasdiq → jurnal
    // =====================================================================

    /// <summary>
    /// <b>Modulning asosiy natijasi.</b> Admin so'raydi, direktor
    /// tasdiqlaydi → <c>debit receivable / credit bank</c> (karta usuli),
    /// avans AYNAN so'ralgan summaga kamayadi.
    /// </summary>
    [Fact]
    public async Task Tasdiqlangan_qaytarim_jurnalga_tushadi_va_avansni_kamaytiradi()
    {
        var (_, admin) = await ClientAsync(Roles.Admin);
        var (director, directorClient) = await ClientAsync(Roles.SuperAdmin);
        var studentId = await SeedStudentWithAdvanceAsync(500_000m);

        var created = await RequestRefundAsync(admin, studentId, 200_000m, PaymentMethod.Card, "Ko'chib ketdi");

        var response = await directorClient.PostAsJsonAsync($"{Refunds}/{created.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = (await response.Content.ReadFromJsonAsync<StudentRefundDto>())!;

        Assert.Equal(StudentRefundStatus.Approved, approved.Status);
        Assert.Equal(director.Id, approved.ApprovedBy);
        Assert.NotNull(approved.ApprovedAt);
        Assert.Null(approved.CashShiftId);   // karta — smenaga tegmaydi

        await using var db = NewDb();
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefType == LedgerRefType.Refund && e.RefId == created.Id)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        var debit = Assert.Single(entries, e => e.Direction == LedgerDirection.Debit);
        Assert.Equal(Accounts.Receivable, debit.Account);
        Assert.Equal(200_000m, debit.Amount);
        var credit = Assert.Single(entries, e => e.Direction == LedgerDirection.Credit);
        Assert.Equal(Accounts.Bank, credit.Account);
        Assert.Equal(200_000m, credit.Amount);
        Assert.All(entries, e => Assert.Equal(director.Id, e.CreatedBy));

        var advance = await new StudentBalanceQuery(db).AdvanceForAsync(studentId);
        Assert.Equal(300_000m, advance);   // 500 000 - 200 000
    }

    /// <summary>SPEC §4.5 — so'ragan shaxs o'zi tasdiqlay olmaydi, direktor bo'lsa ham.</summary>
    [Fact]
    public async Task Soragan_shaxs_ozi_tasdiqlay_olmaydi()
    {
        var (director, directorClient) = await ClientAsync(Roles.SuperAdmin);
        var studentId = await SeedStudentWithAdvanceAsync(200_000m);

        var created = await RequestRefundAsync(
            directorClient, studentId, 50_000m, PaymentMethod.Transfer, "Direktor o'zi so'radi");

        var response = await directorClient.PostAsJsonAsync($"{Refunds}/{created.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("self_approval", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        // Qaytarim hali PENDING — hech qanday pul harakati bo'lmadi.
        var reloaded = await GetRefundAsync(directorClient, created.Id);
        Assert.Equal(StudentRefundStatus.Pending, reloaded.Status);

        await using var db = NewDb();
        Assert.Empty(await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefType == LedgerRefType.Refund && e.RefId == created.Id).ToListAsync());
    }

    /// <summary>So'ralgan summa joriy avansdan katta — 400 <c>insufficient_advance</c>, qator yozilmaydi.</summary>
    [Fact]
    public async Task Avansdan_katta_sorov_400()
    {
        var (_, admin) = await ClientAsync(Roles.Admin);
        var studentId = await SeedStudentWithAdvanceAsync(100_000m);

        var response = await admin.PostAsJsonAsync(Refunds, new
        {
            studentId, amount = 150_000m, method = PaymentMethod.Card, reason = "Ortiqcha so'rov",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("insufficient_advance", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        await using var db = NewDb();
        Assert.Empty(await db.StudentRefunds.AsNoTracking().Where(r => r.StudentId == studentId).ToListAsync());
    }

    /// <summary>Sabab bo'sh — 400; o'quvchi topilmasa — 404.</summary>
    [Fact]
    public async Task Sababsiz_400_notogri_oquvchi_404()
    {
        var (_, admin) = await ClientAsync(Roles.Admin);
        var studentId = await SeedStudentWithAdvanceAsync(100_000m);

        var noReason = await admin.PostAsJsonAsync(Refunds, new
        {
            studentId, amount = 10_000m, method = PaymentMethod.Card, reason = "",
        });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal("reason_required", (await noReason.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        var noStudent = await admin.PostAsJsonAsync(Refunds, new
        {
            studentId = Guid.NewGuid().ToString(), amount = 10_000m, method = PaymentMethod.Card, reason = "x",
        });
        Assert.Equal(HttpStatusCode.NotFound, noStudent.StatusCode);
        Assert.Equal("student_not_found", (await noStudent.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    /// <summary>
    /// SPEC §4.4 — <c>requestedBy</c>, <c>approvedBy</c>, <c>cashShiftId</c> va
    /// h.k. so'rov tanasida kelsa — 400 <c>identity_in_body</c>, jimgina
    /// e'tiborsiz qoldirilmaydi.
    /// </summary>
    [Fact]
    public async Task Tanadagi_server_maydonlari_400_beradi()
    {
        var (admin, client) = await ClientAsync(Roles.Admin);
        var studentId = await SeedStudentWithAdvanceAsync(100_000m);

        var response = await client.PostAsJsonAsync(Refunds, new
        {
            studentId, amount = 10_000m, method = PaymentMethod.Card, reason = "x",
            requestedBy = admin.Id,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("identity_in_body", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    // =====================================================================
    //  3. NAQD QAYTARIM — smenadan chiqadi (F1.03/F1.04 bilan bir xil qoida)
    // =====================================================================

    /// <summary>Ochiq smenasiz naqd qaytarimni tasdiqlab bo'lmaydi — 409, qaror qolmaydi.</summary>
    [Fact]
    public async Task Ochiq_smenasiz_naqd_qaytarim_tasdiqlanmaydi()
    {
        var (_, admin) = await ClientAsync(Roles.Admin);
        var (_, director) = await ClientAsync(Roles.SuperAdmin);
        var studentId = await SeedStudentWithAdvanceAsync(400_000m);

        var created = await RequestRefundAsync(admin, studentId, 150_000m, PaymentMethod.Cash, "Naqd qaytarim");

        var response = await director.PostAsJsonAsync($"{Refunds}/{created.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("no_open_shift", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        var reloaded = await GetRefundAsync(director, created.Id);
        Assert.Equal(StudentRefundStatus.Pending, reloaded.Status);
    }

    /// <summary>
    /// Naqd qaytarim TASDIQLOVCHINING ochiq smenasidan chiqadi va uning
    /// kutilgan naqdini AYNAN shu summaga kamaytiradi (F1.03/F1.04 kabi).
    /// </summary>
    [Fact]
    public async Task Naqd_qaytarim_tasdiqlovchining_smenasidan_chiqadi()
    {
        var (_, admin) = await ClientAsync(Roles.Admin);
        var (director, directorClient) = await ClientAsync(Roles.SuperAdmin);
        var studentId = await SeedStudentWithAdvanceAsync(400_000m);

        var directorShift = await OpenShiftAsync(directorClient, 300_000m);

        var created = await RequestRefundAsync(admin, studentId, 120_000m, PaymentMethod.Cash, "Naqd qaytarim");
        var response = await directorClient.PostAsJsonAsync($"{Refunds}/{created.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = (await response.Content.ReadFromJsonAsync<StudentRefundDto>())!;

        Assert.Equal(directorShift.Id, approved.CashShiftId);

        var closed = await CloseShiftAsync(directorClient, directorShift.Id, countedCash: 180_000m);
        Assert.Equal(180_000m, closed.ExpectedCash);   // 300 000 - 120 000
        Assert.Equal(0m, closed.Variance);

        var report = await GetZReportAsync(directorClient, directorShift.Id);
        Assert.Equal(120_000m, report.CashRefundsTotal);
        Assert.Equal(1, report.CashRefundsCount);

        _ = director;
    }

    // =====================================================================
    //  4. STORNO — asl qator tegilmaydi, yangi qator so'raladi va tasdiqlanadi
    // =====================================================================

    /// <summary>
    /// TO'LIQ storno aylanasi: tasdiqlangan qaytarim → storno so'raladi →
    /// (BOSHQA direktor) tasdiqlaydi → jurnal partiyasi teskari bo'ladi,
    /// asl qator TEGILMAYDI, va SINOV BALANSI ikkala lahzada ham (qaytarim
    /// va storno) nolga teng.
    /// </summary>
    [Fact]
    public async Task Storno_aylanasi_jurnalni_teskari_qiladi_va_balans_nolga_teng()
    {
        var (_, admin) = await ClientAsync(Roles.Admin);
        var (firstDirector, firstDirectorClient) = await ClientAsync(Roles.SuperAdmin);
        var (secondDirector, secondDirectorClient) = await ClientAsync(Roles.SuperAdmin);
        var studentId = await SeedStudentWithAdvanceAsync(600_000m);

        // ---- 1) Oddiy qaytarim, tasdiqlangan ----
        var created = await RequestRefundAsync(admin, studentId, 250_000m, PaymentMethod.Transfer, "Ketdi");
        var approveResponse = await firstDirectorClient.PostAsJsonAsync($"{Refunds}/{created.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        await AssertTrialBalanceZeroAsync();

        await using (var db = NewDb())
        {
            var advanceAfterRefund = await new StudentBalanceQuery(db).AdvanceForAsync(studentId);
            Assert.Equal(350_000m, advanceAfterRefund);   // 600 000 - 250 000
        }

        // ---- 2) Storno so'raladi (admin) ----
        var reverseRequest = await admin.PostAsJsonAsync(
            $"{Refunds}/{created.Id}/reverse", new { reason = "Xato summa kiritilgan edi" });
        Assert.Equal(HttpStatusCode.OK, reverseRequest.StatusCode);
        var reversal = (await reverseRequest.Content.ReadFromJsonAsync<StudentRefundDto>())!;

        Assert.Equal(created.Id, reversal.ReversalOf);
        Assert.Equal(created.Amount, reversal.Amount);
        Assert.Equal(StudentRefundStatus.Pending, reversal.Status);

        // Asl qaytarim hali "approved" — storno hali TASDIQLANMAGAN.
        var stillApproved = await GetRefundAsync(firstDirectorClient, created.Id);
        Assert.Equal(StudentRefundStatus.Approved, stillApproved.Status);
        Assert.False(stillApproved.Reversed);

        // ---- 3) Ikkinchi direktor tasdiqlaydi ----
        var approveReversal = await secondDirectorClient.PostAsJsonAsync(
            $"{Refunds}/{reversal.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approveReversal.StatusCode);
        var approvedReversal = (await approveReversal.Content.ReadFromJsonAsync<StudentRefundDto>())!;
        Assert.Equal(StudentRefundStatus.Reversal, approvedReversal.Status);
        Assert.Equal(secondDirector.Id, approvedReversal.ApprovedBy);

        // ---- 4) Asl qator TEGILMAGAN, lekin endi "reversed" ----
        var finalOriginal = await GetRefundAsync(firstDirectorClient, created.Id);
        Assert.Equal(StudentRefundStatus.Reversed, finalOriginal.Status);
        Assert.True(finalOriginal.Reversed);
        Assert.Equal(250_000m, finalOriginal.Amount);   // summasi o'zgarmagan

        // ---- 5) Jurnal: ASL ikkita satr + KO'ZGU ikkita satr, hammasi ref_id = ASL id ----
        await using (var db = NewDb())
        {
            var entries = await db.LedgerEntries.AsNoTracking()
                .Where(e => e.RefType == LedgerRefType.Refund || e.RefType == LedgerRefType.Reversal)
                .Where(e => e.RefId == created.Id)
                .ToListAsync();
            Assert.Equal(4, entries.Count);
            Assert.Equal(2, entries.Count(e => e.RefType == LedgerRefType.Refund));
            Assert.Equal(2, entries.Count(e => e.RefType == LedgerRefType.Reversal));

            var mirrorDebit = Assert.Single(entries,
                e => e.RefType == LedgerRefType.Reversal && e.Direction == LedgerDirection.Debit);
            Assert.Equal(Accounts.Bank, mirrorDebit.Account);   // pul bankka QAYTDI
            var mirrorCredit = Assert.Single(entries,
                e => e.RefType == LedgerRefType.Reversal && e.Direction == LedgerDirection.Credit);
            Assert.Equal(Accounts.Receivable, mirrorCredit.Account);
        }

        // ---- 6) Avans qaytdi ----
        await using (var db = NewDb())
        {
            var advanceAfterReversal = await new StudentBalanceQuery(db).AdvanceForAsync(studentId);
            Assert.Equal(600_000m, advanceAfterReversal);   // to'liq qaytdi
        }

        // ---- 7) SINOV BALANSI — storno'dan KEYIN ham nolga teng ----
        await AssertTrialBalanceZeroAsync();
    }

    /// <summary>Bitta qaytarim uchun ikkinchi storno so'rovi — 409.</summary>
    [Fact]
    public async Task Ikkinchi_marta_storno_sorash_409()
    {
        var (_, admin) = await ClientAsync(Roles.Admin);
        var (_, director) = await ClientAsync(Roles.SuperAdmin);
        var studentId = await SeedStudentWithAdvanceAsync(300_000m);

        var created = await RequestRefundAsync(admin, studentId, 100_000m, PaymentMethod.Card, "x");
        await director.PostAsJsonAsync($"{Refunds}/{created.Id}/approve", new { });

        var first = await admin.PostAsJsonAsync($"{Refunds}/{created.Id}/reverse", new { reason = "Birinchi" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await admin.PostAsJsonAsync($"{Refunds}/{created.Id}/reverse", new { reason = "Ikkinchi" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_has_reversal_request",
            (await second.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    /// <summary>Hali tasdiqlanmagan (yoki rad etilgan) qaytarimni storno qilib bo'lmaydi — 409.</summary>
    [Fact]
    public async Task Tasdiqlanmagan_qaytarimni_storno_qilib_bolmaydi()
    {
        var (_, admin) = await ClientAsync(Roles.Admin);
        var studentId = await SeedStudentWithAdvanceAsync(300_000m);

        var created = await RequestRefundAsync(admin, studentId, 100_000m, PaymentMethod.Card, "x");

        var response = await admin.PostAsJsonAsync($"{Refunds}/{created.Id}/reverse", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("not_posted", (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    // =====================================================================
    //  5. RAD ETISH — pul harakati bo'lmaydi
    // =====================================================================

    /// <summary>Rad etilgan so'rov jurnalga tushmaydi va avansga tegmaydi.</summary>
    [Fact]
    public async Task Rad_etilgan_sorov_jurnalga_tushmaydi()
    {
        var (_, admin) = await ClientAsync(Roles.Admin);
        var (_, director) = await ClientAsync(Roles.SuperAdmin);
        var studentId = await SeedStudentWithAdvanceAsync(300_000m);

        var created = await RequestRefundAsync(admin, studentId, 100_000m, PaymentMethod.Card, "So'rov");

        var response = await director.PostAsJsonAsync(
            $"{Refunds}/{created.Id}/reject", new { reason = "Asossiz so'rov" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rejected = (await response.Content.ReadFromJsonAsync<StudentRefundDto>())!;
        Assert.Equal(StudentRefundStatus.Rejected, rejected.Status);
        Assert.Equal("Asossiz so'rov", rejected.RejectedReason);

        await using var db = NewDb();
        Assert.Empty(await db.LedgerEntries.AsNoTracking()
            .Where(e => e.RefType == LedgerRefType.Refund && e.RefId == created.Id).ToListAsync());

        var advance = await new StudentBalanceQuery(db).AdvanceForAsync(studentId);
        Assert.Equal(300_000m, advance);   // o'zgarmagan
    }

    /// <summary>Qaror allaqachon qabul qilingan qaytarimni yana tasdiqlab/rad etib bo'lmaydi — 409.</summary>
    [Fact]
    public async Task Qaror_qoyilgan_qaytarim_qayta_hal_qilinmaydi()
    {
        var (_, admin) = await ClientAsync(Roles.Admin);
        var (_, director) = await ClientAsync(Roles.SuperAdmin);
        var studentId = await SeedStudentWithAdvanceAsync(300_000m);

        var created = await RequestRefundAsync(admin, studentId, 100_000m, PaymentMethod.Card, "x");
        await director.PostAsJsonAsync($"{Refunds}/{created.Id}/approve", new { });

        var reapprove = await director.PostAsJsonAsync($"{Refunds}/{created.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, reapprove.StatusCode);
        Assert.Equal("already_decided", (await reapprove.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        var reject = await director.PostAsJsonAsync($"{Refunds}/{created.Id}/reject", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
        Assert.Equal("already_decided", (await reject.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
    }

    // =====================================================================
    //  6. MUVOFIQLIK — 6 ta bir vaqtdagi tasdiq BITTA jurnal partiyasi qo'yadi
    // =====================================================================

    /// <summary>
    /// <b>SPEC §4 ning "measured failure mode"i:</b> boshqa bir moliya
    /// amalida 6 ta bir vaqtdagi tasdiq 6 ta jurnal yozuvi berib qo'ygan.
    /// Bu yerda AYNAN o'sha stsenariy — bitta qaytarimga bir vaqtda 6 ta
    /// tasdiq so'rovi — advisory lock tufayli faqat BITTASI o'tishi, qolgan
    /// beshtasi <b>409 already_decided</b> olishi va bazada FAQAT bitta
    /// jurnal partiyasi (ikkita satr) qolishi shart.
    ///
    /// <para>
    /// Usul — KARTA (bank hisobidan): naqd bo'lganda har chaqiruv o'z ochiq
    /// smenasini talab qilardi, bu yerda esa sinalayotgan narsa — smena
    /// emas, AYNAN qaytarim qatoriga qulf.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Olti_bir_vaqtdagi_tasdiq_bitta_jurnal_partiyasi_qoyadi()
    {
        const int callers = 6;

        // Alohida baza va kengaytirilgan pool: umumiy test bazasi satri
        // `MaxPoolSize = 10` bilan cheklangan (`PostgresFixture`) —
        // `ReceiptNumberingTests` dagi bilan bir xil sabab. Sahna ham shu
        // yerda, xizmat qatlamida to'g'ridan-to'g'ri quriladi (HTTP orqali
        // emas): sinalayotgan narsa — bitta qaytarim qatoriga qulf, marshrut
        // emas.
        var database = await fixture.Postgres.CreateDatabaseAsync("refundconcurrency");
        var connectionString = WithPoolSize(database.OwnerConnectionString, callers + 5);

        var (refundId, directorId) = await ArrangeConcurrencySceneAsync(connectionString, 400_000m);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = 0;
        var peak = 0;

        var tasks = Enumerable.Range(0, callers).Select(async _ =>
        {
            await start.Task;
            var now = Interlocked.Increment(ref inFlight);
            RecordPeak(ref peak, now);
            try
            {
                await using var db = PostgresFixture.NewContext(connectionString);
                var service = new StudentRefundService(db, new LedgerService(db), new CashShiftService(db));
                try
                {
                    await service.ApproveAsync(refundId, directorId);
                    return "ok";
                }
                catch (BillingRuleException ex)
                {
                    return ex.Code;
                }
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        }).ToList();

        start.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.True(peak >= 2,
            $"Bir vaqtda ko'pi bilan {peak} ta tasdiq ishlagan — vazifalar ketma-ket yugurgan bo'lishi mumkin.");

        Assert.Equal(1, results.Count(r => r == "ok"));
        Assert.Equal(callers - 1, results.Count(r => r == "already_decided"));

        await using var check = PostgresFixture.NewContext(connectionString);
        var entries = await check.LedgerEntries.AsNoTracking()
            .Where(e => e.RefType == LedgerRefType.Refund && e.RefId == refundId)
            .ToListAsync();
        Assert.Equal(2, entries.Count);   // BITTA partiya — 6 ta EMAS.

        var stored = await check.StudentRefunds.AsNoTracking().SingleAsync(r => r.Id == refundId);
        Assert.Equal(directorId, stored.ApprovedBy);
    }

    /// <summary>
    /// Muvofiqlik sinovi uchun mustaqil sahna: direktor, admin (so'ragan) va
    /// KARTA usulidagi PENDING qaytarim — to'g'ridan-to'g'ri EGA ulanish
    /// bilan yoziladi (`app_rw` emas — bu yerda HTTP oqimi sinalmayapti).
    /// </summary>
    private static async Task<(Guid RefundId, string DirectorId)> ArrangeConcurrencySceneAsync(
        string connectionString, decimal amount)
    {
        await using var db = PostgresFixture.NewContext(connectionString);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var admin = new AppUser { FullName = $"Admin {suffix}", Role = Roles.Admin, Email = $"admin.{suffix}" };
        var director = new AppUser
        {
            FullName = $"Direktor {suffix}", Role = Roles.SuperAdmin, Email = $"director.{suffix}",
        };
        db.Users.AddRange(admin, director);

        var student = new Student
        {
            FullName = $"Muvofiqlik o'quvchisi {suffix}",
            LastName = "Muvofiqlik",
            FirstName = "O'quvchi",
            ClassName = "1-A",
            EnrollmentDate = AppClock.Today.ToString("yyyy-MM-dd"),
        };
        db.Students.Add(student);

        // AVANS: taqsimotsiz to'lov — `StudentBalanceQuery.AdvanceForAsync`
        // shundan hisoblaydi (`ApproveAsync` HAQIQIY tekshiruvni qayta
        // bajaradi, o'quvchi qulfi ostida). Smena — FAQAT FK talabi uchun,
        // PaymentService orqali emas, to'g'ridan-to'g'ri yoziladi.
        var seedShift = new CashShift
        {
            CashierId = admin.Id,
            OpenedAt = AppClock.NowInstant,
            OpeningFloat = 0m,
            Status = CashShiftStatus.Open,
        };
        db.CashShifts.Add(seedShift);
        db.Payments.Add(new Payment
        {
            ReceiptNo = 1,
            StudentId = student.Id,
            Amount = amount,
            Method = PaymentMethod.Cash,
            CashShiftId = seedShift.Id,
            CashierId = admin.Id,
            ReceivedAt = AppClock.NowInstant,
        });

        var refund = new StudentRefund
        {
            StudentId = student.Id,
            Amount = amount,
            Method = PaymentMethod.Card,
            Reason = "Muvofiqlik sinovi",
            RequestedBy = admin.Id,
            RequestedAt = AppClock.NowInstant,
        };
        db.StudentRefunds.Add(refund);

        await db.SaveChangesAsync();
        return (refund.Id, director.Id);
    }

    // =====================================================================
    //  7. BAZA DARAJASIDAGI QULF — `student_refunds` financial, REVOKE
    // =====================================================================

    /// <summary>
    /// <c>student_refunds</c> — FAQAT INSERT + to'rtta "qaror" ustuniga
    /// BIR MARTALIK UPDATE (finance-parity §3.1 A3). <c>app_rw</c> bilan
    /// summani o'zgartirish yoki qatorni o'chirish — <b>42501</b>.
    /// </summary>
    [Fact]
    public async Task Appraw_summani_ozgartira_ham_qatorni_ochira_ham_olmaydi()
    {
        var (_, admin) = await ClientAsync(Roles.Admin);
        var studentId = await SeedStudentWithAdvanceAsync(300_000m);
        var created = await RequestRefundAsync(admin, studentId, 100_000m, PaymentMethod.Card, "x");

        await using var connection = new NpgsqlConnection(fixture.Database.AppRwConnectionString);
        await connection.OpenAsync();

        foreach (var sql in new[]
                 {
                     "UPDATE student_refunds SET amount = 1 WHERE id = @id",
                     "DELETE FROM student_refunds WHERE id = @id",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", created.Id);

            var pg = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PermissionDenied, pg.SqlState);
        }

        await using var db = NewDb();
        var stored = await db.StudentRefunds.AsNoTracking().FirstAsync(r => r.Id == created.Id);
        Assert.Equal(100_000m, stored.Amount);
    }

    /// <summary>
    /// <b>Qaror BIR MARTA yoziladi.</b> Tasdiqlangandan keyin
    /// <c>approved_by</c> ni (ustun darajasida GRANT bo'lsa ham) qayta
    /// yozishga urinish — <c>student_refunds_locked</c> trigger tomonidan
    /// <b>23514</b> bilan rad etiladi. Bu qulf GRANT'dan KUCHLIROQ: hatto
    /// EGA rol ham (migratsiya) bu trigger'dan chetlab o'ta olmaydi.
    /// </summary>
    [Fact]
    public async Task Tasdiqlangan_qaror_qayta_yozilmaydi_trigger_bilan()
    {
        var (_, admin) = await ClientAsync(Roles.Admin);
        var (director, directorClient) = await ClientAsync(Roles.SuperAdmin);
        var studentId = await SeedStudentWithAdvanceAsync(300_000m);

        var created = await RequestRefundAsync(admin, studentId, 100_000m, PaymentMethod.Card, "x");
        await directorClient.PostAsJsonAsync($"{Refunds}/{created.Id}/approve", new { });

        await using var connection = new NpgsqlConnection(fixture.Database.AppRwConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "UPDATE student_refunds SET approved_at = now() WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", created.Id);

        var pg = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(CheckViolation, pg.SqlState);

        _ = director;
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private async Task<StudentRefundDto> RequestRefundAsync(
        HttpClient client, string studentId, decimal amount, string method, string reason)
    {
        var response = await client.PostAsJsonAsync(Refunds, new { studentId, amount, method, reason });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StudentRefundDto>())!;
    }

    private static async Task<StudentRefundDto> GetRefundAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"{Refunds}/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StudentRefundDto>())!;
    }

    private static async Task<CashShiftDto> OpenShiftAsync(HttpClient client, decimal openingFloat)
    {
        var response = await client.PostAsJsonAsync($"{Shifts}/open", new { openingFloat });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashShiftDto>())!;
    }

    private static async Task<CashShiftDto> CloseShiftAsync(HttpClient client, Guid shiftId, decimal countedCash)
    {
        var response = await client.PostAsJsonAsync(
            $"{Shifts}/{shiftId}/close", new { countedCash, note = (string?)null });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashShiftDto>())!;
    }

    private static async Task<ZReportDto> GetZReportAsync(HttpClient client, Guid shiftId)
    {
        var response = await client.GetAsync($"{Shifts}/{shiftId}/z-report");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ZReportDto>())!;
    }

    /// <summary>Butun jurnal (davrdan qat'i nazar) balansda — SPEC §4.1.</summary>
    private async Task AssertTrialBalanceZeroAsync()
    {
        await using var db = NewDb();
        var trial = await new LedgerService(db).TrialBalanceAsync();
        Assert.Equal(trial.Sum(t => t.Debit), trial.Sum(t => t.Credit));
    }

    /// <summary>Sof o'quvchi qatori — pul harakatisiz.</summary>
    private async Task<string> SeedStudentAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var studentId = "";
        await fixture.Api.WithDbAsync(async db =>
        {
            var student = new Student
            {
                FullName = $"Qaytarim o'quvchisi {suffix}",
                LastName = "Qaytarim",
                FirstName = "O'quvchi",
                ClassName = "1-A",
                EnrollmentDate = AppClock.Today.ToString("yyyy-MM-dd"),
            };
            db.Students.Add(student);
            await db.SaveChangesAsync();
            studentId = student.Id;
        });
        return studentId;
    }

    /// <summary>
    /// O'quvchi + AVANS: alohida "to'lovchi" admin o'z smenasini ochib,
    /// taqsimotsiz (bo'sh <c>allocations</c>) to'lov qiladi — butun summa
    /// taqsimlanmagan bo'lib qoladi, ya'ni AYNAN avans (`StudentBalanceQuery`
    /// ta'rifi). Hisob-faktura kerak emas: qaytarim faqat AVANSGA tegadi.
    /// </summary>
    private async Task<string> SeedStudentWithAdvanceAsync(decimal advance)
    {
        var studentId = await SeedStudentAsync();

        var (_, payer) = await ClientAsync(Roles.Admin);
        await OpenShiftAsync(payer, openingFloat: 0m);

        var payment = await payer.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId,
            amount = advance,
            method = PaymentMethod.Cash,
            allocations = Array.Empty<object>(),
        });
        Assert.Equal(HttpStatusCode.OK, payment.StatusCode);

        return studentId;
    }

    private static void RecordPeak(ref int peak, int candidate)
    {
        int seen;
        while (candidate > Volatile.Read(ref peak))
        {
            seen = Volatile.Read(ref peak);
            if (Interlocked.CompareExchange(ref peak, candidate, seen) == seen) break;
        }
    }

    private static string WithPoolSize(string connectionString, int maxPoolSize) =>
        new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = maxPoolSize }.ConnectionString;

    private async Task<(AppUser User, HttpClient Client)> ClientAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        var client = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));
        return (user, client);
    }
}
