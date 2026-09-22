using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

// ===========================================================================
//  KASSA BALANSI VA JURNALI — UCHALA MANBADAN (ishlab chiqarishdagi nosozlik,
//  2026-09-22).
// ===========================================================================
//
//  NOSOZLIK. Kassir "Kirim" bosdi, o'quvchini tanladi, saqladi. Pul
//  o'quvchining balansiga tushdi (to'g'ri), Kassa ekrani esa "0 so'm" va
//  "Tanlangan davrda tranzaksiya yo'q" deb turaverdi. Bazada uchta `payments`
//  qatori `cash_box_id` bilan yotardi, `cash_box_transactions` da esa 19-sentabrdan
//  beri bitta ham qator yo'q edi. Ya'ni YOZISH to'g'ri, O'QISH uchta manbaning
//  bittasini ko'rardi.
//
//  SHUNING UCHUN BU TESTLAR PULNI HTTP ORQALI KIRITADI va natijani AYNAN
//  kassir ko'radigan ikkita javobdan o'qiydi: `GET /admin/cash-boxes`
//  (balans) va `GET /admin/cash-boxes/transactions` (jurnal). Xizmatni
//  to'g'ridan-to'g'ri chaqirish bu nosozlikni USHLAMAGAN bo'lardi — u
//  ekranga boradigan yo'lda edi.
//
//  QAYTARIM BAZAGA TO'G'RIDAN-TO'G'RI YOZILADI: `StudentRefundService` hali
//  `cash_box_id` ni to'ldirmaydi (u smena davridan qolgan `cash_shift_id` ni
//  yozadi — `Domain/CashDesk.cs` izohi). Ustun esa MAVJUD va o'qish tomoni
//  uni sanashi shart, aks holda qaytarim xizmati kassaga ulangan kuni balans
//  yana jimgina yolg'on gapira boshlardi.
// ===========================================================================

