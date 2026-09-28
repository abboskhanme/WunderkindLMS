using System.Net;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Client, 2026-09-28: "o'qituvchilar accountida o'zining oylik maoshi ko'rinmasin". Every teacher
/// client (Mini App, PWA, web) read its salary from <c>/api/teacher/salary</c>, so the rule is
/// held THERE — even a teacher row that still carries the retired <c>salary</c> key gets 403.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TeacherSalaryHiddenTests(ApiFixture fixture)
{
    [Fact]
    public async Task Oqituvchi_oz_maoshini_kormaydi_eski_salary_kaliti_bilan_ham()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        await fixture.Api.WithDbAsync(async db =>
        {
            var t = await db.Teachers.SingleAsync(x => x.UserId == user.Id);
            t.Permissions = [.. TeacherPermissions.All, TeacherPermissions.Salary]; // an old row keeps the key
            t.Salary = 5_432_100m;
            await db.SaveChangesAsync();
        });
        using var client = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email));

        var response = await client.GetAsync("/api/teacher/salary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("5432100", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Yangi_oqituvchiga_salary_kaliti_berilmaydi() =>
        Assert.DoesNotContain(TeacherPermissions.Salary, TeacherPermissions.All);
}
