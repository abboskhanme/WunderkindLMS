using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  A homeroom teacher sees their own pupils' debts (client, 2026-10-12): the classes they lead and
//  the track groups they teach — debt, unpaid months and the parent's phone. A teacher who leads
//  nothing sees nothing, and no other class leaks in.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class HomeroomDebtsTests(ApiFixture fixture)
{
    private const string Url = "/api/teacher/homeroom/debts";
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly October = new(2026, 10, 1);

    [Fact]
    public async Task Rahbar_oz_sinfi_va_yonalish_guruhi_oquvchilarining_qarzini_koradi()
    {
        var w = await SeedAsync();

        var res = await w.Teacher.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = (await res.Content.ReadFromJsonAsync<HomeroomDebtsDto>())!;

        Assert.Equal([w.LedClass, w.TrackName], dto.Groups);
        // Ali and Bobur (the class) and Doniyor (the track group) — not the archived pupil, not the other class.
        Assert.Equal(3, dto.PupilCount);
        Assert.Equal(2, dto.DebtorCount);
        Assert.Equal(3_100_000m, dto.TotalDebt);
        Assert.DoesNotContain(dto.Pupils, p => p.StudentId == w.Archived || p.StudentId == w.OtherClassPupil);

        // Largest debt first, with what is left of each month.
        var ali = dto.Pupils[0];
        Assert.Equal(w.Ali, ali.StudentId);
        Assert.Equal(w.LedClass, ali.Group);
        Assert.Equal(2_600_000m, ali.Debt);
        Assert.Equal("+998901112233", ali.ParentPhone);
        Assert.Equal(
            [new HomeroomUnpaidMonthDto(September, 1_600_000m), new HomeroomUnpaidMonthDto(October, 1_000_000m)],
            ali.UnpaidMonths);

        var doniyor = dto.Pupils.Single(p => p.StudentId == w.Doniyor);
        Assert.Equal(w.TrackName, doniyor.Group);
        Assert.Equal(500_000m, doniyor.Debt);

        var bobur = dto.Pupils.Single(p => p.StudentId == w.Bobur);
        Assert.Equal(0m, bobur.Debt);
        Assert.Empty(bobur.UnpaidMonths);
        Assert.Equal(w.Bobur, dto.Pupils[^1].StudentId);
    }

    [Fact]
    public async Task Rahbar_bolmagan_oqituvchi_hech_narsa_kormaydi()
    {
        await SeedAsync();
        var (_, plain) = await TeacherAsync();

        var dto = (await plain.GetFromJsonAsync<HomeroomDebtsDto>(Url))!;
        Assert.Empty(dto.Groups);
        Assert.Empty(dto.Pupils);
        Assert.Equal(0m, dto.TotalDebt);
    }

    [Fact]
    public async Task Faqat_oqituvchi_tokeni_bilan()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(Url)).StatusCode);
        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url)).StatusCode);
    }

    // =====================================================================
    //  Seed
    // =====================================================================

    private sealed record World(
        HttpClient Teacher, string LedClass, string TrackName,
        string Ali, string Bobur, string Doniyor, string Archived, string OtherClassPupil);

    private async Task<(string TeacherId, HttpClient Client)> TeacherAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var teacherId = "";
        await fixture.Api.WithDbAsync(async db =>
            teacherId = (await db.Teachers.SingleAsync(t => t.UserId == user.Id)).Id);
        return (teacherId, fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email)));
    }

    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (teacherId, client) = await TeacherAsync();
        var led = new SchoolClass { Name = $"R{tag}-A", Grade = 7 };
        var other = new SchoolClass { Name = $"R{tag}-B", Grade = 7 };
        var track = new StudyGroup { Name = $"Aniq fanlar {tag}", IsTrack = true };

        var ali = Pupil($"Ali {tag}", led.Name);
        var bobur = Pupil($"Bobur {tag}", led.Name);
        var archived = Pupil($"Arxiv {tag}", led.Name);
        archived.IsArchived = true;
        var doniyor = Pupil($"Doniyor {tag}", other.Name);
        var stranger = Pupil($"Begona {tag}", other.Name);

        await fixture.Api.WithDbAsync(async db =>
        {
            var createdBy = await db.Teachers.Where(t => t.Id == teacherId).Select(t => t.UserId!).SingleAsync();
            track.CreatedBy = createdBy;
            db.Classes.AddRange(led, other);
            db.Students.AddRange(ali, bobur, archived, doniyor, stranger);
            db.StudyGroups.Add(track);
            await db.SaveChangesAsync();

            db.ClassHomeroomTeachers.Add(new ClassHomeroomTeacher { ClassId = led.Id, TeacherId = teacherId, CreatedAt = AppClock.NowInstant });
            db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = track.Id, TeacherId = teacherId });
            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                GroupId = track.Id, StudentId = doniyor.Id, JoinedOn = September, CreatedBy = createdBy, CreatedAt = AppClock.NowInstant,
            });

            // Ali: September unpaid, October 600 000 paid of 1 600 000. Doniyor: 500 000.
            // The archived pupil and the other-class pupil owe too — and must not show.
            var aliOctober = Invoice(ali.Id, October, 1_600_000m);
            db.Invoices.AddRange(
                Invoice(ali.Id, September, 1_600_000m), aliOctober,
                Invoice(doniyor.Id, September, 500_000m),
                Invoice(archived.Id, September, 900_000m),
                Invoice(stranger.Id, September, 700_000m));
            await db.SaveChangesAsync();

            var cashier = new AppUser { FullName = "Kassir " + tag, Role = Roles.Cashier, Email = "kassir." + tag };
            cashier.SetInitialPassword("Test-" + tag);
            db.Users.Add(cashier);
            var shift = new CashShift { CashierId = cashier.Id, OpenedAt = AppClock.NowInstant, OpeningFloat = 0m, Status = CashShiftStatus.Open };
            db.CashShifts.Add(shift);
            await db.SaveChangesAsync();
            var payment = new Payment
            {
                ReceiptNo = 1, StudentId = ali.Id, Amount = 600_000m, Method = PaymentMethod.Cash,
                CashShiftId = shift.Id, CashierId = cashier.Id, ReceivedAt = AppClock.NowInstant,
            };
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
            db.PaymentAllocations.Add(new PaymentAllocation { PaymentId = payment.Id, InvoiceId = aliOctober.Id, Amount = 600_000m });
            await db.SaveChangesAsync();
        });

        return new World(client, led.Name, track.Name, ali.Id, bobur.Id, doniyor.Id, archived.Id, stranger.Id);
    }

    private static Student Pupil(string fullName, string className)
    {
        var s = GeneralSettingsFlagsTests.NewStudent(fullName, className, "+998901112233");
        s.Id = Guid.NewGuid().ToString();
        return s;
    }

    private static Invoice Invoice(string studentId, DateOnly month, decimal amount) => new()
    {
        StudentId = studentId,
        CategoryId = TuitionCategory,
        PeriodMonth = month,
        Amount = amount,
        Discount = 0m,
        DueOn = month.AddDays(9),
        Status = InvoiceStatus.Open,
        CreatedAt = AppClock.NowInstant,
    };
}
