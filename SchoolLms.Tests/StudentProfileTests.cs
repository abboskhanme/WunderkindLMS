using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// O'quvchi kartochkasi — docs/modules/students-parity.md §2.3 (S-10, S-12)
/// va §2.8 (L-1).
///
/// <para>
/// Bu yerdagi eng muhim tekshiruv — <b>PUL DARVOZASI</b>: kartochka sarlavhasi
/// balansni umuman qaytarmaydi, faoliyat tarixida esa moliya huquqi
/// bo'lmagan xodim moliyaviy yozuvlarni ham, `before`/`after` ichidagi summa
/// maydonlarini ham KO'RMAYDI.
/// </para>
/// <para>
/// Baza yaratilmaydi: umumiy <see cref="ApiFixture"/> ishlatiladi (kollektsiya
/// fixture'i o'zining <c>DisposeAsync</c> ida hamma narsani yopadi).
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StudentProfileTests(ApiFixture fixture)
{
    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/students/x/card")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/students/x/timetable")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/students/x/attendance-range")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/students/x/activity")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync("/api/admin/students/x/location", new { latitude = 41.0, longitude = 69.0 })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "students");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/students/x/card")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/students/x/activity")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync("/api/admin/students/x/location", new { latitude = 41.0, longitude = 69.0 })).StatusCode);
    }

    /// <summary>Xodim kartochkani o'qiydi; "students" ruxsatisiz joylashuvni YOZA olmaydi.</summary>
    [Fact]
    public async Task Xodim_oqiydi_lekin_ruxsatsiz_yozmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Staff);
        var student = await SeedStudentAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/admin/students/{student}/card")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"/api/admin/students/{student}/location",
                new { latitude = 41.3, longitude = 69.2, address = "Toshkent" })).StatusCode);
    }

    [Fact]
    public async Task Yoq_oquvchi_404()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var missing = "yo-q-" + Guid.NewGuid().ToString("N");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/admin/students/{missing}/card")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/admin/students/{missing}/activity")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"/api/admin/students/{missing}/location", new { latitude = 41.0, longitude = 69.0 })).StatusCode);
    }

    // =====================================================================
    //  2. Kartochka sarlavhasi
    // =====================================================================

    [Fact]
    public async Task Kartochkada_holat_login_va_maktabdagi_kunlar_bor_lekin_balans_yoq()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var status = new StudentStatus { Name = $"VIP {tag}", Color = "#112233", Position = 1 };
        var student = NewStudent($"Kartochka {tag}");
        student.Phone = "+998901234567";
        student.Language = "ru";
        student.DocumentUrl = "/uploads/hujjat.pdf";

        await fixture.Api.WithDbAsync(async db =>
        {
            db.StudentStatuses.Add(status);
            student.StatusId = status.Id;
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });

        using var json = await JsonAsync(client, $"/api/admin/students/{student.Id}/card");
        var root = json.RootElement;

        Assert.Equal($"VIP {tag}", root.GetProperty("statusName").GetString());
        Assert.Equal("#112233", root.GetProperty("statusColor").GetString());
        Assert.Equal("+998901234567", root.GetProperty("phone").GetString());
        Assert.Equal("ru", root.GetProperty("language").GetString());
        Assert.Equal("/uploads/hujjat.pdf", root.GetProperty("documentUrl").GetString());
        Assert.True(root.GetProperty("activeDays").GetInt32() > 0);

        // PUL QOIDASI: kartochka balansni qaytarmaydi — u moliya tab'ida.
        Assert.Equal(JsonValueKind.Null, root.GetProperty("student").GetProperty("balance").ValueKind);
    }

    // =====================================================================
    //  3. Faoliyat tarixi — pul darvozasi (S-12)
    // =====================================================================

    [Fact]
    public async Task Moliya_huquqisiz_xodim_pul_yozuvini_ham_summani_ham_kormaydi()
    {
        var tag = Tag();
        var student = NewStudent($"Tarix {tag}");

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            db.AuditLogs.Add(new AuditLog
            {
                EntityType = AuditService.EntityPayment,
                EntityId = "p-" + tag,
                Action = "create",
                Timestamp = "2026-09-10T10:00:00",
                ActorName = "Kassir",
                Summary = $"To'lov qabul qilindi {tag}",
                After = """{"amount":500000,"method":"cash"}""",
                StudentId = student.Id,
            });
            db.AuditLogs.Add(new AuditLog
            {
                EntityType = StudentProfileController.LocationEntity,
                EntityId = student.Id,
                Action = "update",
                Timestamp = "2026-09-11T10:00:00",
                ActorName = "Xodim",
                Summary = $"Joylashuv yangilandi {tag}",
                After = """{"latitude":41.3,"amount":777}""",
                StudentId = student.Id,
            });
            await db.SaveChangesAsync();
        });

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        var staffRows = await RowsAsync(staff, $"/api/admin/students/{student.Id}/activity");

        // Moliyaviy yozuv umuman kelmaydi.
        Assert.DoesNotContain(staffRows, r => r.GetProperty("entityType").GetString() == AuditService.EntityPayment);
        var locationRow = Assert.Single(staffRows);
        var after = locationRow.GetProperty("after").GetString() ?? "";
        Assert.Contains("latitude", after);
        Assert.DoesNotContain("amount", after);

        // Admin (moliya hisobotlarini ko'ra oladi) — ikkalasi ham to'liq.
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var adminRows = await RowsAsync(admin, $"/api/admin/students/{student.Id}/activity");
        Assert.Equal(2, adminRows.Count);
        var payment = adminRows.First(r => r.GetProperty("entityType").GetString() == AuditService.EntityPayment);
        Assert.Contains("500000", payment.GetProperty("after").GetString() ?? "");
    }

    // =====================================================================
    //  4. Joylashuv (L-1)
    // =====================================================================

    [Fact]
    public async Task Joylashuv_yoziladi_va_tozalanadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var student = await SeedStudentAsync();

        var saved = await client.PutAsJsonAsync($"/api/admin/students/{student}/location",
            new { latitude = 41.311081, longitude = 69.240562, address = "Chilonzor 9-kvartal" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var body = JsonDocument.Parse(await saved.Content.ReadAsStringAsync());
        Assert.Equal(41.311081, body.RootElement.GetProperty("latitude").GetDouble(), 6);
        Assert.Equal("Chilonzor 9-kvartal", body.RootElement.GetProperty("locationAddress").GetString());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("locationUpdatedAt").GetString()));

        // Audit izi qoladi (kim, qachon, nima).
        await fixture.Api.WithDbAsync(async db =>
        {
            var row = await db.AuditLogs.AsNoTracking()
                .FirstOrDefaultAsync(a => a.StudentId == student
                    && a.EntityType == StudentProfileController.LocationEntity);
            Assert.NotNull(row);
        });

        // Tozalash — uchala maydon ham null.
        var cleared = await client.PutAsJsonAsync($"/api/admin/students/{student}/location",
            new { latitude = (double?)null, longitude = (double?)null, address = (string?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using var after = JsonDocument.Parse(await cleared.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, after.RootElement.GetProperty("latitude").ValueKind);
        Assert.Equal(JsonValueKind.Null, after.RootElement.GetProperty("locationUpdatedAt").ValueKind);
    }

    [Fact]
    public async Task Buzuq_koordinata_rad_etiladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var student = await SeedStudentAsync();

        var half = await client.PutAsJsonAsync($"/api/admin/students/{student}/location",
            new { latitude = 41.3, longitude = (double?)null });
        Assert.Equal(HttpStatusCode.BadRequest, half.StatusCode);
        Assert.Equal(StudentProfileController.HalfCoordinateMessage, await MessageAsync(half));

        var wild = await client.PutAsJsonAsync($"/api/admin/students/{student}/location",
            new { latitude = 941.3, longitude = 69.2 });
        Assert.Equal(HttpStatusCode.BadRequest, wild.StatusCode);
        Assert.Equal(StudentProfileController.BadCoordinatesMessage, await MessageAsync(wild));
    }

    // =====================================================================
    //  5. Davomat oralig'i
    // =====================================================================

    /// <summary>
    /// Maxraj — O'TILGAN darslar; "kech keldi" yo'qlik sifatida sanalmaydi
    /// (shaxsiy daftardagi ta'rifning aynan o'zi).
    /// </summary>
    [Fact]
    public async Task Davomat_oraligi_fan_kesimida_hisoblanadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var cls = new SchoolClass { Name = $"KD-{tag}", Grade = 7 };
        var subject = new Subject { Name = $"Algebra {tag}" };
        var absent = new AbsenceReason { Name = $"Kelmadi {tag}", Short = "K", IsLate = false };
        var late = new AbsenceReason { Name = $"Kechikdi {tag}", Short = "Kch", IsLate = true };
        var student = NewStudent($"Davomat {tag}");
        student.ClassName = cls.Name;

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.AbsenceReasons.AddRange(absent, late);
            db.Students.Add(student);
            await db.SaveChangesAsync();

            // Uchta o'tilgan dars: birida yo'qlik, birida kechikish, biri toza.
            for (var day = 1; day <= 3; day++)
                db.LessonNotes.Add(new LessonNote
                {
                    ClassId = cls.Id, SubjectId = subject.Id, Quarter = 1,
                    Date = $"2026-03-0{day}", Period = 1, Topic = "Mavzu", Conducted = true,
                });

            // Oraliqdan TASHQARIDAGI dars — hisobotga tushmasligi kerak.
            db.LessonNotes.Add(new LessonNote
            {
                ClassId = cls.Id, SubjectId = subject.Id, Quarter = 1,
                Date = "2026-04-01", Period = 1, Topic = "Keyingi oy", Conducted = true,
            });

            db.JournalEntries.Add(new JournalEntry
            {
                ClassId = cls.Id, SubjectId = subject.Id, Quarter = 1, StudentId = student.Id,
                Date = "2026-03-01", Period = 1, ReasonId = absent.Id,
            });
            db.JournalEntries.Add(new JournalEntry
            {
                ClassId = cls.Id, SubjectId = subject.Id, Quarter = 1, StudentId = student.Id,
                Date = "2026-03-02", Period = 1, ReasonId = late.Id,
            });
            await db.SaveChangesAsync();
        });

        using var json = await JsonAsync(client,
            $"/api/admin/students/{student.Id}/attendance-range?from=2026-03-01&to=2026-03-31");
        var root = json.RootElement;

        Assert.Equal(3, root.GetProperty("planned").GetInt32());
        Assert.Equal(1, root.GetProperty("absent").GetInt32());
        Assert.Equal(1, root.GetProperty("late").GetInt32());
        Assert.Equal(2, root.GetProperty("attended").GetInt32());
        Assert.Equal(67, root.GetProperty("pct").GetInt32());

        var subjectRow = Assert.Single(root.GetProperty("subjects").EnumerateArray().ToList());
        Assert.Equal(subject.Name, subjectRow.GetProperty("subjectName").GetString());
        Assert.Equal(3, subjectRow.GetProperty("planned").GetInt32());
        Assert.Equal(1, subjectRow.GetProperty("absent").GetInt32());

        // Kun kesimi — oy kalendari uchun; faqat dars o'tilgan kunlar.
        Assert.Equal(3, root.GetProperty("days").GetArrayLength());

        // Sabablar taqsimoti — ikkala sabab ham ko'rinadi.
        Assert.Equal(2, root.GetProperty("reasons").GetArrayLength());
    }

    /// <summary>Oraliq berilmasa ham javob keladi (oxirgi 30 kun) — ekran bo'sh qolmaydi.</summary>
    [Fact]
    public async Task Oraliq_berilmasa_standart_davr()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var student = await SeedStudentAsync();

        using var json = await JsonAsync(client, $"/api/admin/students/{student}/attendance-range");
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("from").GetString()));
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("to").GetString()));
        Assert.Equal(0, json.RootElement.GetProperty("planned").GetInt32());
    }

    // =====================================================================
    //  6. Jadval
    // =====================================================================

    /// <summary>Jadval biriktirilmagan o'quvchida bo'sh ro'yxat (500 emas).</summary>
    [Fact]
    public async Task Jadval_bosh_bolsa_ham_javob_beradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var student = await SeedStudentAsync();

        using var json = await JsonAsync(client, $"/api/admin/students/{student}/timetable");
        Assert.Equal(0, json.RootElement.GetProperty("lessons").GetArrayLength());
        Assert.True(json.RootElement.GetProperty("quarter").GetInt32() >= 1);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static string Tag() => "P" + Guid.NewGuid().ToString("N")[..8];

    private static Student NewStudent(string fullName) => new()
    {
        FullName = fullName,
        LastName = "Familiya",
        FirstName = "Ism",
        MiddleName = "Otasi",
        BirthDate = "2013-01-01",
        Address = "Toshkent",
        Gender = "male",
        ParentFullName = "Ota-ona",
        ParentLastName = "Familiya",
        ParentFirstName = "Ism",
        ParentMiddleName = "Otasi",
        ParentPhone = "+998901112233",
        ClassName = "",
        EnrollmentDate = "2025-09-01",
    };

    private async Task<string> SeedStudentAsync()
    {
        var student = NewStudent($"Profil {Tag()}");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });
        return student.Id;
    }

    private static async Task<JsonDocument> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<List<JsonElement>> RowsAsync(HttpClient client, string url)
    {
        using var json = await JsonAsync(client, url);
        return [.. json.RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("message").GetString() ?? "";
    }
}
