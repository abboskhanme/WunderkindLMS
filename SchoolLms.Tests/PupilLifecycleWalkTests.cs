using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;
// `PaymentDto` exists in both `Dtos` (teacher-salary payment history) and
// `Dtos.Billing` (cash-desk payment). This walk is all cash-desk, so the
// alias wins for the bare name; the rare legacy one is fully qualified.
using PaymentDto = SchoolLms.Application.Dtos.Billing.PaymentDto;

namespace SchoolLms.Tests;

/// <summary>
/// <b>THE morning-demo walk (2026-09-17).</b> One pupil, start to finish, exactly the
/// path the client will click through by hand tomorrow: full enrolment form, class,
/// study group, subscription, a month's accrual, a debt, a partial payment, the rest
/// of the payment, a storno, a correction, and an archive — all through the real HTTP
/// API, never by poking the database.
///
/// <para>
/// <b>Money invariant.</b> After every step that touches <c>ledger_entries</c>, the
/// whole ledger's debit total must equal its credit total, to the tiyin
/// (<see cref="AssertLedgerBalancedAsync"/>). <see cref="LedgerService.PostAsync"/>
/// refuses to write an unbalanced batch, so in a correctly-wired system this can never
/// fail — which is exactly why it is worth asserting here: it catches any future code
/// path that writes to <c>ledger_entries</c> without going through that gate.
/// </para>
/// <para>
/// <b>Full form, not a minimal row.</b> S-8 (docs/modules/students-parity.md) gives
/// <c>StudentPayload</c> the pupil's own <c>Phone</c>, learning <c>Language</c> and a
/// <c>Guardians</c> list (1-2 entries, each with a relation code) alongside the legacy
/// FISH/address/gender/birth date/guardian fields. The enrolment below fills in all of
/// it — pupil phone, language, and BOTH a primary (father) and a second (mother)
/// guardian — the same form the client will fill in by hand tomorrow. S-9 adds
/// <c>TargetGrade</c> for a pupil with no class yet: the walk enrols the pupil
/// class-less (matching the new "add to class later" flow) and assigns the class as
/// its own step, right before checking the class roster shows them.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class PupilLifecycleWalkTests(ApiFixture fixture)
{
    [Fact]
    public async Task Yangi_oquvchi_sinf_guruh_obuna_qarz_tolov_storno_arxiv_yoli()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var cashier = await fixture.Api.ClientAsAsync(Roles.Cashier);

        // Jurnal so'rovlarining sana chegarasi bir marta shu yerda qotiriladi:
        // `GET .../finance/transactions` chaqiruvlari shu ikkitasini ISHLATADI
        // (sukut — "joriy oy, bugungacha" — bilan emas), chunki test o'zi
        // yozgan qatordan OLDINGI oyni ham, bugundan bir necha kun keyingi
        // sanani ham qamrab olishi kerak — qat'iy oraliq har qanday "bugun"
        // siljishi (soat mintaqasi, oy oxiri) tufayli kelib chiqadigan flake'ni
        // butunlay yo'qotadi.
        var walkFrom = new DateOnly(AppClock.Today.Year, AppClock.Today.Month, 1).AddMonths(-1);
        var walkTo = AppClock.Today.AddDays(2);

        // -----------------------------------------------------------------
        //  0. Infratuzilma: sinf, fan, o'qituvchi — bular sahna, sinovning
        //     o'zi emas, shuning uchun to'g'ridan-to'g'ri bazaga yoziladi
        //     (xuddi StudyGroupTests.SeedAsync qilgani kabi).
        // -----------------------------------------------------------------
        var cls = new SchoolClass { Name = $"Namuna {tag}", Grade = 5 };
        var subject = new Subject { Name = $"Ingliz tili {tag}", IsGroupable = true };
        var teacher = new Teacher { FullName = $"Ustoz {tag}" };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.Teachers.Add(teacher);
            await db.SaveChangesAsync();
        });

        // ===================================================================
        //  1. YANGI O'QUVCHI — TO'LIQ FORMA (vasiy, telefon, til — vazifa matni)
        // ===================================================================
        // S-8 endi qurilgan: o'quvchining O'Z telefoni, o'qish tili va ikkita
        // vasiy (asosiy — ota, ikkinchi — ona) — minimal qator EMAS.
        var pupilPhone = $"+99890{Random.Shared.Next(1000000, 9999999)}";
        var fatherPhone = "+998901234567";
        var motherPhone = "+998907654321";
        var createPayload = new StudentPayload(
            FullName: "", BirthDate: "2015-04-12", Address: "Toshkent, Chilonzor",
            Gender: "male",
            ParentFullName: "Yusupov Baxtiyor Erkinovich", ParentPhone: fatherPhone,
            ClassName: "", EnrollmentDate: AppClock.Today.ToString("yyyy-MM-dd"),
            LastName: "Yusupov", FirstName: "Sardor", MiddleName: "Baxtiyor o'g'li",
            ParentLastName: "Yusupov", ParentFirstName: "Baxtiyor", ParentMiddleName: "Erkinovich",
            Phone: pupilPhone, Language: "uz",
            // S-9: sinf hali yo'q — mo'ljaldagi daraja beriladi (ClassName bo'sh
            // bo'lganda majburiy), sinfning o'zi keyingi qadamda biriktiriladi.
            TargetGrade: (short)cls.Grade,
            Guardians:
            [
                new StudentGuardianInput(
                    FullName: "Yusupov Baxtiyor Erkinovich", Phone: fatherPhone,
                    Relation: GuardianRelation.Father, IsPrimary: true),
                new StudentGuardianInput(
                    FullName: "Yusupova Nodira Baxtiyorovna", Phone: motherPhone,
                    Relation: GuardianRelation.Mother, IsPrimary: false),
            ]);

        var createResponse = await admin.PostAsJsonAsync("/api/admin/students", createPayload);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var student = await createResponse.Content.ReadFromJsonAsync<StudentDto>();
        Assert.NotNull(student);
        var studentId = student!.Id;
        Assert.Equal(pupilPhone, student.Phone);
        Assert.Equal("uz", student.Language);
        Assert.Equal(fatherPhone, student.ParentPhone);

        // ===================================================================
        //  2. SINFGA QO'SHISH — ikkalasi ham ro'yxatda ko'rinsin
        // ===================================================================
        var addToClass = await admin.PostAsJsonAsync(
            $"/api/admin/class-roster/{cls.Id}/members", new AddClassMemberRequest(studentId));
        Assert.Equal(HttpStatusCode.NoContent, addToClass.StatusCode);

        var classRoster = await admin.GetFromJsonAsync<ClassRosterDto>($"/api/admin/class-roster/{cls.Id}");
        Assert.NotNull(classRoster);
        Assert.Contains(classRoster!.Students, s => s.StudentId == studentId);

        // ===================================================================
        //  3. O'QUV GURUHIGA QO'SHISH — ikkinchi ro'yxat
        // ===================================================================
        var groupCreate = await admin.PostAsJsonAsync("/api/admin/study-groups", new SaveStudyGroupRequest(
            Name: $"Namuna guruh {tag}", SubjectId: subject.Id,
            ClassIds: [cls.Id], TeacherIds: [teacher.Id]));
        Assert.Equal(HttpStatusCode.OK, groupCreate.StatusCode);
        var group = await groupCreate.Content.ReadFromJsonAsync<StudyGroupDetailDto>();
        Assert.NotNull(group);

        var addToGroup = await admin.PostAsJsonAsync(
            $"/api/admin/study-groups/{group!.Id}/members", new AddGroupMembersRequest([studentId]));
        Assert.Equal(HttpStatusCode.NoContent, addToGroup.StatusCode);

        var groupMembers = await admin.GetFromJsonAsync<List<StudyGroupMemberDto>>(
            $"/api/admin/study-groups/{group.Id}/members");
        Assert.NotNull(groupMembers);
        Assert.Contains(groupMembers!, m => m.StudentId == studentId);

        // ===================================================================
        //  4. OBUNA — va oyni hisoblash (accrual)
        // ===================================================================
        var categories = await admin.GetFromJsonAsync<List<FeeCategoryDto>>(
            "/api/admin/billing/categories?activeOnly=true");
        Assert.NotNull(categories);
        var tuition = categories!.Single(c => c.Code == "tuition");

        var monthStart = new DateOnly(AppClock.Today.Year, AppClock.Today.Month, 1);
        const decimal monthlyAmount = 1_500_000m;

        var subResponse = await admin.PostAsJsonAsync("/api/admin/billing/subscriptions",
            new CreateSubscriptionRequest(studentId, tuition.Id, monthlyAmount, "Namuna obuna", monthStart, null));
        Assert.Equal(HttpStatusCode.OK, subResponse.StatusCode);

        var accrualResponse = await admin.PostAsync(
            $"/api/admin/billing/accrual/run?month={monthStart:yyyy-MM}", null);
        Assert.Equal(HttpStatusCode.OK, accrualResponse.StatusCode);
        var accrualResults = await accrualResponse.Content.ReadFromJsonAsync<List<AccrualResultDto>>();
        Assert.NotNull(accrualResults);
        Assert.Contains(accrualResults!, r => r.Created >= 1);

        await AssertLedgerBalancedAsync();

        // ===================================================================
        //  5. QARZ — pupilda, qarzdorlar ekranida va oyma-oy pivotda
        // ===================================================================
        var invoicePage = await admin.GetFromJsonAsync<InvoicePageDto>(
            $"/api/admin/billing/invoices?studentId={studentId}");
        Assert.NotNull(invoicePage);
        var invoice = Assert.Single(invoicePage!.Rows, i => i.StudentId == studentId);
        Assert.Equal(monthlyAmount, invoice.Payable);
        Assert.Equal(monthlyAmount, invoice.Remaining);

        var ledgerAfterAccrual = await GetStudentLedgerAsync(admin, studentId);
        Assert.Equal(-monthlyAmount, ledgerAfterAccrual.Balance);

        var debtors = await admin.GetFromJsonAsync<List<DebtorRowDto>>("/api/admin/finance/debtors");
        Assert.NotNull(debtors);
        var debtorRow = Assert.Single(debtors!, d => d.StudentId == studentId);
        Assert.Equal(monthlyAmount, debtorRow.Debt);

        var monthKey = monthStart.ToString("yyyy-MM");
        var pivot = await admin.GetFromJsonAsync<ArrearsPivotDto>(
            $"/api/admin/finance/arrears-pivot?fromMonth={monthKey}&toMonth={monthKey}");
        Assert.NotNull(pivot);
        var pivotRow = Assert.Single(pivot!.Rows, r => r.StudentId == studentId);
        Assert.True(pivotRow.Cells.ContainsKey(monthKey),
            $"Pivotda {monthKey} ustuni yo'q — o'quvchining qarzi jadvalda ko'rinmayapti.");
        Assert.Equal(monthlyAmount, pivotRow.Cells[monthKey].ToBePaid);

        // ===================================================================
        //  6. QISMAN TO'LOV, KEYIN QOLGANI — chek, jurnal, qoldiq har safar
        // ===================================================================
        var openShift = await cashier.PostAsJsonAsync("/api/cash/shifts/open", new OpenShiftRequest(0m));
        Assert.Equal(HttpStatusCode.OK, openShift.StatusCode);

        const decimal partial = 900_000m;
        var remaining = monthlyAmount - partial;

        var payment1 = await AcceptPaymentAsync(cashier, studentId, partial, invoice.Id, "Birinchi qism");
        await AssertReceiptAndJournalAsync(admin, payment1.Id, studentId, walkFrom, walkTo, expectReversed: false);

        var ledgerAfterPartial = await GetStudentLedgerAsync(admin, studentId);
        Assert.Equal(-remaining, ledgerAfterPartial.Balance);
        await AssertLedgerBalancedAsync();

        var payment2 = await AcceptPaymentAsync(cashier, studentId, remaining, invoice.Id, "Qoldiq");
        await AssertReceiptAndJournalAsync(admin, payment2.Id, studentId, walkFrom, walkTo, expectReversed: false);

        var ledgerAfterFull = await GetStudentLedgerAsync(admin, studentId);
        Assert.Equal(0m, ledgerAfterFull.Balance);
        await AssertLedgerBalancedAsync();

        // ===================================================================
        //  7. STORNO — bitta to'lovni bekor qilamiz, qoldiq va jurnal tiklanadi
        // ===================================================================
        // Storno tasdiqlovchisi ham o'z ochiq smenasini talab qiladi
        // (PaymentService.ReverseAsync -> RequireOpenShiftAsync(approverId)) —
        // kassirning smenasi bu yerda YETARLI EMAS, admin o'zinikini ochishi shart.
        var adminShift = await admin.PostAsJsonAsync("/api/cash/shifts/open", new OpenShiftRequest(0m));
        Assert.Equal(HttpStatusCode.OK, adminShift.StatusCode);

        var reverseResponse = await admin.PostAsJsonAsync(
            $"/api/admin/payments/{payment2.Id}/reverse",
            new ReversePaymentRequest("Namuna: xato yozildi, tekshirish uchun storno qilinmoqda"));
        Assert.Equal(HttpStatusCode.OK, reverseResponse.StatusCode);
        var reversal = await reverseResponse.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(reversal);
        Assert.Equal(payment2.Id, reversal!.ReversalOf);

        var ledgerAfterReversal = await GetStudentLedgerAsync(admin, studentId);
        Assert.Equal(-remaining, ledgerAfterReversal.Balance);

        var journalAfterReversal = await admin.GetFromJsonAsync<TransactionJournalPageDto>(
            $"/api/admin/finance/transactions?studentId={studentId}"
            + $"&from={walkFrom:yyyy-MM-dd}&to={walkTo:yyyy-MM-dd}");
        Assert.NotNull(journalAfterReversal);
        Assert.Contains(journalAfterReversal!.Rows,
            r => r.Id == reversal.Id && r.Kind == TransactionKind.Reversal);
        Assert.Contains(journalAfterReversal.Rows,
            r => r.Id == payment2.Id && r.Status == TransactionStatus.Reversed);

        await AssertLedgerBalancedAsync();

        // Xatoni to'g'rilaymiz — qoldiqni qayta to'laymiz, arxivlashdan oldin
        // qarz tozalanishi kerak (qarzdorlik to'sig'i, §9 Q4).
        _ = await AcceptPaymentAsync(cashier, studentId, remaining, invoice.Id, "Qoldiq (qayta)");
        var ledgerAfterCorrection = await GetStudentLedgerAsync(admin, studentId);
        Assert.Equal(0m, ledgerAfterCorrection.Balance);
        await AssertLedgerBalancedAsync();

        // ===================================================================
        //  8. ARXIVLASH — pul tarixi yo'qolmasligi kerak
        // ===================================================================
        var archiveResponse = await admin.PostAsJsonAsync($"/api/admin/students/{studentId}/archive",
            new ArchiveStudentRequest("Namuna: sinov yakunlandi, arxivga ko'chirildi"));
        Assert.Equal(HttpStatusCode.NoContent, archiveResponse.StatusCode);

        var archivedList = await admin.GetFromJsonAsync<List<StudentDto>>("/api/admin/students/archived");
        Assert.NotNull(archivedList);
        var archivedRow = Assert.Single(archivedList!, s => s.Id == studentId);
        Assert.True(archivedRow.IsArchived);

        // Moliyaviy iz butunligicha qoladi: qoldiq hamon 0, hamma to'rtta
        // to'lov qatori (qisman, qoldiq, storno, tuzatish) hali ko'rinadi.
        var ledgerAfterArchive = await GetStudentLedgerAsync(admin, studentId);
        Assert.Equal(0m, ledgerAfterArchive.Balance);
        Assert.Equal(4, ledgerAfterArchive.Payments.Count);
        Assert.Equal(monthlyAmount, ledgerAfterArchive.TotalCharged);

        var invoiceAfterArchive = await admin.GetFromJsonAsync<InvoicePageDto>(
            $"/api/admin/billing/invoices?studentId={studentId}");
        Assert.NotNull(invoiceAfterArchive);
        var invoiceRow = Assert.Single(invoiceAfterArchive!.Rows, i => i.StudentId == studentId);
        Assert.Equal(InvoiceStatus.Paid, invoiceRow.Status);
        Assert.Equal(0m, invoiceRow.Remaining);

        await AssertLedgerBalancedAsync();
    }

    // =======================================================================
    //  Yordamchilar
    // =======================================================================

    private static async Task<PaymentDto> AcceptPaymentAsync(
        HttpClient cashier, string studentId, decimal amount, Guid invoiceId, string note)
    {
        var response = await cashier.PostAsJsonAsync("/api/cash/payments", new AcceptPaymentRequest(
            studentId, amount, PaymentMethod.Cash, note, [new AllocationRequest(invoiceId, amount)]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PaymentDto>();
        Assert.NotNull(payment);
        return payment!;
    }

    /// <summary>
    /// Chek (PDF) va jurnaldagi qator — <paramref name="from"/>/<paramref name="to"/>
    /// har doim ANIQ beriladi (sabab: fayl boshidagi "sana chegarasi" izohi).
    /// </summary>
    private static async Task AssertReceiptAndJournalAsync(
        HttpClient admin, Guid paymentId, string studentId, DateOnly from, DateOnly to, bool expectReversed)
    {
        var receipt = await admin.GetAsync($"/api/receipts/{paymentId}.pdf");
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.Equal("application/pdf", receipt.Content.Headers.ContentType?.MediaType);

        var journal = await admin.GetFromJsonAsync<TransactionJournalPageDto>(
            $"/api/admin/finance/transactions?studentId={studentId}"
            + $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        Assert.NotNull(journal);
        var row = Assert.Single(journal!.Rows, r => r.Id == paymentId);
        Assert.Equal(TransactionKind.Payment, row.Kind);
        Assert.Equal(expectReversed ? TransactionStatus.Reversed : TransactionStatus.Active, row.Status);
    }

    private static async Task<StudentLedgerDto> GetStudentLedgerAsync(HttpClient admin, string studentId)
    {
        var response = await admin.GetAsync($"/api/admin/students/{studentId}/ledger");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ledger = await response.Content.ReadFromJsonAsync<StudentLedgerDto>();
        Assert.NotNull(ledger);
        return ledger!;
    }

    /// <summary>
    /// TRIAL BALANCE — butun jurnalning debet yig'indisi kredit yig'indisiga
    /// tiyingacha teng bo'lishi shart. <see cref="LedgerService.PostAsync"/>
    /// har partiyani yozishdan OLDIN balansni tekshiradi (debit == credit), shuning
    /// uchun to'g'ri ishlayotgan tizimda bu tasdiq har doim o'tadi — va aynan shuning
    /// uchun foydali: <c>ledger_entries</c>ga shu darvozani chetlab o'tib yozadigan
    /// har qanday kelajakdagi kod shu yerda ushlanadi. Testlar bitta
    /// (<see cref="SchoolLmsCollection"/>) baza va kolleksiya ichida KETMA-KET
    /// yuguradi, shuning uchun butun jadval bo'yicha yig'indi istalgan paytda
    /// barqaror (parallel yozuv yo'q).
    /// </summary>
    private async Task AssertLedgerBalancedAsync()
    {
        decimal debit = 0m, credit = 0m;
        await fixture.Api.WithDbAsync(async db =>
        {
            debit = await db.LedgerEntries
                .Where(e => e.Direction == LedgerDirection.Debit)
                .SumAsync(e => e.Amount);
            credit = await db.LedgerEntries
                .Where(e => e.Direction == LedgerDirection.Credit)
                .SumAsync(e => e.Amount);
        });
        Assert.Equal(debit, credit);
    }
}
