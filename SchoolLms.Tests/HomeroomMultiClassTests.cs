using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// A teacher who leads several classes (client, 2026-10-09: "bitta o'qituvchi bir nechta sinfga
/// sinf rahbari sifatida biriktirish mumkin bo'lsin") sees the pupils and the pick-up requests
/// of every one of them in the teacher app — and still nothing of a class they do not lead.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class HomeroomMultiClassTests(ApiFixture fixture)
{
    [Fact]
    public async Task Ikki_sinf_rahbari_ikkala_sinfni_koradi_begonasini_yoq()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var classA = new SchoolClass { Name = $"MA{tag}", Grade = 3 };
        var classB = new SchoolClass { Name = $"MB{tag}", Grade = 4 };
        var classC = new SchoolClass { Name = $"MC{tag}", Grade = 4 };
        var a1 = Pupil($"Aliyev {tag}", classA.Name);
        var b1 = Pupil($"Botirov {tag}", classB.Name);
        var c1 = Pupil($"Choriyev {tag}", classC.Name);

        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var teacherId = "";
        var today = AppClock.Now.ToString("o");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(classA, classB, classC);
            db.Students.AddRange(a1, b1, c1);
            var t = await db.Teachers.SingleAsync(x => x.UserId == user.Id);
            teacherId = t.Id;
            db.ClassHomeroomTeachers.AddRange(
                new ClassHomeroomTeacher { ClassId = classA.Id, TeacherId = t.Id },
                new ClassHomeroomTeacher { ClassId = classB.Id, TeacherId = t.Id });
            db.PickupRequests.AddRange(
                new PickupRequest { StudentId = b1.Id, StudentName = b1.FullName, ClassName = classB.Name, CreatedAt = today },
                new PickupRequest { StudentId = c1.Id, StudentName = c1.FullName, ClassName = classC.Name, CreatedAt = today });
            await db.SaveChangesAsync();
        });
        using var client = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email));

        var me = await client.GetFromJsonAsync<JsonElement>("/api/teacher/me");
        Assert.Equal($"{classA.Name}, {classB.Name}", me.GetProperty("homeroomClass").GetString());

        var homeroom = await client.GetFromJsonAsync<JsonElement>("/api/teacher/homeroom");
        Assert.Equal($"{classA.Name}, {classB.Name}", homeroom.GetProperty("className").GetString());
        var rows = homeroom.GetProperty("students").EnumerateArray()
            .Select(r => (r.GetProperty("studentId").GetString(), r.GetProperty("className").GetString())).ToList();
        Assert.Equal([(a1.Id, classA.Name), (b1.Id, classB.Name)], rows);
        Assert.True(homeroom.GetProperty("students")[1].GetProperty("hasPendingPickup").GetBoolean());

        var pickups = await client.GetFromJsonAsync<List<JsonElement>>("/api/teacher/pickups");
        Assert.Equal([b1.Id], pickups!.Select(p => p.GetProperty("studentId").GetString()));

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/teacher/homeroom/handover", new { studentId = a1.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/teacher/homeroom/handover", new { studentId = c1.Id })).StatusCode);

        var foreignPickup = "";
        await fixture.Api.WithDbAsync(async db =>
            foreignPickup = (await db.PickupRequests.SingleAsync(p => p.StudentId == c1.Id)).Id);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsync($"/api/teacher/pickups/{foreignPickup}/accept", null)).StatusCode);
    }

    private static Student Pupil(string fullName, string className) => new()
    {
        FullName = fullName, LastName = fullName.Split(' ')[0], FirstName = fullName.Split(' ')[1],
        BirthDate = "2016-01-01", Gender = "male", ClassName = className, EnrollmentDate = "2026-09-01",
    };
}