/// <summary>
/// Kassaga biriktirilgan to'lov, chiqim va qaytarim kassaning BALANSIDA va
/// JURNALIDA ko'rinadi. Batafsil: fayl boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class CashBoxLedgerSourcesTests(ApiFixture fixture)
{
    private const string Boxes = "/api/admin/cash-boxes";
    private const string Payments = "/api/cash/payments";
    private const string Expenses = "/api/admin/expenses";

    /// <summary>Tasdiq chegarasidan (5 000 000) PAST — chiqim darhol jurnalga tushsin.</summary>
    private const decimal SmallExpense = 300_000m;

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =====================================================================
    //  1. O'quvchi to'lovi — AYNAN o'sha kassada
    // =====================================================================

    /// <summary>
    /// <b>Nosozlikning o'zi.</b> Kassa A'da qabul qilingan to'lov A'ning
    /// balansiga tushadi va A'ning jurnalida o'quvchining ismi, chek raqami
    /// va usuli bilan ko'rinadi.
    /// </summary>
    [Fact]
    public async Task Oquvchi_tolovi_kassa_balansiga_tushadi_va_jurnalda_korinadi()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var (_, cashier) = await ActorAsync(Roles.Cashier);
        var box = await CreateBoxAsync(admin);
        var student = await NewStudentAsync();

        var payment = await PayAsync(cashier, student.Id, box.Id, 750_000m, PaymentMethod.Cash);

        var reloaded = await GetBoxAsync(admin, box.Id);
        Assert.Equal(750_000m, reloaded.Balance);
        Assert.Equal(750_000m, reloaded.ByMethod[PaymentMethod.Cash]);

        var page = await LedgerAsync(admin, box.Id);
        var row = Assert.Single(page.Rows);

        Assert.Equal(payment.Id, row.Id);
        Assert.Equal(CashBoxLedgerKind.StudentPayment, row.Kind);
        // "KIM" ustuni — O'QUVCHI (pul kimdan keldi), kassir emas.
        Assert.Equal(student.FullName, row.Who);
        Assert.Equal(payment.ReceiptNo, row.ReceiptNo);
        Assert.Equal(PaymentMethod.Cash, row.Method);
        Assert.Equal(CashBoxRowStatus.Posted, row.Status);
        Assert.Equal(750_000m, row.Amount);
        Assert.Equal(AppClock.Today, row.Date);

        Assert.Equal(750_000m, page.InTotal);
        Assert.Equal(0m, page.OutTotal);
        Assert.Equal(750_000m, page.TotalsByMethod[PaymentMethod.Cash]);
    }

    /// <summary>
    /// To'lov FAQAT o'z kassasiga tegadi. Nazorat tekshiruvi: "hamma
    /// to'lovni hamma kassaga qo'shib yubor" degan implementatsiya
    /// yuqoridagi test bilan ham yashil bo'lardi.
    /// </summary>
    [Fact]
    public async Task Oquvchi_tolovi_boshqa_kassaga_tegmaydi()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var (_, cashier) = await ActorAsync(Roles.Cashier);
        var boxA = await CreateBoxAsync(admin);
        var boxB = await CreateBoxAsync(admin);
        var student = await NewStudentAsync();

        await PayAsync(cashier, student.Id, boxA.Id, 500_000m, PaymentMethod.Cash);

        var b = await GetBoxAsync(admin, boxB.Id);
        Assert.Equal(0m, b.Balance);
        Assert.All(b.ByMethod.Values, v => Assert.Equal(0m, v));
        Assert.Empty((await LedgerAsync(admin, boxB.Id)).Rows);
    }

    // =====================================================================
    //  2. Storno — pul qaytadi, qator YASHIRILMAYDI (SPEC §4.1)
    // =====================================================================

    /// <summary>
    /// Storno so'rovida kassa KO'RSATILMASA — pul ASL kassaga qaytadi, sukutdagi
    /// kassaga emas (mijoz, 2026-09-22: "birniki birinikiga o'tmasin"). Ilgari
    /// bu yerda sukut kassa turardi va bitta storno ikkita kassani birdan
    /// noto'g'ri qilib qo'yardi: birida ortiqcha pul, ikkinchisida yetishmovchilik.
    /// </summary>
    [Fact]
    public async Task Storno_kassa_korsatilmasa_asl_kassaga_qaytadi()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var (_, cashier) = await ActorAsync(Roles.Cashier);
        var box = await CreateBoxAsync(admin);
        var student = await NewStudentAsync();

        var payment = await PayAsync(cashier, student.Id, box.Id, 250_000m, PaymentMethod.Cash);
        Assert.Equal(250_000m, (await GetBoxAsync(admin, box.Id)).Balance);

        // `cashBoxId` ATAYLAB yuborilmaydi.
        var reverse = await admin.PostAsJsonAsync(
            $"/api/admin/payments/{payment.Id}/reverse", new { reason = "Xato kiritildi" });
        Assert.Equal(HttpStatusCode.OK, reverse.StatusCode);

        Assert.Equal(0m, (await GetBoxAsync(admin, box.Id)).Balance);

        // Ikkala qator ham SHU kassaning jurnalida — storno boshqa kassaga ketmagan.
        var page = await LedgerAsync(admin, box.Id);
        Assert.Equal(2, page.Rows.Count);
        Assert.Contains(page.Rows, r => r.Id == payment.Id && r.Status == CashBoxRowStatus.Cancelled);
        Assert.Contains(page.Rows, r => r.Id != payment.Id && r.Status == CashBoxRowStatus.Reversal);
    }

    /// <summary>
    /// Storno qilingan to'lov balansni OSHIRMAYDI (sof 0), lekin jurnalda
    /// IKKALA qator ham turadi: asli "bekor qilindi", stornoning o'zi
    /// "storno" holatida — ekrandagi "HOLATI" ustuni shu ikkisini ajratadi.
    /// </summary>
    [Fact]
    public async Task Storno_qilingan_tolov_balansni_oshirmaydi_ikkala_qator_ham_korinadi()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var (_, cashier) = await ActorAsync(Roles.Cashier);
        var box = await CreateBoxAsync(admin);
        var student = await NewStudentAsync();

        var payment = await PayAsync(cashier, student.Id, box.Id, 400_000m, PaymentMethod.Cash);
        Assert.Equal(400_000m, (await GetBoxAsync(admin, box.Id)).Balance);

        // Storno — kassirdan BOSHQA shaxs (SPEC §4.5) va AYNAN o'sha kassaga.
        var reverse = await admin.PostAsJsonAsync(
            $"/api/admin/payments/{payment.Id}/reverse",
            new { reason = "Ikki marta kiritilgan", cashBoxId = box.Id });
        Assert.Equal(HttpStatusCode.OK, reverse.StatusCode);

        Assert.Equal(0m, (await GetBoxAsync(admin, box.Id)).Balance);

        var page = await LedgerAsync(admin, box.Id);
        Assert.Equal(2, page.Rows.Count);

        var original = page.Rows.Single(r => r.Id == payment.Id);
        Assert.Equal(CashBoxRowStatus.Cancelled, original.Status);
        Assert.Equal(400_000m, original.Amount);
        Assert.Equal("Ikki marta kiritilgan", original.CancelReason);

        var storno = page.Rows.Single(r => r.Id != payment.Id);
        Assert.Equal(CashBoxRowStatus.Reversal, storno.Status);
        Assert.Equal(CashBoxLedgerKind.StudentPayment, storno.Kind);
        // Stornoning izohi AYNAN sabab — ikkala ustunda bir xil matn turmasin.
        Assert.Null(storno.Note);
        Assert.Equal("Ikki marta kiritilgan", storno.CancelReason);

        Assert.Equal(0m, page.InTotal);
        Assert.Equal(0m, page.TotalsByMethod[PaymentMethod.Cash]);
    }

    // =====================================================================
    //  3. Chiqim va qaytarim — pul javondan CHIQADI
    // =====================================================================

    /// <summary>
    /// Naqd chiqim (`expenses.cash_box_id`) va tasdiqlangan qaytarim
    /// (`student_refunds.cash_box_id`) kassaning balansini KAMAYTIRADI va
    /// jurnalda o'z turi bilan ko'rinadi. So'ralgan, lekin hali
    /// TASDIQLANMAGAN qaytarim esa hech narsaga tegmaydi — javondan pul
    /// chiqmagan.
    /// </summary>
    [Fact]
    public async Task Chiqim_va_qaytarim_kassa_balansini_kamaytiradi()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var (requester, _) = await ActorAsync(Roles.Admin);
        var (approver, _) = await ActorAsync(Roles.Admin);
        var (_, cashier) = await ActorAsync(Roles.Cashier);
        var box = await CreateBoxAsync(admin);
        var student = await NewStudentAsync();

        await PayAsync(cashier, student.Id, box.Id, 1_000_000m, PaymentMethod.Cash);
        var expense = await ExpenseAsync(admin, box.Id, SmallExpense);
        var refund = await SeedRefundAsync(
            student.Id, box.Id, 200_000m, PaymentMethod.Card,
            requester.Id, approver.Id, approved: true);
        // Tasdiqlanmagan (so'ralgan) qaytarim — balansga KIRMAYDI.
        await SeedRefundAsync(
            student.Id, box.Id, 50_000m, PaymentMethod.Card,
            requester.Id, approver.Id, approved: false);

        var reloaded = await GetBoxAsync(admin, box.Id);
        Assert.Equal(500_000m, reloaded.Balance);                                // 1 000 000 − 300 000 − 200 000
        Assert.Equal(700_000m, reloaded.ByMethod[PaymentMethod.Cash]);           // to'lov − chiqim (chiqim doim naqd)
        Assert.Equal(-200_000m, reloaded.ByMethod[PaymentMethod.Card]);          // qaytarim o'z usuli bilan

        var page = await LedgerAsync(admin, box.Id);
        Assert.Equal(3, page.Rows.Count);

        var expenseRow = page.Rows.Single(r => r.Id == expense.Id);
        Assert.Equal(CashBoxLedgerKind.Expense, expenseRow.Kind);
        Assert.Equal(SmallExpense, expenseRow.Amount);
        Assert.Equal(PaymentMethod.Cash, expenseRow.Method);
        Assert.Equal(CashBoxRowStatus.Posted, expenseRow.Status);

        var refundRow = page.Rows.Single(r => r.Id == refund);
        Assert.Equal(CashBoxLedgerKind.Refund, refundRow.Kind);
        Assert.Equal(200_000m, refundRow.Amount);
        Assert.Equal(PaymentMethod.Card, refundRow.Method);
        Assert.Equal(student.FullName, refundRow.Who);

        Assert.Equal(1_000_000m, page.InTotal);
        Assert.Equal(500_000m, page.OutTotal);
    }

    /// <summary>
    /// Storno qilingan chiqim javondagi pulga TA'SIR QILMAYDI, lekin
    /// jadvaldan YO'QOLMAYDI: o'z summasi bilan, "bekor qilindi" holatida va
    /// sababi bilan turadi. (`expenses` da storno QATORI yo'q — u faqat
    /// jurnalda, `ExpenseService.ReverseAsync` izohi.)
    /// </summary>
    [Fact]
    public async Task Storno_qilingan_chiqim_balansga_qaytadi_lekin_jadvalda_qoladi()
    {
        var (_, author) = await ActorAsync(Roles.Admin);
        // Chiqim stornosi — direktorniki (`FinanceMatrix`: ApproveExpense).
        var (_, approver) = await ActorAsync(Roles.SuperAdmin);
        var box = await CreateBoxAsync(author);

        var expense = await ExpenseAsync(author, box.Id, SmallExpense);
        Assert.Equal(-SmallExpense, (await GetBoxAsync(author, box.Id)).Balance);

        // Storno — chiqimni YOZGAN odam emas (SPEC §4.5).
        var reverse = await approver.PostAsJsonAsync(
            $"{Expenses}/{expense.Id}/reverse", new { reason = "Hujjat noto'g'ri" });
        Assert.Equal(HttpStatusCode.OK, reverse.StatusCode);

        Assert.Equal(0m, (await GetBoxAsync(author, box.Id)).Balance);

        var row = Assert.Single((await LedgerAsync(author, box.Id)).Rows);
        Assert.Equal(expense.Id, row.Id);
        Assert.Equal(SmallExpense, row.Amount);
        Assert.Equal(CashBoxRowStatus.Cancelled, row.Status);
        Assert.Equal("Hujjat noto'g'ri", row.CancelReason);
    }

    // =====================================================================
    //  4. `cash_box_id` NULL — HECH QAYSI kassaga taxmin qilinmaydi
    // =====================================================================

    /// <summary>
    /// Kassasiz (eski, smena davridagi) to'lov na so'ralgan kassada, na
    /// SUKUT kassada ko'rinadi. Bu <c>CashBoxModel.cs</c> dagi ataylab
    /// qo'yilgan qoida: "qaysidir kassadir" degan taxmin bugungi balansni
    /// orqaga qarab buzardi.
    /// </summary>
    [Fact]
    public async Task Kassasiz_tolov_hech_qaysi_kassada_korinmaydi()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var (cashier, _) = await ActorAsync(Roles.Cashier);
        var box = await CreateBoxAsync(admin);
        var student = await NewStudentAsync();

        var defaultBox = (await ListBoxesAsync(admin)).Single(b => b.IsDefault);
        var defaultBefore = defaultBox.Balance;

        var orphan = new Payment
        {
            // Chek raqami kassa bo'yicha unikal; `cash_box_id` NULL bo'lganda
            // PostgreSQL NULL'larni farqli deb biladi, ya'ni indeks to'sqinlik
            // qilmaydi (`CashBoxModel.ConfigurePaymentCashBox` izohi).
            ReceiptNo = 1,
            StudentId = student.Id,
            Amount = 999_000m,
            Method = PaymentMethod.Cash,
            CashShiftId = null,
            CashBoxId = null,
            CashierId = cashier.Id,
            Note = "Smena davridagi eski qator",
            ReceivedAt = AppClock.NowInstant,
        };

        await using (var db = NewDb())
        {
            db.Payments.Add(orphan);
            await db.SaveChangesAsync();
        }

        Assert.Equal(0m, (await GetBoxAsync(admin, box.Id)).Balance);
        Assert.Empty((await LedgerAsync(admin, box.Id)).Rows);

        var defaultAfter = (await ListBoxesAsync(admin)).Single(b => b.Id == defaultBox.Id);
        Assert.Equal(defaultBefore, defaultAfter.Balance);
        Assert.DoesNotContain(
            (await LedgerAsync(admin, defaultBox.Id)).Rows, r => r.Id == orphan.Id);
    }

    // =====================================================================
    //  5. Usul kesimi va sana filtri
    // =====================================================================

    /// <summary>
    /// Har bir to'lov O'Z usuli ustuniga tushadi (naqd / karta / o'tkazma /
    /// onlayn) — ekrandagi usul kartochkalari shu qiymatlarni ko'rsatadi.
    /// </summary>
    [Fact]
    public async Task Usul_kesimi_har_bir_tolov_usulini_alohida_sanaydi()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var (_, cashier) = await ActorAsync(Roles.Cashier);
        var box = await CreateBoxAsync(admin);
        var student = await NewStudentAsync();

        await PayAsync(cashier, student.Id, box.Id, 500_000m, PaymentMethod.Cash);
        await PayAsync(cashier, student.Id, box.Id, 300_000m, PaymentMethod.Card);
        await PayAsync(cashier, student.Id, box.Id, 100_000m, PaymentMethod.Transfer);
        await PayAsync(cashier, student.Id, box.Id, 50_000m, PaymentMethod.Online);

        var reloaded = await GetBoxAsync(admin, box.Id);
        Assert.Equal(500_000m, reloaded.ByMethod[PaymentMethod.Cash]);
        Assert.Equal(300_000m, reloaded.ByMethod[PaymentMethod.Card]);
        Assert.Equal(100_000m, reloaded.ByMethod[PaymentMethod.Transfer]);
        Assert.Equal(50_000m, reloaded.ByMethod[PaymentMethod.Online]);
        Assert.Equal(950_000m, reloaded.Balance);

        var page = await LedgerAsync(admin, box.Id);
        Assert.Equal(950_000m, page.TotalsByMethod.Values.Sum());
    }

    /// <summary>
    /// Kunlik filtr to'lov qatorlarini ham keser: orqadagi sana bilan
    /// yozilgan to'lov O'SHA kunning ro'yxatida turadi, bugungisida emas.
    /// </summary>
    [Fact]
    public async Task Jurnal_sana_filtri_tolov_qatorlarini_ham_keser()
    {
        var (_, admin) = await ActorAsync(Roles.Admin);
        var (_, cashier) = await ActorAsync(Roles.Cashier);
        var box = await CreateBoxAsync(admin);
        var student = await NewStudentAsync();

        var earlier = AppClock.Today.AddDays(-3);
        var today = await PayAsync(cashier, student.Id, box.Id, 100_000m, PaymentMethod.Cash);
        var backdated = await PayAsync(
            cashier, student.Id, box.Id, 200_000m, PaymentMethod.Cash, receivedOn: earlier);

        var todayOnly = await LedgerAsync(admin, box.Id, AppClock.Today, AppClock.Today);
        Assert.Equal(today.Id, Assert.Single(todayOnly.Rows).Id);
        Assert.Equal(100_000m, todayOnly.InTotal);

        var earlierOnly = await LedgerAsync(admin, box.Id, earlier, earlier);
        Assert.Equal(backdated.Id, Assert.Single(earlierOnly.Rows).Id);
        Assert.Equal(earlier, Assert.Single(earlierOnly.Rows).Date);
        Assert.Equal(200_000m, earlierOnly.InTotal);

        var whole = await LedgerAsync(admin, box.Id, earlier, AppClock.Today);
        Assert.Equal(2, whole.Rows.Count);
        Assert.Equal(300_000m, whole.InTotal);
        // Eng yangisi ustida — ro'yxat vaqt bo'yicha kamayish tartibida.
        Assert.Equal(today.Id, whole.Rows[0].Id);
        Assert.Equal(1, whole.Rows[0].No);
        Assert.Equal(2, whole.Rows[1].No);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private async Task<(AppUser User, HttpClient Client)> ActorAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        var client = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));
        return (user, client);
    }

    private static async Task<CashBoxDto> CreateBoxAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(Boxes, new
        {
            name = "Jurnal kassasi " + Guid.NewGuid().ToString("N")[..8],
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashBoxDto>())!;
    }

    private static async Task<List<CashBoxDto>> ListBoxesAsync(HttpClient client) =>
        (await (await client.GetAsync(Boxes)).Content.ReadFromJsonAsync<List<CashBoxDto>>())!;

    private static async Task<CashBoxDto> GetBoxAsync(HttpClient client, Guid id) =>
        (await ListBoxesAsync(client)).Single(b => b.Id == id);

    private static async Task<CashBoxTransactionsPageDto> LedgerAsync(
        HttpClient client, Guid boxId, DateOnly? from = null, DateOnly? to = null)
    {
        var url = $"{Boxes}/transactions?boxId={boxId}";
        if (from is { } f) url += $"&from={f:yyyy-MM-dd}";
        if (to is { } t) url += $"&to={t:yyyy-MM-dd}";

        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashBoxTransactionsPageDto>())!;
    }

    private static async Task<PaymentDto> PayAsync(
        HttpClient cashier, string studentId, Guid boxId, decimal amount, string method,
        DateOnly? receivedOn = null)
    {
        var response = await cashier.PostAsJsonAsync(Payments, new
        {
            studentId,
            amount,
            method,
            cashBoxId = boxId,
            // Taqsimotsiz — pul avans bo'lib qoladi (docs/ASSUMPTIONS.md Q15).
            allocations = Array.Empty<object>(),
            receivedOn = receivedOn?.ToString("yyyy-MM-dd"),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PaymentDto>())!;
    }

    private static async Task<ExpenseDto> ExpenseAsync(HttpClient client, Guid boxId, decimal amount)
    {
        var response = await client.PostAsJsonAsync(Expenses, new
        {
            onDate = AppClock.Today.ToString("yyyy-MM-dd"),
            category = "utilities",
            amount,
            method = PaymentMethod.Cash,
            note = "Kassa jurnali testi",
            cashBoxId = boxId,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExpenseDto>())!;
    }

    private async Task<(string Id, string FullName)> NewStudentAsync()
    {
        var id = "stu-" + Guid.NewGuid().ToString("N")[..12];
        var fullName = "Kassa O'quvchi " + id[^6..];

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(new Student
            {
                Id = id,
                FullName = fullName,
                LastName = "Kassa",
                FirstName = "O'quvchi",
                ClassName = "1-A",
                EnrollmentDate = AppClock.Today.ToString("yyyy-MM-dd"),
            });
            await db.SaveChangesAsync();
        });

        return (id, fullName);
    }

    /// <summary>
    /// Qaytarim qatorini BAZAGA to'g'ridan-to'g'ri yozadi — nega: fayl
    /// boshidagi izoh (xizmat hali `cash_box_id` ni to'ldirmaydi).
    ///
    /// <para>
    /// Usul <c>card</c>: naqd qaytarimni tasdiqlash bazadagi
    /// <c>ck_student_refunds_cash_shift</c> tufayli SMENA talab qiladi, smena
    /// esa mahsulotdan olib tashlangan tushuncha — testga o'sha eski
    /// cheklovni qaytarib olib kelmaymiz.
    /// </para>
    /// </summary>
    private async Task<Guid> SeedRefundAsync(
        string studentId, Guid boxId, decimal amount, string method,
        string requestedBy, string approvedBy, bool approved)
    {
        var refund = new StudentRefund
        {
            StudentId = studentId,
            Amount = amount,
            Method = method,
            Reason = "Ortiqcha to'langan",
            RequestedBy = requestedBy,
            RequestedAt = AppClock.NowInstant,
            ApprovedBy = approved ? approvedBy : null,
            ApprovedAt = approved ? AppClock.NowInstant : null,
            CashBoxId = boxId,
        };

        await using var db = NewDb();
        db.StudentRefunds.Add(refund);
        await db.SaveChangesAsync();

        return refund.Id;
    }
}
