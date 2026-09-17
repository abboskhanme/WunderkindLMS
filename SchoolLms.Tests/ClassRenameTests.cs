using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// G-1 (docs/modules/students-parity.md §2.1.3): sinf nomini o'zgartirish o'quvchilarni
/// "yetim" qoldirmasligi kerak.
///
/// <para>
/// <b>Nega bu xato jiddiy.</b> O'quvchi sinfiga ID bilan emas, NOM bilan bog'langan
/// (<c>students.class_name</c>). Ilgari <c>PUT /api/admin/classes/{id}</c> faqat
/// <c>classes.name</c> ni o'zgartirardi — sinfdagi HAR BIR o'quvchi jimgina sinfsiz
/// qolardi: jurnal ro'yxati, davomat, chat, e'lon va sinf rahbarining olib ketish
/// so'rovlari bo'shab qolardi. Hech qanday xato chiqmasdi.
/// </para>
/// <para>
/// Nom nusxalari (hammasi bitta tranzaksiyada yangilanadi):
/// <c>students.class_name</c>, <c>teachers.homeroom_class</c>, <c>chat_messages.class_name</c>,
/// <c>broadcasts.class_name</c> (aniq nom va "&lt;sinf&gt; — qarzdorlar" yorlig'i),
/// <c>pickup_requests.class_name</c> va <c>chat_reads.channel</c> (audit ro'yxatida yo'q edi —
/// usiz o'qituvchida sinfning butun chat tarixi "o'qilmagan" bo'lib qolardi).
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ClassRenameTests(ApiFixture fixture)
{
    private const string Classes = "/api/admin/classes";

    // =====================================================================
    //  1. Kaskad
    // =====================================================================

    /// <summary>
    /// Nom o'zgaradi → o'quvchilar (arxivdagisi ham), sinf rahbari, chat, e'lonlar, olib ketish
    /// so'rovlari va chat o'qish belgisi yangi nomga ergashadi. Qo'shni sinf (nomi eskisi bilan
    /// BOSHLANADIGAN) va "Barcha sinflar" e'loni tegilmaydi. Audit yoziladi.
    /// </summary>
    [Fact]
    public async Task Sinf_nomi_ozgarsa_oquvchilar_va_barcha_nom_nusxalari_ergashadi()
    {
        var tag = Tag();
        var oldName = $"RN-{tag}";
        var newName = $"RN2-{tag}";
        var neighbourName = oldName + "0";

        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        using var admin = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Admin, user.Id, user.FullName, user.Email));

        var cls = new SchoolClass { Name = oldName, Grade = 5, MonthlyFee = 100m };
        var neighbour = new SchoolClass { Name = neighbourName, Grade = 5 };
        var active = GeneralSettingsFlagsTests.NewStudent($"Faol {tag}", oldName, "+998900000001");
        active.SubGroup = 1;
        var archived = GeneralSettingsFlagsTests.NewStudent($"Arxivda {tag}", oldName, "+998900000002");
        archived.IsArchived = true;
        archived.ArchivedWithClass = true;
        var stranger = GeneralSettingsFlagsTests.NewStudent($"Qo'shni {tag}", neighbourName, "+998900000003");
        var homeroom = new Teacher { FullName = $"Rahbar {tag}", HomeroomClass = oldName };
        var otherHomeroom = new Teacher { FullName = $"Qo'shni rahbar {tag}", HomeroomClass = neighbourName };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(cls, neighbour);
            db.Students.AddRange(active, archived, stranger);
            db.Teachers.AddRange(homeroom, otherHomeroom);
            db.ChatMessages.AddRange(
                new ChatMessage { ClassName = oldName, Text = "salom", SenderUserId = user.Id, SenderName = "A" },
                new ChatMessage { ClassName = neighbourName, Text = "qo'shni", SenderUserId = user.Id, SenderName = "A" });
            db.Broadcasts.AddRange(
                new Broadcast { ClassName = oldName, Text = "e'lon " + tag, SenderUserId = user.Id },
                new Broadcast { ClassName = oldName + " — qarzdorlar", Text = "qarz " + tag, SenderUserId = user.Id },
                new Broadcast { ClassName = "Barcha sinflar", Text = "hamma " + tag, SenderUserId = user.Id },
                new Broadcast { ClassName = neighbourName, Text = "qo'shni " + tag, SenderUserId = user.Id });
            db.PickupRequests.Add(new PickupRequest
            {
                StudentId = active.Id, StudentName = active.FullName, ClassName = oldName,
                RequestedByUserId = user.Id, CreatedAt = "2026-09-17T08:00:00",
            });
            db.ChatReads.AddRange(
                new ChatRead { UserId = user.Id, Channel = oldName },
                new ChatRead { UserId = user.Id, Channel = neighbourName });
            await db.SaveChangesAsync();
        });

        var response = await admin.PutAsJsonAsync($"{Classes}/{cls.Id}",
            new { name = newName, grade = 6, language = "uz", monthlyFee = 100m, room = "12" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            Assert.Equal(newName, body.RootElement.GetProperty("name").GetString());

        await fixture.Api.WithDbAsync(async db =>
        {
            Assert.Equal(newName, (await db.Classes.AsNoTracking().SingleAsync(c => c.Id == cls.Id)).Name);

            // O'quvchilar — arxivdagisi ham (sinf arxivdan chiqarilganda u nom bo'yicha qaytadi).
            Assert.Equal(newName, (await db.Students.AsNoTracking().SingleAsync(s => s.Id == active.Id)).ClassName);
            Assert.Equal(newName, (await db.Students.AsNoTracking().SingleAsync(s => s.Id == archived.Id)).ClassName);
            Assert.Equal(1, (await db.Students.AsNoTracking().SingleAsync(s => s.Id == active.Id)).SubGroup);
            Assert.Equal(neighbourName, (await db.Students.AsNoTracking().SingleAsync(s => s.Id == stranger.Id)).ClassName);

            Assert.Equal(newName, (await db.Teachers.AsNoTracking().SingleAsync(t => t.Id == homeroom.Id)).HomeroomClass);
            Assert.Equal(neighbourName, (await db.Teachers.AsNoTracking().SingleAsync(t => t.Id == otherHomeroom.Id)).HomeroomClass);

            var chat = await db.ChatMessages.AsNoTracking()
                .Where(m => m.SenderUserId == user.Id).Select(m => m.ClassName).OrderBy(n => n).ToListAsync();
            Assert.Equal(new[] { neighbourName, newName }.OrderBy(n => n), chat);

            var broadcasts = await db.Broadcasts.AsNoTracking()
                .Where(b => b.Text.EndsWith(tag)).ToDictionaryAsync(b => b.Text, b => b.ClassName);
            Assert.Equal(newName, broadcasts["e'lon " + tag]);
            Assert.Equal(newName + " — qarzdorlar", broadcasts["qarz " + tag]);
            Assert.Equal("Barcha sinflar", broadcasts["hamma " + tag]);
            Assert.Equal(neighbourName, broadcasts["qo'shni " + tag]);

            Assert.Equal(newName,
                (await db.PickupRequests.AsNoTracking().SingleAsync(p => p.StudentId == active.Id)).ClassName);

            var reads = await db.ChatReads.AsNoTracking()
                .Where(r => r.UserId == user.Id).Select(r => r.Channel).OrderBy(c => c).ToListAsync();
            Assert.Equal(new[] { neighbourName, newName }.OrderBy(c => c), reads);

            var audit = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == AuditService.EntityStudentClass && a.EntityId == cls.Id)
                .ToListAsync();
            var row = Assert.Single(audit);
            Assert.Contains(oldName, row.Summary);
            Assert.Contains(newName, row.Summary);
        });

        // Foydalanuvchi ko'radigan belgi: sinf ekranidagi o'quvchilar ro'yxati bo'shab qolmaydi.
        using var groups = JsonDocument.Parse(await admin.GetStringAsync($"{Classes}/{cls.Id}/groups"));
        var ids = groups.RootElement.GetProperty("students").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString()).ToList();
        Assert.Contains(active.Id, ids);
    }

    /// <summary>
    /// Yangi nom bilan eskirgan chat o'qish belgisi allaqachon bor (masalan shu nomli sinf
    /// ilgari o'chirilgan) — birlamchi kalit (user_id, channel) to'qnashmaydi, nom baribir
    /// o'zgaradi va KECHROQ o'qilgan vaqt qoladi.
    /// </summary>
    [Fact]
    public async Task Yangi_nomda_eski_oqish_belgisi_bolsa_ham_nom_ozgaradi()
    {
        var tag = Tag();
        var oldName = $"RC-{tag}";
        var newName = $"RC2-{tag}";
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        using var admin = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Admin, user.Id, user.FullName, user.Email));

        var cls = new SchoolClass { Name = oldName, Grade = 4 };
        var later = new DateTime(2026, 9, 10, 12, 0, 0);
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.ChatReads.AddRange(
                new ChatRead { UserId = user.Id, Channel = oldName, ReadAt = later },
                new ChatRead { UserId = user.Id, Channel = newName, ReadAt = later.AddDays(-30) });
            await db.SaveChangesAsync();
        });

        var response = await admin.PutAsJsonAsync($"{Classes}/{cls.Id}",
            new { name = newName, grade = 4, language = "uz", monthlyFee = 0m, room = (string?)null });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var reads = await db.ChatReads.AsNoTracking().Where(r => r.UserId == user.Id).ToListAsync();
            var read = Assert.Single(reads);
            Assert.Equal(newName, read.Channel);
            Assert.Equal(later, read.ReadAt);
        });
    }

    // =====================================================================
    //  2. Rad etish va no-op
    // =====================================================================

    /// <summary>
    /// Boshqa sinfning nomiga o'zgartirish — 409 va o'qiladigan matn. Ruxsat berilsa ikki sinf
    /// o'quvchilari bitta nom ostida aralashib ketardi. Hech narsa o'zgarmaydi.
    /// </summary>
    [Fact]
    public async Task Boshqa_sinf_nomiga_ozgartirish_409_va_hech_narsa_ozgarmaydi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var a = new SchoolClass { Name = $"RA-{tag}", Grade = 7 };
        var b = new SchoolClass { Name = $"RB-{tag}", Grade = 7, IsArchived = true };
        var pupil = GeneralSettingsFlagsTests.NewStudent($"A o'quvchi {tag}", a.Name, "+998900000004");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(a, b);
            db.Students.Add(pupil);
            await db.SaveChangesAsync();
        });

        // Arxivdagi sinf nomi ham band: uning o'quvchilari shu nom bilan arxivda turibdi.
        var response = await admin.PutAsJsonAsync($"{Classes}/{a.Id}",
            new { name = b.Name, grade = 8, language = "uz", monthlyFee = 0m, room = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            var message = body.RootElement.GetProperty("message").GetString()!;
            Assert.Contains(b.Name, message);
            Assert.Contains("mavjud", message);
        }

        await fixture.Api.WithDbAsync(async db =>
        {
            var reloaded = await db.Classes.AsNoTracking().SingleAsync(c => c.Id == a.Id);
            Assert.Equal(a.Name, reloaded.Name);
            Assert.Equal(7, reloaded.Grade);
            Assert.Equal(a.Name, (await db.Students.AsNoTracking().SingleAsync(s => s.Id == pupil.Id)).ClassName);
        });
    }

    /// <summary>
    /// Ayni nom — nom kaskadi ishga tushmaydi (audit yozilmaydi), boshqa maydonlar esa
    /// avvalgidek saqlanadi.
    /// </summary>
    [Fact]
    public async Task Ayni_nomga_ozgartirish_kaskadsiz_boshqa_maydonlarni_saqlaydi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var cls = new SchoolClass { Name = $"RS-{tag}", Grade = 2 };
        var pupil = GeneralSettingsFlagsTests.NewStudent($"Ayni {tag}", cls.Name, "+998900000005");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Students.Add(pupil);
            await db.SaveChangesAsync();
        });

        var response = await admin.PutAsJsonAsync($"{Classes}/{cls.Id}",
            new { name = cls.Name, grade = 3, language = "ru", monthlyFee = 0m, room = "7" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var reloaded = await db.Classes.AsNoTracking().SingleAsync(c => c.Id == cls.Id);
            Assert.Equal(cls.Name, reloaded.Name);
            Assert.Equal(3, reloaded.Grade);
            Assert.Equal("ru", reloaded.Language);
            Assert.Equal("7", reloaded.Room);
            Assert.Equal(cls.Name, (await db.Students.AsNoTracking().SingleAsync(s => s.Id == pupil.Id)).ClassName);
            Assert.False(await db.AuditLogs.AsNoTracking().AnyAsync(
                a => a.EntityType == AuditService.EntityStudentClass && a.EntityId == cls.Id));
        });
    }

    /// <summary>
    /// Bo'sh nom — 400. Kaskad bilan bo'sh nom sinfsiz (<c>class_name = ''</c>) o'quvchilarning
    /// hammasini shu sinfga qo'shib yuborardi.
    /// </summary>
    [Fact]
    public async Task Bosh_nomga_ozgartirish_400()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var cls = new SchoolClass { Name = $"RE-{tag}", Grade = 2 };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
        });

        var response = await admin.PutAsJsonAsync($"{Classes}/{cls.Id}",
            new { name = "   ", grade = 2, language = "uz", monthlyFee = 0m, room = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(cls.Name, (await db.Classes.AsNoTracking().SingleAsync(c => c.Id == cls.Id)).Name));
    }

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];
}
