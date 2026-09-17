using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Billing;

/// <summary>
/// F1.06 (docs/modules/finance-parity.md §2.1) — obunani "preview" bilan
/// yopish: <c>SubscriptionService.PreviewEndAsync</c> va
/// <c>SubscriptionEndController</c>.
///
/// <para>
/// <b>SETTLEMENT QOIDASI bu yerda sinaladi</b> (to'liq izoh:
/// <c>SubscriptionService.PreviewEndAsync</c>, <c>EndSubscriptionModal.tsx</c>):
/// tugash oyining o'zi HECH QACHON preview ro'yxatida ko'rinmaydi (proratsiya
/// yo'q — to'liq qarz bo'lib qoladi), faqat undan KEYINGI, oldindan
/// hisoblangan oylar ko'rinadi va admin ulardan qaysilarini bekor qilishni
/// O'ZI tanlaydi (avtomatik emas).
/// </para>
/// <para>
/// Bekor qilishning o'zi YANGI kod EMAS — mavjud
/// <c>POST /api/admin/billing/invoices/{id}/void</c> (F10.02) har bir
/// tanlangan oy uchun ALOHIDA chaqiriladi, shuning uchun bu yerda faqat
/// ORKESTRATSIYA (preview → void → end) uchtomonlama sinaladi, void'ning
/// o'z qoidalari (qulf, ikki qavatli nazorat) <c>InvoiceRegisterTests</c>
/// da allaqachon qamrab olingan.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class SubscriptionEndControllerTests(ApiFixture fixture)
{
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    private static InvoiceService InvoicesFor(AppDbContext db) => new(db, new LedgerService(db));

    // =================================================================
    //  1. SubscriptionService.PreviewEndAsync — xizmat darajasida
    // =================================================================

    /// <summary>
    /// Uch oy oldindan hisoblangan (sentyabr, oktyabr, noyabr). Obuna
    /// SENTYABRDA yopiladi — preview'da FAQAT oktyabr va noyabr ko'rinishi
    /// kerak, sentyabrning O'ZI EMAS (proratsiya qoidasi).
    /// </summary>
    [Fact]
    public async Task Tugash_oyining_ozi_preview_royxatida_yoq_keyingi_oylar_bor()
    {
        var actorId = await NewUserAsync();
        var studentId = await NewStudentAsync();
        var sep = new DateOnly(2026, 9, 1);

        await using (var db = NewDb())
        {
            db.StudentSubscriptions.Add(new StudentSubscription
            {
                StudentId = studentId,
                CategoryId = TuitionCategory,
                MonthlyAmount = 1_000_000m,
                StartsOn = sep,
                CreatedBy = actorId,
                CreatedAt = AppClock.NowInstant,
            });
            await db.SaveChangesAsync();

            var invoices = InvoicesFor(db);
            await invoices.AccrueMonthAsync(sep, actorId);
            await invoices.AccrueMonthAsync(sep.AddMonths(1), actorId);
            await invoices.AccrueMonthAsync(sep.AddMonths(2), actorId);
        }

        Guid subscriptionId;
        await using (var db = NewDb())
        {
            subscriptionId = await db.StudentSubscriptions.AsNoTracking()
                .Where(s => s.StudentId == studentId).Select(s => s.Id).SingleAsync();
        }

        await using var check = NewDb();
        var service = new SubscriptionService(check, TestAudit(check), InvoicesFor(check));

        var preview = await service.PreviewEndAsync(subscriptionId, new DateOnly(2026, 9, 15));

        Assert.Equal(2, preview.FutureInvoices.Count);
        Assert.DoesNotContain(preview.FutureInvoices, i => i.PeriodMonth == sep);
        Assert.Contains(preview.FutureInvoices, i => i.PeriodMonth == sep.AddMonths(1));
        Assert.Contains(preview.FutureInvoices, i => i.PeriodMonth == sep.AddMonths(2));
        Assert.All(preview.FutureInvoices, i => Assert.Equal(1_000_000m, i.Payable));
        Assert.All(preview.FutureInvoices, i => Assert.Equal(0m, i.Paid));
    }

    /// <summary>
    /// To'lov taqsimlangan kelajak oy preview'da <c>Paid &gt; 0</c> bilan
    /// ko'rinadi — frontend buni <c>canVoid</c> orqali "bekor qilib
    /// bo'lmaydi, avval storno" deb ko'rsatadi. Ro'yxatdan TUSHIRILMAYDI —
    /// admin nimaga to'lov bor ekanini ko'rishi kerak.
    /// </summary>
    [Fact]
    public async Task Tolovi_bor_kelajak_oy_royxatda_qoladi_lekin_paid_nolmas()
    {
        var actorId = await NewUserAsync();
        var studentId = await NewStudentAsync();
        var sep = new DateOnly(2026, 9, 1);
        var oct = sep.AddMonths(1);

        Guid subscriptionId;
        Guid octInvoiceId;
        await using (var db = NewDb())
        {
            var subscription = new StudentSubscription
            {
                StudentId = studentId,
                CategoryId = TuitionCategory,
                MonthlyAmount = 500_000m,
                StartsOn = sep,
                CreatedBy = actorId,
                CreatedAt = AppClock.NowInstant,
            };
            db.StudentSubscriptions.Add(subscription);
            await db.SaveChangesAsync();
            subscriptionId = subscription.Id;

            var invoices = InvoicesFor(db);
            await invoices.AccrueMonthAsync(sep, actorId);
            await invoices.AccrueMonthAsync(oct, actorId);

            octInvoiceId = await db.Invoices.AsNoTracking()
                .Where(i => i.StudentId == studentId && i.PeriodMonth == oct)
                .Select(i => i.Id).SingleAsync();
        }

        // Oktyabr oyiga to'lov — haqiqiy PaymentService orqali (kassa smenasi bilan).
        await PayInvoiceAsync(studentId, octInvoiceId, 500_000m);

        await using var check = NewDb();
        var service = new SubscriptionService(check, TestAudit(check), InvoicesFor(check));
        var preview = await service.PreviewEndAsync(subscriptionId, sep);

        var octRow = Assert.Single(preview.FutureInvoices, i => i.PeriodMonth == oct);
        Assert.Equal(500_000m, octRow.Paid);
        Assert.Equal(0m, octRow.Remaining);
    }

    /// <summary>Allaqachon <c>void</c> qilingan kelajak oy preview'da UMUMAN ko'rinmaydi.</summary>
    [Fact]
    public async Task Void_qilingan_kelajak_oy_preview_royxatida_korinmaydi()
    {
        var actorId = await NewUserAsync();
        // SPEC §4.5 — jurnalga o'zi qo'ygan odam o'zi bekor qila olmaydi, shuning
        // uchun void BOSHQA admin nomidan.
        var voiderId = await NewUserAsync();
        var studentId = await NewStudentAsync();
        var sep = new DateOnly(2026, 9, 1);
        var oct = sep.AddMonths(1);

        Guid subscriptionId;
        Guid octInvoiceId;
        await using (var db = NewDb())
        {
            var subscription = new StudentSubscription
            {
                StudentId = studentId,
                CategoryId = TuitionCategory,
                MonthlyAmount = 500_000m,
                StartsOn = sep,
                CreatedBy = actorId,
                CreatedAt = AppClock.NowInstant,
            };
            db.StudentSubscriptions.Add(subscription);
            await db.SaveChangesAsync();
            subscriptionId = subscription.Id;

            var invoiceService = InvoicesFor(db);
            await invoiceService.AccrueMonthAsync(sep, actorId);
            await invoiceService.AccrueMonthAsync(oct, actorId);

            octInvoiceId = await db.Invoices.AsNoTracking()
                .Where(i => i.StudentId == studentId && i.PeriodMonth == oct)
                .Select(i => i.Id).SingleAsync();

            await invoiceService.VoidAsync(octInvoiceId, "Sinov uchun oldindan bekor qilingan", voiderId);
        }

        await using var check = NewDb();
        var service = new SubscriptionService(check, TestAudit(check), InvoicesFor(check));
        var preview = await service.PreviewEndAsync(subscriptionId, sep);

        Assert.DoesNotContain(preview.FutureInvoices, i => i.PeriodMonth == oct);
    }

    // =================================================================
    //  2. HTTP — rol darvozasi (har endpoint uchun majburiy)
    // =================================================================

    [Fact]
    public async Task Kassir_preview_ni_kora_olmaydi_403()
    {
        var studentId = await NewStudentAsync();
        var subscriptionId = await NewOpenSubscriptionAsync(studentId, new DateOnly(2026, 9, 1));

        using var client = await fixture.Api.ClientAsAsync(Roles.Cashier);
        var response = await client.PostAsJsonAsync(
            $"/api/admin/billing/subscriptions/{subscriptionId}/end/preview",
            new { endsOn = "2026-09-15" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();
        var response = await client.PostAsJsonAsync(
            $"/api/admin/billing/subscriptions/{Guid.NewGuid()}/end/preview",
            new { endsOn = "2026-09-15" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    public async Task Admin_va_direktor_preview_ni_kora_oladi(string role)
    {
        var studentId = await NewStudentAsync();
        var subscriptionId = await NewOpenSubscriptionAsync(studentId, new DateOnly(2026, 9, 1));

        using var client = await fixture.Api.ClientAsAsync(role);
        var response = await client.PostAsJsonAsync(
            $"/api/admin/billing/subscriptions/{subscriptionId}/end/preview",
            new { endsOn = "2026-09-15" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<EndSubscriptionPreviewDto>();
        Assert.NotNull(preview);
        Assert.Equal(subscriptionId, preview!.SubscriptionId);
    }

    // =================================================================
    //  3. To'liq oqim: preview → tanlangan oyni void → obunani end
    // =================================================================

    /// <summary>
    /// ENG MUHIM TEST: uchtomonlama orkestratsiya. Sentyabr + oktyabr
    /// hisoblangan, ikkalasi ham to'lanmagan. Obuna sentyabrda yopiladi;
    /// admin preview'dan oktyabrni ko'rib, uni <c>voidInvoice</c> bilan
    /// bekor qiladi, so'ng obunani yopadi. Natija: sentyabr TEGILMAY qarz
    /// bo'lib qoladi, oktyabr <c>void</c>, jurnal balansda.
    /// </summary>
    [Fact]
    public async Task Preview_void_end_oqimi_tugash_oyini_saqlab_keyingisini_bekor_qiladi()
    {
        var actorId = await NewUserAsync();
        var studentId = await NewStudentAsync();
        var sep = new DateOnly(2026, 9, 1);
        var oct = sep.AddMonths(1);

        Guid subscriptionId;
        await using (var db = NewDb())
        {
            var subscription = new StudentSubscription
            {
                StudentId = studentId,
                CategoryId = TuitionCategory,
                MonthlyAmount = 700_000m,
                StartsOn = sep,
                CreatedBy = actorId,
                CreatedAt = AppClock.NowInstant,
            };
            db.StudentSubscriptions.Add(subscription);
            await db.SaveChangesAsync();
            subscriptionId = subscription.Id;

            var invoiceService = InvoicesFor(db);
            await invoiceService.AccrueMonthAsync(sep, actorId);
            await invoiceService.AccrueMonthAsync(oct, actorId);
        }

        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var previewResponse = await client.PostAsJsonAsync(
            $"/api/admin/billing/subscriptions/{subscriptionId}/end/preview",
            new { endsOn = "2026-09-15" });
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<EndSubscriptionPreviewDto>();
        var octRow = Assert.Single(preview!.FutureInvoices);
        Assert.Equal(oct, octRow.PeriodMonth);

        var voidResponse = await client.PostAsJsonAsync(
            $"/api/admin/billing/invoices/{octRow.Id}/void",
            new { reason = "O'quvchi avtobusdan chiqdi — oktyabrdan boshlab kerak emas" });
        Assert.Equal(HttpStatusCode.OK, voidResponse.StatusCode);

        var endResponse = await client.PostAsJsonAsync(
            $"/api/admin/billing/subscriptions/{subscriptionId}/end",
            new { endsOn = "2026-09-15" });
        Assert.Equal(HttpStatusCode.OK, endResponse.StatusCode);

        await using var check = NewDb();

        // Sentyabr TEGILMAGAN — hali ham to'liq qarz (proratsiya yo'q).
        var sepInvoice = await check.Invoices.AsNoTracking()
            .SingleAsync(i => i.StudentId == studentId && i.PeriodMonth == sep);
        Assert.Equal(InvoiceStatus.Open, sepInvoice.Status);
        Assert.Equal(700_000m, sepInvoice.Amount - sepInvoice.Discount);

        // Oktyabr — bekor qilingan.
        var octInvoice = await check.Invoices.AsNoTracking()
            .SingleAsync(i => i.StudentId == studentId && i.PeriodMonth == oct);
        Assert.Equal(InvoiceStatus.Void, octInvoice.Status);

        // Obuna yopilgan.
        var subscriptionRow = await check.StudentSubscriptions.AsNoTracking()
            .SingleAsync(s => s.Id == subscriptionId);
        Assert.Equal(new DateOnly(2026, 9, 15), subscriptionRow.EndsOn);

        // Pul yo'li testi (SPEC §4): butun jurnal (davrdan qat'i nazar) balansda —
        // void'ning ko'zgu yozuvlari ham, ikkala oyning original accrual jufti ham.
        var trial = await new LedgerService(check).TrialBalanceAsync();
        Assert.Equal(trial.Sum(t => t.Debit), trial.Sum(t => t.Credit));
    }

    // =================================================================
    //  Yordamchilar
    // =================================================================

    private static AuditService TestAudit(AppDbContext db) =>
        new(db, new Microsoft.AspNetCore.Http.HttpContextAccessor());

    private async Task<string> NewUserAsync(string role = Roles.Admin)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        return user.Id;
    }

    private async Task<string> NewStudentAsync()
    {
        var id = "stu-" + Guid.NewGuid().ToString("N")[..12];
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(new Student
            {
                Id = id,
                FullName = "Test O'quvchi " + id[^6..],
                LastName = "Test",
                FirstName = "O'quvchi",
                ClassName = "1-A",
                EnrollmentDate = AppClock.Today.ToString("yyyy-MM-dd"),
            });
            await db.SaveChangesAsync();
        });
        return id;
    }

    private async Task<Guid> NewOpenSubscriptionAsync(string studentId, DateOnly startsOn)
    {
        var actorId = await NewUserAsync();
        var subscription = new StudentSubscription
        {
            StudentId = studentId,
            CategoryId = TuitionCategory,
            MonthlyAmount = 500_000m,
            StartsOn = startsOn,
            CreatedBy = actorId,
            CreatedAt = AppClock.NowInstant,
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.StudentSubscriptions.Add(subscription);
            await db.SaveChangesAsync();
        });
        return subscription.Id;
    }

    /// <summary>Kassir ochiq smena bilan — haqiqiy <c>PaymentService</c> orqali to'lov qabul qiladi.</summary>
    private async Task PayInvoiceAsync(string studentId, Guid invoiceId, decimal amount)
    {
        var (cashier, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);
        var shift = new CashShift
        {
            CashierId = cashier.Id,
            OpenedAt = AppClock.NowInstant,
            OpeningFloat = 0m,
            Status = CashShiftStatus.Open,
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.CashShifts.Add(shift);
            await db.SaveChangesAsync();
        });

        await using var db = NewDb();
        var payments = new PaymentService(db, new PaymentShiftStub(db), new LedgerService(db));
        await payments.AcceptAsync(
            new AcceptPaymentRequest(studentId, amount, PaymentMethod.Cash, null,
                [new AllocationRequest(invoiceId, amount)]),
            cashier.Id);
    }

    /// <summary>
    /// <c>PaymentsTests.ShiftDouble</c> bilan bir xil naqsh: kassirning ochiq
    /// smenasini bazadan o'qiydi, chek raqamini shu smena bo'yicha hisoblaydi.
    /// P1-10 ning haqiqiy implementatsiyasi emas — P1-11 faqat INTERFEYSGA
    /// tayanadi.
    /// </summary>
    private sealed class PaymentShiftStub(AppDbContext db) : ICashShiftService
    {
        public async Task<CashShiftDto?> CurrentAsync(string cashierId, CancellationToken ct = default)
        {
            var shift = await db.CashShifts.AsNoTracking().FirstOrDefaultAsync(
                s => s.CashierId == cashierId && s.Status == CashShiftStatus.Open, ct);

            return shift is null
                ? null
                : new CashShiftDto(
                    shift.Id, shift.CashierId, "Test kassir",
                    shift.OpenedAt, shift.ClosedAt, shift.OpeningFloat,
                    shift.ExpectedCash, shift.CountedCash, shift.Variance,
                    shift.Status, null, 0, 0m, 0m);
        }

        public async Task<long> NextReceiptNoAsync(Guid shiftId, CancellationToken ct = default)
        {
            var max = await db.Payments
                .Where(p => p.CashShiftId == shiftId)
                .MaxAsync(p => (long?)p.ReceiptNo, ct);
            return (max ?? 0L) + 1L;
        }

        public Task<CashShiftDto> OpenAsync(string cashierId, decimal openingFloat, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CashShiftDto> CloseAsync(
            Guid shiftId, string closedByUserId, decimal countedCash, string? note, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ZReportDto> ZReportAsync(Guid shiftId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CashShiftDto>> ListAsync(CashShiftQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
