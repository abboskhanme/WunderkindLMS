using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Xonalar reyestri — docs/modules/students-parity.md §2.6 (R-1):
/// CRUD, tur ro'yxati, ommaviy yaratish va sinf ko'rsatgan xonani himoya qilish.
///
/// <para>
/// <b>Tur alohida tekshiriladi.</b> Bazada <c>kind</c> uchun CHECK constraint
/// ATAYLAB yo'q (Rooms.cs), ya'ni yagona darvoza — controller. Agar u
/// tekshirmasa, jadval moduli tushunmaydigan matn bazaga tushib ketardi va
/// buni keyin faqat ma'lumot tozalash bilan tuzatib bo'lardi.
/// </para>
/// <para>
/// <b>Ruxsat — <c>students</c> (F-4 tuzatuvidan keyin).</b> Xona "O'quv
/// bo'limi" menyusida (Fanlar bilan yonma-yon), shuning uchun darvoza ham
/// o'sha bo'lim kaliti. Test buni ikki tomondan qamrab oladi: <c>students</c>
/// li xodim yozadi, <c>classes</c> li (va eski <c>schedule</c> li) xodim
/// yozmaydi — ikkinchisi ATAYLAB: F-4 dan oldin xuddi shu ruxsat yozar edi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class RoomTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/rooms";

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(Url, new { name = "101" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync($"{Url}/multiple", new { count = 3 })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "students");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Url, new { name = "101" })).StatusCode);
    }

    /// <summary>
    /// Xodim reyestrni o'qiydi; "students" ruxsatisiz yoza olmaydi —
    /// "classes" ruxsati ham, ESKI "schedule" ruxsati ham yordam bermaydi
    /// (F-4: darvoza endi "O'quv bo'limi" kaliti bilan bir xil).
    /// </summary>
    [Theory]
    [InlineData("classes")]
    [InlineData("schedule")]
    public async Task Xodim_oqiydi_lekin_ruxsatsiz_yozmaydi(string perm)
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, perm);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Url}/buildings")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Url, new { name = "X-" + Tag() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"{Url}/multiple", new { count = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}", new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.DeleteAsync($"{Url}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Oquv_bolimi_ruxsatli_xodim_yozadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff, "students");

        var created = await client.PostAsJsonAsync(Url, new { name = "X-" + Tag() });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    }

    // =====================================================================
    //  2. CRUD
    // =====================================================================

    [Fact]
    public async Task Crud_va_takroriy_nom()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var name = $"Lab-{tag}";

        var created = await client.PostAsJsonAsync(Url, new
        {
            name,
            building = $"Bino {tag}",
            floor = 2,
            capacity = 18,
            kind = "lab",
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var row = await JsonAsync(created);
        var id = row.GetProperty("id").GetGuid();
        Assert.Equal(name, row.GetProperty("name").GetString());
        Assert.Equal("lab", row.GetProperty("kind").GetString());
        Assert.Equal(18, row.GetProperty("capacity").GetInt32());
        Assert.Equal(2, row.GetProperty("floor").GetInt32());
        Assert.Equal(0, row.GetProperty("usedByClasses").GetInt32());

        // Nom unikal.
        var duplicate = await client.PostAsJsonAsync(Url, new { name });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(RoomsController.NameTakenMessage, await MessageAsync(duplicate));

        // Nomsiz.
        var noName = await client.PostAsJsonAsync(Url, new { name = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        Assert.Equal(RoomsController.NameRequiredMessage, await MessageAsync(noName));

        // Tahrir: sig'im va tur berilmasa joyida qoladi, bino tozalanadi.
        var updated = await client.PutAsJsonAsync($"{Url}/{id}", new { name = $"{name}-yangi" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var after = await JsonAsync(updated);
        Assert.Equal($"{name}-yangi", after.GetProperty("name").GetString());
        Assert.Equal("lab", after.GetProperty("kind").GetString());
        Assert.Equal(18, after.GetProperty("capacity").GetInt32());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("building").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Url}/{id}")).StatusCode);
        Assert.DoesNotContain(await RowsAsync(client, $"{Url}?search={name}"),
            r => r.GetProperty("id").GetGuid() == id);
    }

    /// <summary>
    /// Tur faqat ro'yxatdan (baza CHECK bilan cheklamaydi — Rooms.cs).
    /// Sig'im va qavat ham shu yerda tekshiriladi.
    /// </summary>
    [Fact]
    public async Task Tur_royxatdan_boladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var bad = await client.PostAsJsonAsync(Url, new { name = $"T-{tag}", kind = "ombor" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(RoomsController.KindMessage, await MessageAsync(bad));

        var badCapacity = await client.PostAsJsonAsync(Url, new { name = $"T2-{tag}", capacity = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, badCapacity.StatusCode);
        Assert.Equal(RoomsController.CapacityMessage, await MessageAsync(badCapacity));

        var badFloor = await client.PostAsJsonAsync(Url, new { name = $"T3-{tag}", floor = 101 });
        Assert.Equal(HttpStatusCode.BadRequest, badFloor.StatusCode);
        Assert.Equal(RoomsController.FloorMessage, await MessageAsync(badFloor));

        // Barcha to'g'ri turlar qabul qilinadi; sukut — classroom.
        foreach (var kind in RoomKind.All)
            Assert.Equal(HttpStatusCode.OK,
                (await client.PostAsJsonAsync(Url, new { name = $"{kind}-{tag}", kind })).StatusCode);

        var plain = await JsonAsync(await client.PostAsJsonAsync(Url, new { name = $"Sukut-{tag}" }));
        Assert.Equal(RoomKind.Classroom, plain.GetProperty("kind").GetString());
        Assert.Equal(30, plain.GetProperty("capacity").GetInt32());

        // Noto'g'ri tur bilan FILTR ham 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Url}?kind=ombor")).StatusCode);
    }

    /// <summary>
    /// Yangi xona har doim faol (entity DEFAULT'i). "isActive" filtri va
    /// tahrirlashdagi qisman yangilash (berilmasa — joyida qoladi).
    /// </summary>
    [Fact]
    public async Task Faollik_sukut_true_va_filtrlanadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var name = $"Faol-{tag}";

        var created = await JsonAsync(await client.PostAsJsonAsync(Url, new { name }));
        var id = created.GetProperty("id").GetGuid();
        Assert.True(created.GetProperty("isActive").GetBoolean());

        // Faolsizlantirish: boshqa maydonlar tegilmaydi.
        var off = await JsonAsync(await client.PutAsJsonAsync($"{Url}/{id}",
            new { name, isActive = false }));
        Assert.False(off.GetProperty("isActive").GetBoolean());
        Assert.Equal(name, off.GetProperty("name").GetString());

        // Ro'yxatdagi filtr.
        var actives = await RowsAsync(client, $"{Url}?isActive=true&search={name}");
        Assert.DoesNotContain(actives, r => r.GetProperty("id").GetGuid() == id);
        var inactives = await RowsAsync(client, $"{Url}?isActive=false&search={name}");
        Assert.Contains(inactives, r => r.GetProperty("id").GetGuid() == id);
        // Filtrsiz — hammasi (mavjud o'quvchilar/sinflar ekranlarini buzmaslik uchun).
        var all = await RowsAsync(client, $"{Url}?search={name}");
        Assert.Contains(all, r => r.GetProperty("id").GetGuid() == id);

        // isActive berilmasa — joyida qoladi.
        var untouched = await JsonAsync(await client.PutAsJsonAsync($"{Url}/{id}", new { name }));
        Assert.False(untouched.GetProperty("isActive").GetBoolean());

        // Qaytadan faollashtirish.
        var on = await JsonAsync(await client.PutAsJsonAsync($"{Url}/{id}",
            new { name, isActive = true }));
        Assert.True(on.GetProperty("isActive").GetBoolean());
    }

    /// <summary>
    /// Sinf ko'rsatgan xonani ham FAOLSIZLANTIRISH mumkin — bu o'chirish emas:
    /// sinf ekranlari o'zgarmaydi, faqat yangi tanlovda ko'rinmay qoladi.
    /// </summary>
    [Fact]
    public async Task Faolsizlantirish_ishlatilgan_xonada_ham_ishlaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var name = $"Band-faolsiz-{tag}";

        var id = (await JsonAsync(await client.PostAsJsonAsync(Url, new { name })))
            .GetProperty("id").GetGuid();

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(new SchoolClass { Name = $"8-{tag[..4]}", Grade = 8, Room = name });
            await db.SaveChangesAsync();
        });

        var off = await client.PutAsJsonAsync($"{Url}/{id}", new { name, isActive = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        var dto = await JsonAsync(off);
        Assert.False(dto.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, dto.GetProperty("usedByClasses").GetInt32());

        // O'CHIRISH hamon rad etiladi — faolsizlantirish uni bekor qilmaydi.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"{Url}/{id}")).StatusCode);
    }

    // =====================================================================
    //  3. SINF KO'RSATGAN XONA
    // =====================================================================

    /// <summary>
    /// Sinf ko'rsatgan xona o'chirilmaydi — <c>rooms.is_active</c> qo'shilgandan
    /// keyin ham shunday (o'chirish va faolsizlantirish ikki xil amal, pastdagi
    /// <see cref="Faolsizlantirish_ishlatilgan_xonada_ham_ishlaydi"/> ga qarang).
    /// </summary>
    [Fact]
    public async Task Sinf_korsatgan_xona_ochirilmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var name = $"Band-{tag}";

        var id = (await JsonAsync(await client.PostAsJsonAsync(Url, new { name })))
            .GetProperty("id").GetGuid();

        var cls = new SchoolClass { Name = $"7-{tag[..4]}", Grade = 7, Room = name };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
        });

        var refused = await client.DeleteAsync($"{Url}/{id}");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("o'chirib bo'lmaydi", await MessageAsync(refused));

        // Ro'yxatda nechta sinf ishlatayotgani ko'rinadi.
        var row = (await RowsAsync(client, $"{Url}?search={name}")).Single();
        Assert.Equal(1, row.GetProperty("usedByClasses").GetInt32());

        // Sinf bo'shatilgach — o'chadi.
        await fixture.Api.WithDbAsync(async db =>
        {
            var tracked = await db.Classes.FindAsync(cls.Id);
            tracked!.Room = null;
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Url}/{id}")).StatusCode);
    }

    /// <summary>
    /// "Sinflardan ko'chirish" olib tashlandi (mijoz, 2026-09-23) — endpoint endi yo'q va
    /// sinflardagi xona matnidan reyestrga hech narsa qo'shilmaydi.
    /// </summary>
    [Fact]
    public async Task Sinflardan_kochirish_olib_tashlangan()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var res = await client.PostAsJsonAsync($"{Url}/import-from-classes", new { });
        Assert.True(res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Kutilgan 404/405, keldi {(int)res.StatusCode}");
    }

    // =====================================================================
    //  4. OMMAVIY YARATISH
    // =====================================================================

    [Fact]
    public async Task Ommaviy_yaratish()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var prefix = $"{tag}-";

        var response = await client.PostAsJsonAsync($"{Url}/multiple", new
        {
            count = 5,
            startFrom = 101,
            prefix,
            building = $"Bosh bino {tag}",
            floor = 1,
            capacity = 24,
            kind = "classroom",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await JsonAsync(response);
        var created = result.GetProperty("created").EnumerateArray().Select(e => e.Clone()).ToList();
        Assert.Equal(5, created.Count);
        Assert.Equal($"{prefix}101", created[0].GetProperty("name").GetString());
        Assert.Equal($"{prefix}105", created[4].GetProperty("name").GetString());
        Assert.All(created, r => Assert.Equal(24, r.GetProperty("capacity").GetInt32()));
        Assert.Empty(result.GetProperty("skipped").EnumerateArray());

        // Takror: band nomlar O'TKAZIB YUBORILADI, so'rov yiqilmaydi.
        var again = await JsonAsync(await client.PostAsJsonAsync($"{Url}/multiple",
            new { count = 3, startFrom = 104, prefix }));
        Assert.Single(again.GetProperty("created").EnumerateArray());
        Assert.Equal(2, again.GetProperty("skipped").GetArrayLength());

        // startFrom berilmasa — mavjud eng katta raqamdan keyingisi.
        var auto = await JsonAsync(await client.PostAsJsonAsync($"{Url}/multiple",
            new { count = 1, prefix }));
        Assert.Equal($"{prefix}107",
            auto.GetProperty("created")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Ommaviy_yaratish_chegaralari()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        foreach (var count in new[] { 0, 51 })
        {
            var bad = await client.PostAsJsonAsync($"{Url}/multiple", new { count });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Equal(RoomsController.CountMessage, await MessageAsync(bad));
        }

        var badKind = await client.PostAsJsonAsync($"{Url}/multiple", new { count = 2, kind = "ombor" });
        Assert.Equal(HttpStatusCode.BadRequest, badKind.StatusCode);
        Assert.Equal(RoomsController.KindMessage, await MessageAsync(badKind));

        var badStart = await client.PostAsJsonAsync($"{Url}/multiple",
            new { count = 2, startFrom = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, badStart.StatusCode);
        Assert.Equal(RoomsController.StartFromMessage, await MessageAsync(badStart));
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static string Tag() => "R" + Guid.NewGuid().ToString("N")[..8];

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static async Task<List<JsonElement>> RowsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. json.RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("message").GetString() ?? "";
    }
}
