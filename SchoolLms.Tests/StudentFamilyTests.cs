using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Brothers and sisters behind one pupil's login (client, 2026-10-08: "bittasiniki bilan kirsa tepada
/// o'ngga chapga boshqa farzandiga ham o'tib ketishi mumkin edi"). Parents sign in with a child's login;
/// the family comes from the school's records — a shared guardian or the same parents' phone. A stranger
/// stays a stranger, and a placeholder phone on many pupils is no family.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StudentFamilyTests(ApiFixture fixture)
{
    [Fact]
    public async Task Bir_telefon_oilasi_almashtiriladi_begona_bola_korinmaydi()
    {
        var phone = UniquePhone();
        var (aUser, a) = await PupilAsync("Aka", phone);
        var (_, b) = await PupilAsync("Uka", phone);
        var (_, stranger) = await PupilAsync("Begona", UniquePhone());
        using var client = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Student, aUser.Id, aUser.FullName, aUser.Email));

        var family = await Read(await client.GetAsync("/api/student/family"));
        Assert.Equal(2, family.GetArrayLength());
        Assert.Equal(a.Id, family[0].GetProperty("id").GetString());   // own first
        Assert.Equal(b.Id, family[1].GetProperty("id").GetString());

        // The brother's data with the same login…
        var brother = await Read(await client.GetAsync($"/api/student/billing?studentId={b.Id}"));
        Assert.Equal(b.FullName, brother.GetProperty("studentName").GetString());
        // …a stranger's id is ignored — the pupil's own data, nothing about the stranger.
        var foreign = await Read(await client.GetAsync($"/api/student/billing?studentId={stranger.Id}"));
        Assert.Equal(a.FullName, foreign.GetProperty("studentName").GetString());
    }

    [Fact]
    public async Task Umumiy_vasiy_ham_oila_toldiruvchi_raqam_oila_emas()
    {
        // Guardianship: different phones, one guardian.
        var (aUser, a) = await PupilAsync("Opa", UniquePhone());
        var (_, b) = await PupilAsync("Singil", UniquePhone());
        await fixture.Api.WithDbAsync(async db =>
        {
            var guardian = new Guardian { FullName = "Ona " + Guid.NewGuid().ToString("N")[..6], Phone = UniquePhone() };
            db.Guardians.Add(guardian);
            db.StudentGuardians.AddRange(
                new StudentGuardian { StudentId = a.Id, GuardianId = guardian.Id },
                new StudentGuardian { StudentId = b.Id, GuardianId = guardian.Id });
            await db.SaveChangesAsync();
        });
        using var sister = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Student, aUser.Id, aUser.FullName, aUser.Email));
        Assert.Equal(2, (await Read(await sister.GetAsync("/api/student/family"))).GetArrayLength());

        // A placeholder number on seven pupils — each of them is alone.
        var placeholder = UniquePhone();
        var (pUser, _) = await PupilAsync("Bir", placeholder);
        for (var i = 0; i < 6; i++) await PupilAsync("Boshqa" + i, placeholder);
        using var lone = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Student, pUser.Id, pUser.FullName, pUser.Email));
        Assert.Equal(1, (await Read(await lone.GetAsync("/api/student/family"))).GetArrayLength());
    }

    [Fact]
    public async Task Oila_farzandi_uchun_olib_ketish_sorovi_ham_ishlaydi()
    {
        var phone = UniquePhone();
        var (aUser, _) = await PupilAsync("Katta", phone);
        var (_, b) = await PupilAsync("Kichik", phone);
        var (_, stranger) = await PupilAsync("Yot", UniquePhone());
        using var client = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Student, aUser.Id, aUser.FullName, aUser.Email));

        var own = await client.PostAsJsonAsync("/api/student/pickup", new { studentId = b.Id });
        Assert.True(own.IsSuccessStatusCode, await own.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/api/student/pickup", new { studentId = stranger.Id })).StatusCode);
    }

    // =================================================================

    private async Task<(AppUser User, Student Student)> PupilAsync(string name, string parentPhone)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Student);
        var student = new Student
        {
            FullName = $"{name} {Guid.NewGuid().ToString("N")[..6]}",
            ClassName = "4-A",
            EnrollmentDate = "2026-09-01",
            ParentPhone = parentPhone,
            UserId = user.Id,
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });
        return (user, student);
    }

    /// <summary>A phone no other test uses — families never leak between tests.</summary>
    private static string UniquePhone() => "+99890" + Random.Shared.Next(1_000_000, 9_999_999);

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }
}
