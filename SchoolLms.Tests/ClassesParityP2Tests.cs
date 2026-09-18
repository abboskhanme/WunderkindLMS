using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Sinflar P2 — docs/modules/students-parity.md §2.2.3, gap'lar C-3/C-4/C-5/C-6.
/// Sxema oldingi to'lqinda keldi (<c>StudentsParityP2</c> migratsiyasi:
/// <c>classes.capacity</c>) — bu testlar FAQAT o'sha ustunning ustiga qurilgan
/// yangi endpoint'larni tekshiradi.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ClassesParityP2Tests(ApiFixture fixture)
{
    private const string Classes = "/api/admin/classes";
    private const string Roster = "/api/admin/class-roster";
    private const string Discipline = "/api/admin/discipline";

    /* =====================================================================
     *  C-3 — qidiruv va eksport
     * ================================================================== */

    [Fact]
    public async Task Qidiruv_nom_yoki_xona_boyicha_filtrlaydi()
    {
        var tag = Tag();
        var target = new SchoolClass { Name = $"Q{tag}-A", Grade = 5, Room = $"R{tag}" };
        var other = new SchoolClass { Name = $"Q{tag}-B", Grade = 5, Room = "202" };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(target, other);
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        // Nom bo'yicha — faqat nishon.
        var byName = await admin.GetFromJsonAsync<List<JsonElement>>($"{Classes}?search={target.Name}");
        Assert.Single(byName!);
        Assert.Equal(target.Id, byName![0].GetProperty("id").GetString());

        // Xona bo'yicha — faqat nishon (boshqasining xonasi boshqa).
        var byRoom = await admin.GetFromJsonAsync<List<JsonElement>>($"{Classes}?search={target.Room}");
        Assert.Contains(byRoom!, c => c.GetProperty("id").GetString() == target.Id);
        Assert.DoesNotContain(byRoom!, c => c.GetProperty("id").GetString() == other.Id);
    }

    [Fact]
    public async Task Eksport_xlsx_qaytaradi_ochilmagan_rol_403()
    {
        var tag = Tag();
        var cls = new SchoolClass { Name = $"EX{tag}", Grade = 3 };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
        });

        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{Classes}/export")).StatusCode);

        // Eksport GET — AdminPermAttribute O'QISHNI har doim ochiq qoldiradi,
        // "classes" ruxsatisiz xodim ham fayl ola oladi.
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff);
        var res = await staff.GetAsync($"{Classes}/export?search={cls.Name}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            res.Content.Headers.ContentType?.MediaType);
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);
    }

    /* =====================================================================
     *  C-4 — sig'im ogohlantirishi (taqiq emas)
     * ================================================================== */

    [Fact]
    public async Task Qoshishda_sigim_oshsa_ogohlantiradi_lekin_taqiqlamaydi()
    {
        var tag = Tag();
        var cls = new SchoolClass { Name = $"CAP{tag}", Grade = 4, Capacity = 1 };
        var already = GeneralSettingsFlagsTests.NewStudent($"Bor {tag}", cls.Name, Phone());
        var loose = GeneralSettingsFlagsTests.NewStudent($"Sinfsiz {tag}", "", Phone());

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Students.AddRange(already, loose);
            db.ClassMemberships.Add(new ClassMembership
            {
                StudentId = already.Id, ClassId = cls.Id, JoinedOn = AppClock.Today,
            });
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var res = await admin.PostAsJsonAsync($"{Roster}/{cls.Id}/members", new { studentId = loose.Id });

        // 1/1 dan oshdi (endi 2 ta) — 204 emas, ogohlantirish bilan 200.
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("sig'im", body.GetProperty("warning").GetString(), StringComparison.OrdinalIgnoreCase);

        // Amal baribir bajarilgan — bu OGOHLANTIRISH, taqiq emas.
        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().SingleAsync(s => s.Id == loose.Id);
            Assert.Equal(cls.Name, student.ClassName);
        });
    }

    [Fact]
    public async Task Otkazishda_nishon_sinf_sigimi_oshsa_ogohlantiradi()
    {
        var tag = Tag();
        var full = new SchoolClass { Name = $"TOF{tag}", Grade = 6, Capacity = 1 };
        var empty = new SchoolClass { Name = $"TOE{tag}", Grade = 6 };
        var already = GeneralSettingsFlagsTests.NewStudent($"Toq {tag}", full.Name, Phone());
        var moving = GeneralSettingsFlagsTests.NewStudent($"Kochuvchi {tag}", empty.Name, Phone());

        var membershipId = Guid.Empty;
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(full, empty);
            db.Students.AddRange(already, moving);
            db.ClassMemberships.AddRange(
                new ClassMembership { StudentId = already.Id, ClassId = full.Id, JoinedOn = AppClock.Today },
                new ClassMembership { StudentId = moving.Id, ClassId = empty.Id, JoinedOn = AppClock.Today });
            await db.SaveChangesAsync();
            membershipId = (await db.ClassMemberships.AsNoTracking()
                .SingleAsync(m => m.StudentId == moving.Id)).Id;
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var res = await admin.PostAsJsonAsync($"{Roster}/members/{membershipId}/transfer",
            new { toClassId = full.Id });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("sig'im", body.GetProperty("warning").GetString(), StringComparison.OrdinalIgnoreCase);

        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().SingleAsync(s => s.Id == moving.Id);
            Assert.Equal(full.Name, student.ClassName);
        });
    }

    /* =====================================================================
     *  C-5 — sinf rahbari(lari)
     * ================================================================== */

    [Fact]
    public async Task Sinf_rahbarini_belgilash_boshqasidan_ogirlaydi_va_boshatiladi()
    {
        var tag = Tag();
        var classA = new SchoolClass { Name = $"HR{tag}-A", Grade = 2 };
        var classB = new SchoolClass { Name = $"HR{tag}-B", Grade = 2 };
        var teacher = new Teacher { FullName = $"Rahbar {tag}", HomeroomClass = classA.Name };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(classA, classB);
            db.Teachers.Add(teacher);
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var before = await admin.GetFromJsonAsync<List<JsonElement>>($"{Classes}/{classA.Id}/homeroom-teachers");
        Assert.Contains(before!, t => t.GetProperty("id").GetString() == teacher.Id);

        // B ga tayinlaymiz — HomeroomClass BITTA qiymat, shuning uchun A dan "o'g'irlanadi".
        var res = await admin.PutAsJsonAsync($"{Classes}/{classB.Id}/homeroom-teachers",
            new { teacherIds = new[] { teacher.Id } });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var row = await db.Teachers.AsNoTracking().SingleAsync(t => t.Id == teacher.Id);
            Assert.Equal(classB.Name, row.HomeroomClass);
        });

        var afterA = await admin.GetFromJsonAsync<List<JsonElement>>($"{Classes}/{classA.Id}/homeroom-teachers");
        Assert.Empty(afterA!);

        // Bo'shatish — ro'yxat bo'sh yuborilsa hozirgi rahbar chiqariladi.
        var clear = await admin.PutAsJsonAsync($"{Classes}/{classB.Id}/homeroom-teachers",
            new { teacherIds = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.NoContent, clear.StatusCode);
        await fixture.Api.WithDbAsync(async db =>
        {
            var row = await db.Teachers.AsNoTracking().SingleAsync(t => t.Id == teacher.Id);
            Assert.Equal("", row.HomeroomClass);
        });
    }

    [Fact]
    public async Task Sinf_rahbari_yozish_classes_ruxsatini_talab_qiladi_oqish_ochiq()
    {
        var tag = Tag();
        var cls = new SchoolClass { Name = $"HRP{tag}", Grade = 1 };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
        });

        using var noPerm = await fixture.Api.ClientAsAsync(Roles.Staff);
        Assert.Equal(HttpStatusCode.OK, (await noPerm.GetAsync($"{Classes}/{cls.Id}/homeroom-teachers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noPerm.PutAsJsonAsync($"{Classes}/{cls.Id}/homeroom-teachers",
                new { teacherIds = Array.Empty<string>() })).StatusCode);

        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.GetAsync($"{Classes}/{cls.Id}/homeroom-teachers")).StatusCode);
    }

    /* =====================================================================
     *  C-6 — sinfga ball qo'yish
     * ================================================================== */

    [Fact]
    public async Task Sinfga_ball_har_bir_faol_oquvchiga_alohida_yoziladi_arxivdagi_qoldiriladi()
    {
        var tag = Tag();
        var cls = new SchoolClass { Name = $"PT{tag}", Grade = 3 };
        var a = GeneralSettingsFlagsTests.NewStudent($"A {tag}", cls.Name, Phone());
        var b = GeneralSettingsFlagsTests.NewStudent($"B {tag}", cls.Name, Phone());
        var archived = GeneralSettingsFlagsTests.NewStudent($"Arx {tag}", cls.Name, Phone());
        archived.IsArchived = true;
        var reason = new DisciplineReason { Name = $"Tozalik {tag}", Points = -5, IsActive = true };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Students.AddRange(a, b, archived);
            db.DisciplineReasons.Add(reason);
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var res = await admin.PostAsJsonAsync($"{Discipline}/points/class",
            new { classId = cls.Id, reasonId = reason.Id, note = "Sinf reydi" });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("applied").GetInt32());

        await fixture.Api.WithDbAsync(async db =>
        {
            var points = await db.DisciplinePoints.Where(p => p.ReasonId == reason.Id).ToListAsync();
            Assert.Equal(2, points.Count);
            Assert.All(points, p => Assert.Equal(-5, p.Points));
            Assert.Contains(points, p => p.StudentId == a.Id);
            Assert.Contains(points, p => p.StudentId == b.Id);
            Assert.DoesNotContain(points, p => p.StudentId == archived.Id);
        });
    }

    [Fact]
    public async Task Nofaol_sabab_bilan_sinfga_ball_qoyilmaydi()
    {
        var tag = Tag();
        var cls = new SchoolClass { Name = $"PTI{tag}", Grade = 3 };
        var a = GeneralSettingsFlagsTests.NewStudent($"A {tag}", cls.Name, Phone());
        var reason = new DisciplineReason { Name = $"Ochiq emas {tag}", Points = -5, IsActive = false };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Students.Add(a);
            db.DisciplineReasons.Add(reason);
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var res = await admin.PostAsJsonAsync($"{Discipline}/points/class",
            new { classId = cls.Id, reasonId = reason.Id });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(0, await db.DisciplinePoints.CountAsync(p => p.ReasonId == reason.Id)));
    }

    [Fact]
    public async Task Sinfda_faol_oquvchi_bolmasa_400()
    {
        var tag = Tag();
        var cls = new SchoolClass { Name = $"PTE{tag}", Grade = 3 };
        var reason = new DisciplineReason { Name = $"Bosh sinf {tag}", Points = -1, IsActive = true };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.DisciplineReasons.Add(reason);
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var res = await admin.PostAsJsonAsync($"{Discipline}/points/class",
            new { classId = cls.Id, reasonId = reason.Id });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Sinfga_ball_qoyish_discipline_ruxsatini_talab_qiladi()
    {
        var tag = Tag();
        var cls = new SchoolClass { Name = $"PTP{tag}", Grade = 3 };
        var a = GeneralSettingsFlagsTests.NewStudent($"A {tag}", cls.Name, Phone());
        var reason = new DisciplineReason { Name = $"Ruxsat {tag}", Points = -1, IsActive = true };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Students.Add(a);
            db.DisciplineReasons.Add(reason);
            await db.SaveChangesAsync();
        });

        using var noPerm = await fixture.Api.ClientAsAsync(Roles.Staff);
        var forbidden = await noPerm.PostAsJsonAsync($"{Discipline}/points/class",
            new { classId = cls.Id, reasonId = reason.Id });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var withPerm = await fixture.Api.ClientAsAsync(Roles.Staff, "discipline");
        var ok = await withPerm.PostAsJsonAsync($"{Discipline}/points/class",
            new { classId = cls.Id, reasonId = reason.Id });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];
    private static string Phone() => "+99890" + Random.Shared.Next(1_000_000, 9_999_999);
}
