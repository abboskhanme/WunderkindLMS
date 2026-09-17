using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// O'quvchi formasi va VASIYLAR — docs/modules/students-parity.md §2.3 (S-8)
/// va §2.9 (P-1, P-2).
///
/// <para>
/// <b>Eng muhim tekshiruv — "bugungidek".</b> Forma kengaydi, lekin eski
/// payload bilan saqlangan o'quvchi HARFMA-HARF bugungiday bo'lishi kerak:
/// yangi ustunlar null, vasiy qatori bittagina va turi <c>parent</c>. Aks
/// holda import, ota-ona portali va shartnoma matni jimgina siljib ketardi.
/// </para>
/// <para>
/// <b>Ikkinchi tekshiruv — ikki ustun bir qadamda.</b>
/// <c>students.parent_full_name</c> / <c>.parent_phone</c> o'nlab joydan
/// o'qiladi (ota-ona portali oilani TELEFON bo'yicha topadi). Asosiy vasiy
/// almashsa yoki tahrirlansa, shu ikki ustun ham o'sha zahoti tenglashishi
/// kerak.
/// </para>
/// <para>
/// <b>Izolyatsiya.</b> Testlar bitta bazani bo'lishadi. Vasiy telefoni
/// BUTUN bazada unikal (<c>ux_guardians_phone_key</c>), shuning uchun har
/// test o'z raqamlarini <see cref="NewPhone"/> orqali oladi va o'z tegi
/// bilan qidiradi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StudentGuardianTests(ApiFixture fixture)
{
    private const string Students = "/api/admin/students";
    private const string Parents = "/api/admin/parents/guardians";

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"{Students}/x/form-card")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"{Students}/x/guardians")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync($"{Students}/x/guardians",
                new { fullName = "A", phone = "+998901234567" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.DeleteAsync($"{Students}/x/guardians/y")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Parents)).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "students");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Students}/x/form-card")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"{Students}/x/guardians",
                new { fullName = "A", phone = "+998901234567" })).StatusCode);
    }

    /// <summary>
    /// Xodim o'qiy oladi (bo'limlararo bog'liqlik uchun GET ochiq), lekin
    /// "students" ruxsatisiz vasiyga TEGA OLMAYDI — qo'shish ham,
    /// tahrirlash ham, asosiysini almashtirish ham, uzish ham.
    /// </summary>
    [Fact]
    public async Task Xodim_oqiydi_lekin_ruxsatsiz_yozmaydi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (studentId, guardianId) = await SeedWithGuardianAsync(admin, tag);

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff);

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"{Students}/{studentId}/form-card")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"{Students}/{studentId}/guardians")).StatusCode);

        var body = new { fullName = $"Xodim {tag}", phone = NewPhone(), relation = "mother" };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PostAsJsonAsync($"{Students}/{studentId}/guardians", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PutAsJsonAsync($"{Students}/{studentId}/guardians/{guardianId}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PostAsJsonAsync($"{Students}/{studentId}/guardians/{guardianId}/primary",
                new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.DeleteAsync($"{Students}/{studentId}/guardians/{guardianId}")).StatusCode);
    }

    /// <summary>"students" ruxsatli xodim esa yoza oladi — darvoza shu kalitga bog'langan.</summary>
    [Fact]
    public async Task Students_ruxsatli_xodim_yozadi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (studentId, _) = await SeedWithGuardianAsync(admin, tag);

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "students");

        var response = await staff.PostAsJsonAsync($"{Students}/{studentId}/guardians",
            new { fullName = $"Buvisi {tag}", phone = NewPhone(), relation = "grandparent" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =====================================================================
    //  2. ESKI PAYLOAD — BUGUNGIDEK
    // =====================================================================

    /// <summary>
    /// Faqat bugungi maydonlar bilan saqlangan o'quvchi harfma-harf bugungiday:
    /// yangi ustunlar null, vasiy BITTA va turi <c>parent</c>.
    /// </summary>
    [Fact]
    public async Task Eski_payload_bugungidek_saqlanadi()
    {
        var tag = Tag();
        var phone = NewPhone();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var id = await CreateAsync(admin, LegacyPayload(tag, phone));

        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().FirstAsync(s => s.Id == id);
            // Yangi ustunlar — TEGILMAGAN.
            Assert.Null(student.Phone);
            Assert.Null(student.Language);
            Assert.Null(student.DocumentUrl);
            Assert.Null(student.StatusId);
            // Eski ustunlar — payload'dagidek.
            Assert.Equal($"Ota-ona {tag}", student.ParentFullName);
            Assert.Equal(phone, student.ParentPhone);

            var links = await db.StudentGuardians.AsNoTracking()
                .Where(l => l.StudentId == id).ToListAsync();
            var link = Assert.Single(links);
            Assert.Equal(GuardianRelation.Parent, link.Relation);
            Assert.Null(link.RelationNote);
            Assert.True(link.IsPrimary);

            var guardian = await db.Guardians.AsNoTracking().FirstAsync(g => g.Id == link.GuardianId);
            Assert.Equal($"Ota-ona {tag}", guardian.FullName);
            Assert.Equal(phone, guardian.Phone);
        });
    }

    /// <summary>
    /// Forma ASOSIY vasiyni ro'yxatda yuborganda natija yuqoridagi bilan
    /// AYNAN bir xil bo'ladi: qo'shimcha vasiy ham, tur o'zgarishi ham yo'q.
    /// </summary>
    [Fact]
    public async Task Faqat_asosiy_vasiy_yuborilsa_natija_ozgarmaydi()
    {
        var tag = Tag();
        var phone = NewPhone();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var payload = LegacyPayload(tag, phone);
        payload["guardians"] = new[]
        {
            new { fullName = $"Ota-ona {tag}", phone, relation = "parent", isPrimary = true },
        };
        var id = await CreateAsync(admin, payload);

        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().FirstAsync(s => s.Id == id);
            Assert.Equal($"Ota-ona {tag}", student.ParentFullName);
            Assert.Equal(phone, student.ParentPhone);
            Assert.Null(student.Phone);

            var link = Assert.Single(await db.StudentGuardians.AsNoTracking()
                .Where(l => l.StudentId == id).ToListAsync());
            Assert.Equal(GuardianRelation.Parent, link.Relation);
            Assert.True(link.IsPrimary);
            Assert.Null(link.RelationNote);
        });
    }

    /// <summary>
    /// Tahrirda yangi maydonlar yuborilmasa TOZALANMAYDI (eski mijoz ularni
    /// umuman bilmaydi), bo'sh satr yuborilsa esa tozalanadi.
    /// </summary>
    [Fact]
    public async Task Yangi_maydonlar_yuborilmasa_tegilmaydi()
    {
        var tag = Tag();
        var phone = NewPhone();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var payload = LegacyPayload(tag, phone);
        payload["phone"] = "+998911112233";
        payload["language"] = "kaa";
        payload["documentUrl"] = "/uploads/passport.pdf";
        var id = await CreateAsync(admin, payload);

        // Eski payload bilan tahrir — uch maydon joyida qoladi.
        var legacyEdit = LegacyPayload(tag, phone);
        legacyEdit["address"] = "Samarqand";
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync($"{Students}/{id}", legacyEdit)).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().FirstAsync(s => s.Id == id);
            Assert.Equal("Samarqand", student.Address);
            Assert.Equal("+998911112233", student.Phone);
            Assert.Equal("kaa", student.Language);
            Assert.Equal("/uploads/passport.pdf", student.DocumentUrl);
        });

        // Bo'sh satr — ATAYLAB tozalash.
        var clearing = LegacyPayload(tag, phone);
        clearing["phone"] = "";
        clearing["documentUrl"] = "";
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync($"{Students}/{id}", clearing)).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().FirstAsync(s => s.Id == id);
            Assert.Null(student.Phone);
            Assert.Null(student.DocumentUrl);
            Assert.Equal("kaa", student.Language);
        });
    }

    // =====================================================================
    //  3. IKKINCHI VASIY
    // =====================================================================

    /// <summary>Ikkinchi vasiy turi bilan birga saqlanadi va kartochkada qaytadi.</summary>
    [Fact]
    public async Task Ikkinchi_vasiy_turi_bilan_qaytadi()
    {
        var tag = Tag();
        var fatherPhone = NewPhone();
        var motherPhone = NewPhone();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var payload = LegacyPayload(tag, fatherPhone);
        payload["guardians"] = new object[]
        {
            new { fullName = $"Otasi {tag}", phone = fatherPhone, relation = "father", isPrimary = true },
            new { fullName = $"Onasi {tag}", phone = motherPhone, relation = "mother", isPrimary = false },
        };
        var id = await CreateAsync(admin, payload);

        var card = await GetJsonAsync(admin, $"{Students}/{id}/form-card");
        var guardians = card.GetProperty("guardians").EnumerateArray().ToList();
        Assert.Equal(2, guardians.Count);

        // Asosiysi birinchi.
        Assert.True(guardians[0].GetProperty("isPrimary").GetBoolean());
        Assert.Equal("father", guardians[0].GetProperty("relation").GetString());
        Assert.Equal($"Otasi {tag}", guardians[0].GetProperty("fullName").GetString());

        Assert.False(guardians[1].GetProperty("isPrimary").GetBoolean());
        Assert.Equal("mother", guardians[1].GetProperty("relation").GetString());
        Assert.Equal(motherPhone, guardians[1].GetProperty("phone").GetString());

        // Eski ustunlar ASOSIY vasiyga teng.
        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().FirstAsync(s => s.Id == id);
            Assert.Equal($"Otasi {tag}", student.ParentFullName);
            Assert.Equal(fatherPhone, student.ParentPhone);
        });
    }

    /// <summary>
    /// Kengaytirilgan ro'yxatdagi HAR bir qiymat qabul qilinadi; "boshqa"
    /// yonidagi izoh esa faqat <c>other</c> da saqlanadi.
    /// </summary>
    [Theory]
    [InlineData("parent")]
    [InlineData("father")]
    [InlineData("mother")]
    [InlineData("grandparent")]
    [InlineData("trustee")]
    [InlineData("other")]
    public async Task Kengaytirilgan_turlar_qabul_qilinadi(string relation)
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (studentId, _) = await SeedWithGuardianAsync(admin, tag);

        var phone = NewPhone();
        var response = await admin.PostAsJsonAsync($"{Students}/{studentId}/guardians",
            new { fullName = $"Vasiy {tag}", phone, relation, relationNote = "amakisi" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(relation, body.RootElement.GetProperty("relation").GetString());
        // Izoh faqat "boshqa" da ma'noga ega.
        var note = body.RootElement.GetProperty("relationNote").GetString();
        Assert.Equal(relation == GuardianRelation.Other ? "amakisi" : null, note);
    }

    /// <summary>O'ylab topilgan tur — tushunarli 400 (bazadagi 23514 emas).</summary>
    [Fact]
    public async Task Notanish_tur_rad_etiladi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (studentId, guardianId) = await SeedWithGuardianAsync(admin, tag);

        var attach = await admin.PostAsJsonAsync($"{Students}/{studentId}/guardians",
            new { fullName = $"Vasiy {tag}", phone = NewPhone(), relation = "uncle" });
        Assert.Equal(HttpStatusCode.BadRequest, attach.StatusCode);
        Assert.Contains("uncle", await attach.Content.ReadAsStringAsync());

        var edit = await admin.PutAsJsonAsync($"{Students}/{studentId}/guardians/{guardianId}",
            new { fullName = $"Ota-ona {tag}", phone = NewPhone(), relation = "stepfather" });
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);

        // Formadan kelgan payload ham xuddi shunday rad etiladi.
        var payload = LegacyPayload(tag, NewPhone());
        payload["guardians"] = new[]
        {
            new { fullName = $"Kim {tag}", phone = NewPhone(), relation = "neighbour", isPrimary = true },
        };
        var created = await admin.PostAsJsonAsync(Students, payload);
        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
    }

    /// <summary>O'qish tili ham ro'yxatdan — "de" bazaga yetib bormaydi.</summary>
    [Fact]
    public async Task Notanish_til_rad_etiladi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var payload = LegacyPayload(tag, NewPhone());
        payload["language"] = "de";

        var response = await admin.PostAsJsonAsync(Students, payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =====================================================================
    //  4. ESKI USTUNLAR ASOSIY VASIY BILAN BIR QADAMDA
    // =====================================================================

    /// <summary>
    /// Asosiy vasiy almashsa <c>parent_full_name</c> va <c>parent_phone</c>
    /// ham o'sha zahoti yangisiga tenglashadi — aks holda ota-ona portali
    /// (telefon bo'yicha topadi) oilani yo'qotardi.
    /// </summary>
    [Fact]
    public async Task Asosiy_vasiy_almashsa_eski_ustunlar_ergashadi()
    {
        var tag = Tag();
        var fatherPhone = NewPhone();
        var motherPhone = NewPhone();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var payload = LegacyPayload(tag, fatherPhone);
        payload["guardians"] = new object[]
        {
            new { fullName = $"Otasi {tag}", phone = fatherPhone, relation = "father", isPrimary = true },
            new { fullName = $"Onasi {tag}", phone = motherPhone, relation = "mother", isPrimary = false },
        };
        var id = await CreateAsync(admin, payload);

        var motherId = await GuardianIdAsync(admin, id, motherPhone);

        var promoted = await admin.PostAsJsonAsync(
            $"{Students}/{id}/guardians/{motherId}/primary", new { });
        Assert.Equal(HttpStatusCode.NoContent, promoted.StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().FirstAsync(s => s.Id == id);
            Assert.Equal($"Onasi {tag}", student.ParentFullName);
            Assert.Equal(motherPhone, student.ParentPhone);

            // Asosiy vasiy HAR DOIM bittagina.
            var links = await db.StudentGuardians.AsNoTracking()
                .Where(l => l.StudentId == id).ToListAsync();
            Assert.Equal(2, links.Count);
            Assert.Single(links, l => l.IsPrimary);
        });
    }

    /// <summary>Asosiy vasiyning raqami tahrirlansa ham eski ustun ergashadi.</summary>
    [Fact]
    public async Task Asosiy_vasiy_tahrirlansa_eski_ustunlar_ergashadi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (studentId, guardianId) = await SeedWithGuardianAsync(admin, tag);

        var newPhone = NewPhone();
        var response = await admin.PutAsJsonAsync($"{Students}/{studentId}/guardians/{guardianId}",
            new { fullName = $"Yangi Ism {tag}", phone = newPhone, relation = "mother" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().FirstAsync(s => s.Id == studentId);
            Assert.Equal($"Yangi Ism {tag}", student.ParentFullName);
            Assert.Equal(newPhone, student.ParentPhone);
            // F.I.SH bo'laklari ham qayta yozilgan (eksport va shartnoma matni o'qiydi).
            Assert.Equal("Yangi", student.ParentLastName);

            var link = Assert.Single(await db.StudentGuardians.AsNoTracking()
                .Where(l => l.StudentId == studentId).ToListAsync());
            Assert.Equal(GuardianRelation.Mother, link.Relation);
        });
    }

    /// <summary>Band raqam boshqa vasiyga yozilmaydi — "bir raqam, bir vasiy".</summary>
    [Fact]
    public async Task Band_raqam_rad_etiladi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (studentId, guardianId) = await SeedWithGuardianAsync(admin, tag);

        var otherPhone = NewPhone();
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"{Students}/{studentId}/guardians",
                new { fullName = $"Buvisi {tag}", phone = otherPhone, relation = "grandparent" }))
            .StatusCode);

        var clash = await admin.PutAsJsonAsync($"{Students}/{studentId}/guardians/{guardianId}",
            new { fullName = $"Ota-ona {tag}", phone = otherPhone, relation = "father" });
        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
        Assert.Equal(StudentGuardiansController.PhoneTakenMessage, await MessageAsync(clash));
    }

    /// <summary>
    /// Yagona vasiyni uzib bo'lmaydi; asosiysi uzilsa esa keyingisi asosiy
    /// bo'ladi va eski ustunlar unga tenglashadi.
    /// </summary>
    [Fact]
    public async Task Uzish_yagonasini_saqlaydi_va_asosiysini_kochiradi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (studentId, guardianId) = await SeedWithGuardianAsync(admin, tag);

        var only = await admin.DeleteAsync($"{Students}/{studentId}/guardians/{guardianId}");
        Assert.Equal(HttpStatusCode.BadRequest, only.StatusCode);
        Assert.Equal(StudentGuardiansController.LastGuardianMessage, await MessageAsync(only));

        var secondPhone = NewPhone();
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"{Students}/{studentId}/guardians",
                new { fullName = $"Buvisi {tag}", phone = secondPhone, relation = "grandparent" }))
            .StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"{Students}/{studentId}/guardians/{guardianId}")).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var link = Assert.Single(await db.StudentGuardians.AsNoTracking()
                .Where(l => l.StudentId == studentId).ToListAsync());
            Assert.True(link.IsPrimary);

            var student = await db.Students.AsNoTracking().FirstAsync(s => s.Id == studentId);
            Assert.Equal($"Buvisi {tag}", student.ParentFullName);
            Assert.Equal(secondPhone, student.ParentPhone);

            // Vasiy QATORI o'chmadi — u boshqa farzandga bog'langan bo'lishi mumkin.
            Assert.True(await db.Guardians.AnyAsync(g => g.Id == guardianId));
        });
    }

    // =====================================================================
    //  5. OTA-ONALAR EKRANI (§2.9)
    // =====================================================================

    /// <summary>
    /// Ro'yxat vasiy jadvalidan quriladi: qator = ODAM, farzandlari ichida.
    /// Tur bo'yicha filtr va'da qilganini toraytiradi.
    /// </summary>
    [Fact]
    public async Task Ota_onalar_royxati_vasiy_jadvalidan()
    {
        var tag = Tag();
        var fatherPhone = NewPhone();
        var motherPhone = NewPhone();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var payload = LegacyPayload(tag, fatherPhone);
        payload["guardians"] = new object[]
        {
            new { fullName = $"Otasi {tag}", phone = fatherPhone, relation = "father", isPrimary = true },
            new { fullName = $"Onasi {tag}", phone = motherPhone, relation = "mother", isPrimary = false },
        };
        var id = await CreateAsync(admin, payload);

        var rows = await RowsAsync(admin, $"{Parents}?search={tag}");
        Assert.Equal(2, rows.Count);

        var father = rows.Single(r => r.GetProperty("fullName").GetString() == $"Otasi {tag}");
        Assert.Equal(fatherPhone, father.GetProperty("phone").GetString());
        Assert.False(father.GetProperty("telegramLinked").GetBoolean());
        Assert.Equal(1, father.GetProperty("childrenCount").GetInt32());
        var child = father.GetProperty("children").EnumerateArray().Single();
        Assert.Equal(id, child.GetProperty("studentId").GetString());
        Assert.Equal("father", child.GetProperty("relation").GetString());
        Assert.True(child.GetProperty("isPrimary").GetBoolean());

        // Tur bo'yicha filtr — faqat onasi.
        var mothers = await RowsAsync(admin, $"{Parents}?search={tag}&relation=mother");
        var single = Assert.Single(mothers);
        Assert.Equal($"Onasi {tag}", single.GetProperty("fullName").GetString());

        // Sinf bo'yicha filtr — mos kelmasa bo'sh.
        Assert.Empty(await RowsAsync(admin, $"{Parents}?search={tag}&className=YO-Q-SINF"));

        // Eski yo'l TEGILMAGAN: o'z shaklida javob beradi.
        var legacy = await admin.GetAsync("/api/admin/parents");
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
    }

    /// <summary>Arxivlangan o'quvchining vasiysi sukut bo'yicha ro'yxatda ko'rinmaydi.</summary>
    [Fact]
    public async Task Arxivdagi_farzand_sukut_boyicha_yashirinadi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (studentId, _) = await SeedWithGuardianAsync(admin, tag);

        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.FirstAsync(s => s.Id == studentId);
            student.IsArchived = true;
            student.ArchivedAt = "2026-09-01";
            await db.SaveChangesAsync();
        });

        Assert.Empty(await RowsAsync(admin, $"{Parents}?search={tag}"));
        Assert.Single(await RowsAsync(admin, $"{Parents}?search={tag}&state=archived"));
        Assert.Single(await RowsAsync(admin, $"{Parents}?search={tag}&state=all"));
    }

    /// <summary>Eksport o'sha filtrdan oziqlanadi va .xlsx qaytaradi.</summary>
    [Fact]
    public async Task Eksport_xlsx_qaytaradi()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await SeedWithGuardianAsync(admin, tag);

        var response = await admin.GetAsync($"{Parents}/export?search={tag}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        var rows = StudentListTests.XlsxRows(await response.Content.ReadAsByteArrayAsync(), 8);
        Assert.True(rows.Count >= 2, "Sarlavha va kamida bitta qator kutilgan");
        Assert.Equal("Vasiy F.I.SH", rows[0][0]);
        Assert.Contains(rows.Skip(1), r => r[0] == $"Ota-ona {tag}");
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static string Tag() => "V" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Butun bazada takrorlanmas raqam: <c>guardians.phone_key</c> unikal va u
    /// OXIRGI 9 RAQAMDAN iborat, shuning uchun aynan shu 9 raqam tasodifiy.
    /// </summary>
    private static string NewPhone() =>
        "+998" + Random.Shared.Next(100_000_000, 999_999_999).ToString();

    private static Dictionary<string, object?> LegacyPayload(string tag, string phone) => new()
    {
        ["fullName"] = $"O'quvchi {tag}",
        ["birthDate"] = "2015-05-05",
        ["address"] = "Toshkent",
        ["gender"] = "male",
        ["parentFullName"] = $"Ota-ona {tag}",
        ["parentPhone"] = phone,
        ["className"] = $"V-{tag[..4]}",
        ["enrollmentDate"] = "2026-09-01",
    };

    private static async Task<string> CreateAsync(HttpClient client, object payload)
    {
        var response = await client.PostAsJsonAsync(Students, payload);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Bitta o'quvchi va uning bitta (asosiy) vasiysi.</summary>
    private async Task<(string StudentId, string GuardianId)> SeedWithGuardianAsync(
        HttpClient admin, string tag)
    {
        var phone = NewPhone();
        var id = await CreateAsync(admin, LegacyPayload(tag, phone));
        return (id, await GuardianIdAsync(admin, id, phone));
    }

    private async Task<string> GuardianIdAsync(HttpClient client, string studentId, string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        var key = digits.Length > 9 ? digits[^9..] : digits;

        var list = await GetJsonAsync(client, $"{Students}/{studentId}/guardians");
        var row = list.EnumerateArray()
            .First(g => (g.GetProperty("phone").GetString() ?? "").EndsWith(key, StringComparison.Ordinal));
        return row.GetProperty("guardianId").GetString()!;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private static async Task<List<JsonElement>> RowsAsync(HttpClient client, string url) =>
        [.. (await GetJsonAsync(client, url)).EnumerateArray()];

    private static async Task<string?> MessageAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("message").GetString();
    }
}
