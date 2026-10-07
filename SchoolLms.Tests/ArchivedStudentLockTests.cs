using System.Net;
using System.Net.Http.Json;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// An archived pupil is no longer the school's (client, 2026-10-08: "arxivdagi o'quvchi uchun to'lov
/// qilish, chegirma yoki abonement biriktirish yoki sinfga qo'shish butkul taqiqlansin ... deyarli
/// amallar bajarish mumkin bo'lmasligi kerak"). What creates or raises something is refused with
/// 409 <c>student_archived</c>; reading, comments and restoring stay open.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ArchivedStudentLockTests(ApiFixture fixture)
{
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");

    [Fact]
    public async Task Arxivdagi_oquvchiga_yozuvchi_amallar_409()
    {
        var studentId = await SeedStudentAsync(archived: true);
        var subscriptionId = await SeedSubscriptionAsync(studentId);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var calls = new (string Name, Func<Task<HttpResponseMessage>> Call)[]
        {
            ("profile", () => admin.PutAsJsonAsync($"/api/admin/students/{studentId}", new
            {
                fullName = "X", birthDate = "2015-01-01", address = "Toshkent", gender = "male",
                parentFullName = "Ota-ona", parentPhone = "+998901112233", className = "5-A", enrollmentDate = "2026-09-01",
            })),
            ("reset-password", () => admin.PostAsync($"/api/admin/students/{studentId}/reset-password", null)),
            ("credentials", () => admin.GetAsync($"/api/admin/students/{studentId}/credentials")),
            ("discipline", () => admin.PostAsJsonAsync("/api/admin/discipline/points", new { studentId, reasonId = "r", note = (string?)null })),
            ("abonement edit", () => admin.PutAsJsonAsync($"/api/admin/billing/subscriptions/{subscriptionId}",
                new { monthlyAmount = 500_000m, detail = (string?)null, endsOn = (string?)null })),
        };
        foreach (var (name, call) in calls)
        {
            var response = await call();
            Assert.True(response.StatusCode == HttpStatusCode.Conflict,
                $"{name}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            Assert.Contains("student_archived", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Izoh_va_arxivdan_qaytarish_ochiq()
    {
        var studentId = await SeedStudentAsync(archived: true);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var comment = await admin.PostAsJsonAsync($"/api/admin/students/{studentId}/comments",
            new { kind = StudentCommentKind.Negative, body = "O'qimay ketdi" });
        Assert.True(comment.IsSuccessStatusCode, await comment.Content.ReadAsStringAsync());

        var restore = await admin.PostAsJsonAsync($"/api/admin/students/{studentId}/restore", new { newPassword = (string?)null });
        Assert.True(restore.IsSuccessStatusCode, await restore.Content.ReadAsStringAsync());
        // Back in the school — the same action now passes the guard.
        Assert.NotEqual(HttpStatusCode.Conflict,
            (await admin.PostAsync($"/api/admin/students/{studentId}/reset-password", null)).StatusCode);
    }

    [Fact]
    public async Task Tolov_va_jurnal_arxivdagi_oquvchiga_yozilmaydi()
    {
        var studentId = await SeedStudentAsync(archived: true);
        var (cashier, _) = await fixture.Api.SeedUserAsync(Roles.Cashier);

        await fixture.Api.WithDbAsync(async db =>
        {
            var payment = await Assert.ThrowsAsync<PaymentException>(() => new PaymentService(db, new LedgerService(db))
                .AcceptAsync(new AcceptPaymentRequest(studentId, 100_000m, PaymentMethod.Cash, null, []), cashier.Id));
            Assert.Equal(ArchivedStudents.Code, payment.Code);

            var journal = await JournalService.SetEntryAsync(db,
                new SetJournalEntryRequest("class", "subject", 1, studentId, "2026-10-05", 1, 5, null));
            Assert.NotNull(journal);
            Assert.Contains("arxivda", journal);
            Assert.Empty(db.JournalEntries.Where(e => e.StudentId == studentId));
        });
    }

    // =================================================================

    private async Task<string> SeedStudentAsync(bool archived)
    {
        var student = new Student
        {
            FullName = "Arxiv " + Guid.NewGuid().ToString("N")[..8],
            ClassName = archived ? "" : "5-A",
            EnrollmentDate = "2026-09-01",
            IsArchived = archived,
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });
        return student.Id;
    }

    private async Task<Guid> SeedSubscriptionAsync(string studentId)
    {
        var (actor, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var subscription = new StudentSubscription
        {
            StudentId = studentId,
            CategoryId = TuitionCategory,
            MonthlyAmount = 900_000m,
            StartsOn = new DateOnly(2026, 9, 1),
            CreatedBy = actor.Id,
            CreatedAt = AppClock.NowInstant,
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.StudentSubscriptions.Add(subscription);
            await db.SaveChangesAsync();
        });
        return subscription.Id;
    }
}
