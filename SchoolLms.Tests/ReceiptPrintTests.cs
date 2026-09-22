using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// 58 mm termal chek — <c>GET /api/receipts/{paymentId}</c> (mijoz so'rovi,
/// 2026-09-22). Brauzer shu JSON'dan ikki nusxani (maktab + ota-ona) chop etadi.
///
/// <para>
/// Ikki qatlam. (1) <b>Bazasiz</b>: <see cref="ReceiptPrintQuery.Build"/> sof
/// funksiya — qatorlar, "to'liq yopildi / qoldi", avans, storno shtampi.
/// (2) <b>HTTP + baza</b>: haqiqiy to'lov (kassa endpoint'i orqali), haqiqiy
/// hisob-fakturalar va storno; RBAC esa PDF endpoint'i bilan BIR XIL ekani
/// yonma-yon tekshiriladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ReceiptPrintTests(ApiFixture fixture)
{
    // ===================================================================
    //  1-qatlam — chek mazmuni (bazasiz)
    // ===================================================================

    /// <summary>
    /// Har abonement oyi alohida qatorda, summasi SHU to'lovdan tushgan qism,
    /// hisob-faktura id'si esa taqsimotdagining o'zi (PDF qatorlari bilan bir xil).
    /// </summary>
    [Fact]
    public void Termal_chek_qatorlari_taqsimotlarga_mos_keladi()
    {
        var tuition = Allocation("tuition", "O'qish to'lovi", new DateOnly(2026, 9, 1), 500_000m);
        var bus = Allocation("bus", "Avtobus", new DateOnly(2026, 9, 1), 100_000m);
        var payment = SamplePayment([tuition, bus], amount: 600_000m, unallocated: 0m);

        var dto = Build(payment, [Invoice(tuition, remaining: 0m), Invoice(bus, remaining: 100_000m)]);

        Assert.Equal(2, dto.Lines.Count);
        Assert.All(dto.Lines, l => Assert.Equal(ReceiptPrintQuery.KindAllocation, l.Kind));

        var t = Assert.Single(dto.Lines, l => l.InvoiceId == tuition.InvoiceId);
        Assert.Equal("O'qish to'lovi", t.CategoryName);
        Assert.Equal(500_000m, t.Amount);
        Assert.Equal(ReceiptText.Money(500_000m), t.AmountText);
        Assert.Equal("2026-yil sentyabr", t.PeriodText);

        var b = Assert.Single(dto.Lines, l => l.InvoiceId == bus.InvoiceId);
        Assert.Equal(100_000m, b.Amount);

        Assert.Equal(600_000m, dto.Total);
        Assert.Equal(ReceiptText.Money(600_000m), dto.TotalText);
        Assert.Equal(ReceiptText.MethodLabel(PaymentMethod.Cash), dto.MethodText);
        Assert.Equal("1-A", dto.ClassName);
        Assert.Null(dto.CancelledStamp);
    }

    /// <summary>
    /// Mijoz talabi: har oy "to'liq yopildi" yoki "qancha qoldi". Qoldiq —
    /// SERVER hisoblagan <see cref="InvoiceDto.Remaining"/>; bu yerda hech
    /// qanday arifmetika yo'q, faqat yorliq tanlanadi.
    /// </summary>
    [Fact]
    public void Qoldigi_nol_oy_toliq_yopildi_qolgani_qoldi_deb_yoziladi()
    {
        var closed = Allocation("tuition", "O'qish to'lovi", new DateOnly(2026, 9, 1), 500_000m);
        var open = Allocation("bus", "Avtobus", new DateOnly(2026, 9, 1), 100_000m);
        var payment = SamplePayment([closed, open], amount: 600_000m, unallocated: 0m);

        var dto = Build(payment,
        [
            Invoice(closed, remaining: 0m, status: InvoiceStatus.Paid),
            Invoice(open, remaining: 250_000m, status: InvoiceStatus.Partial),
        ]);

        var c = Assert.Single(dto.Lines, l => l.InvoiceId == closed.InvoiceId);
        Assert.Equal("to'liq yopildi", c.StatusText);
        Assert.True(c.IsClosed);
        Assert.Equal(0m, c.Remaining);

        var o = Assert.Single(dto.Lines, l => l.InvoiceId == open.InvoiceId);
        Assert.Equal("qoldi: 250 000 so'm", o.StatusText);
        Assert.False(o.IsClosed);
        Assert.Equal(250_000m, o.Remaining);
    }

    /// <summary>
    /// Keyinroq bekor qilingan (void) hisob-faktura "qoldi: …" deb chop
    /// etilmaydi — u summa endi qarz emas.
    /// </summary>
    [Fact]
    public void Bekor_qilingan_hisob_faktura_qoldi_deb_yozilmaydi()
    {
        var line = Allocation("tuition", "O'qish to'lovi", new DateOnly(2026, 9, 1), 200_000m);
        var payment = SamplePayment([line], amount: 200_000m, unallocated: 0m);

        var dto = Build(payment, [Invoice(line, remaining: 300_000m, status: InvoiceStatus.Void)]);

        var single = Assert.Single(dto.Lines);
        Assert.Equal(ReceiptPrintQuery.VoidLabel, single.StatusText);
        Assert.False(single.IsClosed);
    }

    /// <summary>
    /// Taqsimlanmagan pul — alohida "Avans" qatori (PDF bilan bir xil yorliq),
    /// unda "yopildi/qoldi" holati yo'q; qatorlar yig'indisi JAMIGA teng.
    /// </summary>
    [Fact]
    public void Avans_alohida_qator_bolib_chiqadi_va_holati_yoq()
    {
        var line = Allocation("tuition", "O'qish to'lovi", new DateOnly(2026, 9, 1), 500_000m);
        var payment = SamplePayment([line], amount: 800_000m, unallocated: 300_000m);

        var dto = Build(payment, [Invoice(line, remaining: 0m)]);

        Assert.Equal(2, dto.Lines.Count);
        var advance = Assert.Single(dto.Lines, l => l.Kind == ReceiptPrintQuery.KindAdvance);
        Assert.Equal(ReceiptText.AdvanceLine, advance.CategoryName);
        Assert.Equal(300_000m, advance.Amount);
        Assert.Null(advance.InvoiceId);
        Assert.Null(advance.StatusText);
        Assert.Null(advance.IsClosed);
        Assert.Equal(string.Empty, advance.PeriodText);
        Assert.Equal(dto.Total, dto.Lines.Sum(l => l.Amount));
    }

    /// <summary>Storno qilingan to'lovning termal chekida ham "BEKOR QILINGAN" shtampi va sanasi bor.</summary>
    [Fact]
    public void Storno_qilingan_tolov_chekida_shtamp_va_sana_bor()
    {
        var line = Allocation("tuition", "O'qish to'lovi", new DateOnly(2026, 9, 1), 500_000m);
        var payment = SamplePayment([line], amount: 500_000m, unallocated: 0m);
        // 2026-09-12 05:20 UTC = Toshkentda 10:20.
        var cancelledAt = new DateTimeOffset(2026, 9, 12, 5, 20, 0, TimeSpan.Zero);

        var dto = ReceiptPrintQuery.Build(
            payment, ReceiptService.BuildModel(payment, School(), cancelledAt), "1-A",
            [Invoice(line, remaining: 500_000m)], PrintedAt);

        Assert.Equal(ReceiptText.CancelledStamp, dto.CancelledStamp);
        Assert.Equal("Bekor qilingan sana: 12.09.2026 10:20", dto.CancelledAtText);
        Assert.Equal(cancelledAt, dto.CancelledAt);
        Assert.False(dto.IsReversal);
    }

    // ===================================================================
    //  2-qatlam — haqiqiy to'lov, haqiqiy hisob-fakturalar (HTTP + baza)
    // ===================================================================

    /// <summary>
    /// To'lov kassa endpoint'i orqali qabul qilinadi (o'qish 500 000 — to'liq,
    /// avtobus 200 000 dan 50 000 — qisman), keyin shu kassir termal chekni
    /// oladi: qatorlar taqsimotga mos, o'qish "to'liq yopildi", avtobus
    /// "qoldi: 150 000 so'm", sinf va maktab nomi bor.
    /// </summary>
    [Fact]
    public async Task Kassir_qabul_qilgan_tolovining_termal_chekini_oladi()
    {
        var (cashier, client) = await ClientAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();
        var tuition = await NewInvoiceAsync(studentId, "tuition", 500_000m);
        var bus = await NewInvoiceAsync(studentId, "bus", 200_000m);

        var payment = await AcceptAsync(client, studentId, 550_000m,
            (tuition, 500_000m), (bus, 50_000m));

        var response = await client.GetAsync($"/api/receipts/{payment.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ReceiptPrintDto>();
        Assert.NotNull(dto);

        Assert.Equal(payment.Id, dto.PaymentId);
        Assert.Equal(payment.ReceiptNo, dto.ReceiptNo);
        Assert.Equal(payment.StudentName, dto.StudentName);
        Assert.Equal("1-A", dto.ClassName);
        Assert.Equal(cashier.FullName, dto.CashierName);
        Assert.False(string.IsNullOrWhiteSpace(dto.SchoolName));
        Assert.Equal(550_000m, dto.Total);
        Assert.Equal(ReceiptText.Money(550_000m), dto.TotalText);
        Assert.Equal(ReceiptText.DateTime(payment.ReceivedAt), dto.ReceivedAtText);
        Assert.Null(dto.CancelledStamp);

        // Qatorlar = taqsimotlar (avans yo'q, chunki hammasi taqsimlangan).
        Assert.Equal(payment.Allocations.Count, dto.Lines.Count);
        foreach (var allocation in payment.Allocations)
        {
            var line = Assert.Single(dto.Lines, l => l.InvoiceId == allocation.InvoiceId);
            Assert.Equal(allocation.Amount, line.Amount);
            Assert.Equal(allocation.CategoryName, line.CategoryName);
            Assert.Equal(allocation.PeriodMonth, line.PeriodMonth);
        }

        var t = Assert.Single(dto.Lines, l => l.InvoiceId == tuition);
        Assert.Equal(ReceiptPrintQuery.ClosedLabel, t.StatusText);
        Assert.True(t.IsClosed);

        var b = Assert.Single(dto.Lines, l => l.InvoiceId == bus);
        Assert.Equal(150_000m, b.Remaining);
        Assert.Equal(ReceiptPrintQuery.RemainingLabel(150_000m), b.StatusText);
        Assert.False(b.IsClosed);
    }

    /// <summary>Hisob-fakturaga sig'magan pul termal chekda avans qatori bo'lib chiqadi.</summary>
    [Fact]
    public async Task Taqsimlanmagan_pul_termal_chekda_avans_qatori()
    {
        var (_, client) = await ClientAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();
        var tuition = await NewInvoiceAsync(studentId, "tuition", 300_000m);

        var payment = await AcceptAsync(client, studentId, 450_000m, (tuition, 300_000m));

        var dto = await client.GetFromJsonAsync<ReceiptPrintDto>($"/api/receipts/{payment.Id}");

        Assert.NotNull(dto);
        Assert.Equal(2, dto.Lines.Count);
        var advance = Assert.Single(dto.Lines, l => l.Kind == ReceiptPrintQuery.KindAdvance);
        Assert.Equal(150_000m, advance.Amount);
        Assert.Null(advance.StatusText);
        Assert.Equal(dto.Total, dto.Lines.Sum(l => l.Amount));
    }

    /// <summary>
    /// Storno: asl to'lovning cheki "BEKOR QILINGAN" shtampi bilan chiqadi va
    /// uning oyi endi yana ochiq ("qoldi" — chop etilgan paytdagi holat);
    /// storno qatorining o'z cheki esa qaytarilgan summa qatori va shtamp bilan.
    /// </summary>
    [Fact]
    public async Task Storno_qilingan_tolov_va_storno_qatorining_termal_chekida_shtamp_bor()
    {
        var (_, cashier) = await ClientAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();
        var tuition = await NewInvoiceAsync(studentId, "tuition", 500_000m);
        var original = await AcceptAsync(cashier, studentId, 500_000m, (tuition, 500_000m));

        var (_, admin) = await ClientAsync(Roles.Admin);
        var reverse = await admin.PostAsJsonAsync(
            $"/api/admin/payments/{original.Id}/reverse", new { reason = "Termal chek testi" });
        Assert.Equal(HttpStatusCode.OK, reverse.StatusCode);
        var storno = (await reverse.Content.ReadFromJsonAsync<PaymentDto>())!;

        // --- Asl to'lov ---
        var cancelled = await admin.GetFromJsonAsync<ReceiptPrintDto>($"/api/receipts/{original.Id}");
        Assert.NotNull(cancelled);
        Assert.Equal(ReceiptText.CancelledStamp, cancelled.CancelledStamp);
        Assert.NotNull(cancelled.CancelledAt);
        Assert.StartsWith("Bekor qilingan sana: ", cancelled.CancelledAtText, StringComparison.Ordinal);
        Assert.False(cancelled.IsReversal);
        var line = Assert.Single(cancelled.Lines);
        Assert.Equal(500_000m, line.Amount);
        // Storno qilingan taqsimot "to'langan" hisoblanmaydi — oy yana ochiq.
        Assert.Equal(ReceiptPrintQuery.RemainingLabel(500_000m), line.StatusText);

        // --- Storno qatorining o'zi ---
        var reversal = await admin.GetFromJsonAsync<ReceiptPrintDto>($"/api/receipts/{storno.Id}");
        Assert.NotNull(reversal);
        Assert.True(reversal.IsReversal);
        Assert.Equal(ReceiptText.CancelledStamp, reversal.CancelledStamp);
        var refund = Assert.Single(reversal.Lines);
        Assert.Equal(ReceiptPrintQuery.KindRefund, refund.Kind);
        Assert.Equal(ReceiptText.RefundLine, refund.CategoryName);
    }

    // ===================================================================
    //  3-qatlam — RBAC: PDF endpoint'i bilan AYNAN bir xil
    // ===================================================================

    /// <summary>
    /// Termal chek ruxsati PDF chekning ruxsatini TAKRORLAYDI: har rol uchun
    /// ikkala endpoint bir xil status qaytaradi. Kassir (o'z to'lovi) va admin —
    /// 200; o'qituvchi, ota-ona va xodim — 403; boshqa kassir — 404
    /// (chekning borligi ham ma'lumot, F0.04); token'siz — 401.
    /// </summary>
    [Fact]
    public async Task Termal_chek_ruxsati_pdf_chek_bilan_bir_xil()
    {
        var (_, owner) = await ClientAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();
        var tuition = await NewInvoiceAsync(studentId, "tuition", 100_000m);
        var payment = await AcceptAsync(owner, studentId, 100_000m, (tuition, 100_000m));

        var cases = new List<(string Who, HttpClient Client, HttpStatusCode Expected)>
        {
            ("o'z kassiri", owner, HttpStatusCode.OK),
            ("admin", (await ClientAsync(Roles.Admin)).Client, HttpStatusCode.OK),
            ("direktor", (await ClientAsync(Roles.SuperAdmin)).Client, HttpStatusCode.OK),
            ("boshqa kassir", (await ClientAsync(Roles.Cashier)).Client, HttpStatusCode.NotFound),
            ("o'qituvchi", (await ClientAsync(Roles.Teacher)).Client, HttpStatusCode.Forbidden),
            ("ota-ona", (await ClientAsync("parent")).Client, HttpStatusCode.Forbidden),
            ("xodim", (await ClientAsync(Roles.Staff)).Client, HttpStatusCode.Forbidden),
            ("token'siz", fixture.Api.AnonymousClient(), HttpStatusCode.Unauthorized),
        };

        foreach (var (who, client, expected) in cases)
        {
            var json = await client.GetAsync($"/api/receipts/{payment.Id}");
            var pdf = await client.GetAsync($"/api/receipts/{payment.Id}.pdf");

            Assert.True(expected == json.StatusCode,
                $"{who}: termal chek {expected} kutilgan edi, {json.StatusCode} keldi.");
            Assert.True(pdf.StatusCode == json.StatusCode,
                $"{who}: PDF {pdf.StatusCode}, termal chek {json.StatusCode} — ruxsat ajralib qoldi.");
        }
    }

    /// <summary>Mavjud bo'lmagan to'lov — 404, 500 emas.</summary>
    [Fact]
    public async Task Yoq_tolovning_termal_cheki_404()
    {
        var (_, admin) = await ClientAsync(Roles.Admin);

        var response = await admin.GetAsync($"/api/receipts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Termal chekni olish — faqat O'QISH: to'lov, taqsimot va jurnal
    /// qatorlari soni o'zgarmaydi (chekni ikki marta ochish hech narsa yozmaydi).
    /// </summary>
    [Fact]
    public async Task Termal_chekni_olish_hech_narsa_yozmaydi()
    {
        var (_, client) = await ClientAsync(Roles.Cashier);
        var studentId = await NewStudentAsync();
        var tuition = await NewInvoiceAsync(studentId, "tuition", 100_000m);
        var payment = await AcceptAsync(client, studentId, 100_000m, (tuition, 100_000m));

        var before = await CountsAsync(payment.Id);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/receipts/{payment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/receipts/{payment.Id}")).StatusCode);

        Assert.Equal(before, await CountsAsync(payment.Id));
    }

    // ===================================================================
    //  Yordamchilar
    // ===================================================================

    private static readonly DateTimeOffset PrintedAt = new(2026, 9, 22, 7, 0, 0, TimeSpan.Zero);

    private static ReceiptPrintDto Build(PaymentDto payment, IReadOnlyCollection<InvoiceDto> invoices) =>
        ReceiptPrintQuery.Build(
            payment, ReceiptService.BuildModel(payment, School(), cancelledAt: null), "1-A",
            invoices, PrintedAt);

    private static SchoolMeta School() => new()
    {
        Name = "Wunderkind maktabi",
        Address = "Toshkent sh., Chilonzor t.",
        Phone = "+998 90 000 00 00",
    };

    private static PaymentAllocationDto Allocation(
        string code, string name, DateOnly period, decimal amount) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), code, name, period, amount);

    /// <summary>Taqsimotga mos hisob-faktura — faqat chek o'qiydigan maydonlar muhim.</summary>
    private static InvoiceDto Invoice(
        PaymentAllocationDto allocation, decimal remaining, string? status = null) =>
        new(allocation.InvoiceId, "stu-1", "Abdullayev Jasur",
            allocation.CategoryId, allocation.CategoryCode, allocation.CategoryName,
            allocation.PeriodMonth,
            Amount: 1_000_000m, Discount: 0m, Payable: 1_000_000m,
            Paid: 1_000_000m - remaining, Remaining: remaining,
            DueOn: allocation.PeriodMonth.AddDays(9),
            Status: status ?? (remaining == 0m ? InvoiceStatus.Paid : InvoiceStatus.Partial),
            IsOverdue: false,
            CreatedAt: PrintedAt);

    private static PaymentDto SamplePayment(
        List<PaymentAllocationDto> allocations, decimal amount, decimal unallocated) =>
        new(Id: Guid.NewGuid(),
            ReceiptNo: 1042,
            StudentId: "stu-1",
            StudentName: "Abdullayev Jasur G'ayrat o'g'li",
            Amount: amount,
            Method: PaymentMethod.Cash,
            CashShiftId: null,
            CashierId: "usr-1",
            CashierName: "Karimova Dilnoza",
            Note: null,
            ReceivedAt: new DateTimeOffset(2026, 9, 11, 9, 35, 0, TimeSpan.Zero),
            ReversalOf: null,
            ReversedBy: null,
            Unallocated: unallocated,
            Allocations: allocations);

    private async Task<(AppUser User, HttpClient Client)> ClientAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        var client = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));
        return (user, client);
    }

    private async Task<string> NewStudentAsync()
    {
        var id = "stu-" + Guid.NewGuid().ToString("N")[..12];

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(new Student
            {
                Id = id,
                FullName = "Termal chek o'quvchisi " + id[^6..],
                LastName = "Termal",
                FirstName = "O'quvchi",
                ClassName = "1-A",
                EnrollmentDate = AppClock.Today.ToString("yyyy-MM-dd"),
            });
            await db.SaveChangesAsync();
        });

        return id;
    }

    /// <summary>Joriy oy uchun bitta hisob-faktura (o'quvchi × toifa × oy).</summary>
    private async Task<Guid> NewInvoiceAsync(string studentId, string categoryCode, decimal amount)
    {
        var first = new DateOnly(AppClock.Today.Year, AppClock.Today.Month, 1);
        var invoice = new Invoice
        {
            StudentId = studentId,
            Amount = amount,
            Discount = 0m,
            Status = InvoiceStatus.Open,
            PeriodMonth = first,
            DueOn = first.AddDays(9),
            CreatedAt = AppClock.NowInstant,
        };

        await fixture.Api.WithDbAsync(async db =>
        {
            invoice.CategoryId = await db.FeeCategories
                .Where(c => c.Code == categoryCode).Select(c => c.Id).SingleAsync();
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        });

        return invoice.Id;
    }

    /// <summary>To'lovni kassa endpoint'i orqali qabul qiladi (tayyorlov qadami).</summary>
    private static async Task<PaymentDto> AcceptAsync(
        HttpClient client, string studentId, decimal amount, params (Guid InvoiceId, decimal Amount)[] allocations)
    {
        var response = await client.PostAsJsonAsync("/api/cash/payments", new
        {
            studentId,
            amount,
            method = PaymentMethod.Cash,
            allocations = allocations.Select(a => new { invoiceId = a.InvoiceId, amount = a.Amount }).ToArray(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PaymentDto>())!;
    }

    /// <summary>To'lovga bog'liq pul qatorlari soni: to'lovlar, taqsimotlar, jurnal.</summary>
    private async Task<(int Payments, int Allocations, int Ledger)> CountsAsync(Guid paymentId)
    {
        var result = (0, 0, 0);
        await fixture.Api.WithDbAsync(async db =>
        {
            result = (
                await db.Payments.CountAsync(p => p.Id == paymentId || p.ReversalOf == paymentId),
                await db.PaymentAllocations.CountAsync(a => a.PaymentId == paymentId),
                await db.LedgerEntries.CountAsync(e => e.RefId == paymentId));
        });
        return result;
    }
}
