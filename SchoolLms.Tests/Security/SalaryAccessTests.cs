using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Security;

// ===========================================================================
//  MAOSH VA PUL O'QISHLARI — XODIMGA YOPIQ. F3.05 va F0.02
//  (docs/modules/finance-parity.md §1.1, §2.3.4, §2.15; SPEC §4.3)
// ===========================================================================
//
//  NIMA OCHIQ EDI
//  --------------
//  `AdminPermAttribute` ikki narsani qiladi: HAR QANDAY xodimga (staff)
//  O'QISHNI ochadi (`:40-42`) va bo'lim kaliti bo'lganiga YOZISHNI beradi.
//  Shu sababli "teachers" kaliti bo'lgan xodim:
//    * `POST /api/admin/teachers/{id}/salary-payments` bilan maosh PULINI
//      jurnalga yoza olardi (SPEC §4.3 — "Record an expense", xodim ⛔);
//    * soat narxini va ustama foizini o'zgartirib, butun oylik fondni
//      qayta hisoblatib yubora olardi;
//  har qanday xodim esa maosh hisobotini, maosh daftarini va o'quvchining
//  to'lov daftarini o'qiy olardi.
//
//  BU TESTLAR NIMANI KAFOLATLAYDI
//  ------------------------------
//  Har tor darvoza ikki tomondan sinaladi: RAD ETILGAN rol (xodim, kaliti
//  bilan) 403 oladi, RUXSAT ETILGAN rol (admin) esa AMALNI BAJARADI —
//  ya'ni tuzatish teshikni yopdi, bo'limni emas. Oxirgi test buni alohida
//  tasdiqlaydi: xodim o'qituvchilar ro'yxatini bemalol o'qiydi.
// ===========================================================================

/// <summary>
/// Maosh amallari va pul o'qishlarining rol chegarasi. Batafsil: fayl
/// boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class SalaryAccessTests(ApiFixture fixture)
{
    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    // =================================================================
    //  F3.05 — maosh berish (chiqim yozish)
    // =================================================================

    /// <summary>
    /// Xodim maosh pulini yoza OLMAYDI (403), admin esa yozadi va chiqim
    /// jurnalga tushadi.
    /// </summary>
    [Fact]
    public async Task Maosh_berishni_xodim_bajara_olmaydi_admin_bajaradi()
    {
        var teacherId = await SeedTeacherAsync();
        var url = $"/api/admin/teachers/{teacherId}/salary-payments";
        var body = new { amount = 150_000m, method = PaymentMethod.Transfer, note = "Test maosh" };

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "teachers");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(url, body)).StatusCode);

        // Pul yozilmagani — 403 dan keyin bazada hech narsa qolmasligi kerak.
        await using (var db = NewDb())
            Assert.Empty(await db.Expenses.AsNoTracking().Where(e => e.TeacherId == teacherId).ToListAsync());

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var allowed = await admin.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        await using (var db = NewDb())
        {
            var expense = Assert.Single(await db.Expenses.AsNoTracking()
                .Where(e => e.TeacherId == teacherId).ToListAsync());
            Assert.Equal(150_000m, expense.Amount);
        }
    }

    /// <summary>
    /// Maosh tarixi va maosh daftari — moliya hisoboti (F0.02): xodimga 403,
    /// adminga 200.
    /// </summary>
    [Fact]
    public async Task Maosh_hisobotlarini_xodim_oqiy_olmaydi_admin_oqiydi()
    {
        var teacherId = await SeedTeacherAsync();
        string[] urls =
        [
            $"/api/admin/teachers/{teacherId}/salary-history",
            $"/api/admin/teachers/{teacherId}/salary-ledger",
            "/api/admin/finance/salary-report",
            // .xlsx nusxasi ham o'sha darvozadan o'tadi (2026-09-19): eksport
            // hisobotning boshqa shakli, boshqa ruxsati emas.
            "/api/admin/finance/salary-report/export",
        ];

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "teachers", "finance");
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        foreach (var url in urls)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
        }
    }

    // =================================================================
    //  F3.05 — narx va ustama YOZUVLARI
    // =================================================================

    /// <summary>
    /// Soat narxi va ustama foizi — maoshning ikki yarmi. Xodim ikkalasini
    /// ham o'zgartira olmaydi (403); admin o'zgartiradi.
    /// </summary>
    [Fact]
    public async Task Narx_va_ustama_yozuvlari_xodimga_yopiq()
    {
        var teacherId = await SeedTeacherAsync();

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "teachers");
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(
            $"/api/admin/salary-rates/{teacherId}/bonus", new { bonusPct = 70 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(
            "/api/admin/salary-rates/bonus",
            new { teacherIds = new[] { teacherId }, bonusPct = 80 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(
            "/api/admin/salary-rates", new { oliy = 1, t1 = 1, t2 = 1, mutaxasis = 1 })).StatusCode);

        // Rad etilgan so'rovlardan keyin ustama O'ZGARMAGAN.
        await using (var db = NewDb())
            Assert.Equal(0m, (await db.Teachers.AsNoTracking().FirstAsync(t => t.Id == teacherId)).BonusPct);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(
            $"/api/admin/salary-rates/{teacherId}/bonus", new { bonusPct = 70 })).StatusCode);

        await using (var db = NewDb())
            Assert.Equal(70m, (await db.Teachers.AsNoTracking().FirstAsync(t => t.Id == teacherId)).BonusPct);
    }

    /// <summary>
    /// Soat narxlarini admin saqlay oladi. Test MAVJUD qiymatlarni qaytarib
    /// yozadi: bu darvozani sinaydi, umumiy test bazasidagi narxlarni esa
    /// o'zgartirmaydi (ular boshqa testlarning maosh arifmetikasiga kiradi).
    /// </summary>
    [Fact]
    public async Task Soat_narxlarini_admin_saqlay_oladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var current = await admin.GetFromJsonAsync<SalaryRatesDto>("/api/admin/salary-rates");
        Assert.NotNull(current);

        var response = await admin.PutAsJsonAsync("/api/admin/salary-rates", new
        {
            oliy = current.Oliy,
            t1 = current.T1,
            t2 = current.T2,
            mutaxasis = current.Mutaxasis,
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>
    /// Maosh jadvalini (butun maktabning oyligi) xodim KO'RA olmaydi, admin
    /// ko'radi — F0.02.
    /// </summary>
    [Fact]
    public async Task Maosh_jadvalini_xodim_kora_olmaydi()
    {
        var teacherId = await SeedTeacherAsync();

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "teachers");
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/salary-rates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.GetAsync($"/api/admin/salary-rates/{teacherId}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/salary-rates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync($"/api/admin/salary-rates/{teacherId}")).StatusCode);
    }

    // =================================================================
    //  F3.05 — ustama o'zgarishi AUDITDA qoladi
    // =================================================================

    /// <summary>
    /// Ustama — pul qarori, ya'ni SPEC §4.6 bo'yicha audit izi qoldirishi
    /// shart. Ilgari <c>SetBonus</c> ham, <c>SetBonusBulk</c> ham hech narsa
    /// yozmasdi: "kim maoshga +50% qo'ydi" degan savolga javob yo'q edi.
    /// Guruh bilan tayinlashda har o'qituvchiga ALOHIDA qator.
    /// </summary>
    [Fact]
    public async Task Ustama_ozgarishi_audit_qatorini_qoldiradi()
    {
        var first = await SeedTeacherAsync();
        var second = await SeedTeacherAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(
            $"/api/admin/salary-rates/{first}/bonus", new { bonusPct = 30 })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(
            "/api/admin/salary-rates/bonus",
            new { teacherIds = new[] { first, second }, bonusPct = 45 })).StatusCode);

        await using var db = NewDb();
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "TeacherSalary" && (a.EntityId == first || a.EntityId == second))
            .ToListAsync();

        // Bittalik yozuv + guruh yozuvining ikkita qatori.
        Assert.Equal(2, rows.Count(a => a.EntityId == first));
        Assert.Single(rows, a => a.EntityId == second);
        Assert.Contains(rows, a => a.Summary.Contains("0% → 30%", StringComparison.Ordinal));
        Assert.Contains(rows, a => a.Summary.Contains("30% → 45%", StringComparison.Ordinal));
        Assert.All(rows, a => Assert.Equal(a.EntityId, a.TeacherId));
    }

    // =================================================================
    //  F0.02 — o'quvchining to'lov daftari
    // =================================================================

    /// <summary>
    /// O'quvchi to'lov daftari — moliya hisoboti: xodimga 403, adminga 200.
    /// Ro'yxatdagi <c>Balans</c> ustuni esa xodimga OCHIQ qoladi (Students
    /// moduli qarori) — pastdagi test shuni tasdiqlaydi.
    /// </summary>
    [Fact]
    public async Task Oquvchi_daftarini_xodim_oqiy_olmaydi_admin_oqiydi()
    {
        var studentId = await SeedStudentAsync();
        var url = $"/api/admin/students/{studentId}/ledger";

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
    }

    // =================================================================
    //  Teshik yopildi, bo'lim emas
    // =================================================================

    /// <summary>
    /// Xodim o'z ishini bajarishda davom etadi: o'qituvchilar va o'quvchilar
    /// ro'yxati (balans ustuni bilan) unga ochiq. Tuzatish FAQAT pul
    /// endpoint'larini yopdi.
    /// </summary>
    [Fact]
    public async Task Xodim_royxatlarni_oqiyveradi()
    {
        await SeedTeacherAsync();
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "teachers", "students");

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/admin/teachers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/admin/students")).StatusCode);
    }

    // =================================================================
    //  Yordamchilar
    // =================================================================

    private async Task<string> SeedTeacherAsync()
    {
        await using var db = NewDb();
        var teacher = new Teacher
        {
            FullName = "Maosh Testi " + Guid.NewGuid().ToString("N")[..6],
            Category = "1",
            SalaryStartDate = "2023-09-01",
            SalaryStartMonth = "2023-09",
        };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();
        return teacher.Id;
    }

    private async Task<string> SeedStudentAsync()
    {
        await using var db = NewDb();
        var student = new Student
        {
            FullName = "Daftar Testi " + Guid.NewGuid().ToString("N")[..6],
            LastName = "Familiya",
            FirstName = "Ism",
            MiddleName = "Otasi",
            BirthDate = "2015-01-01",
            Address = "Toshkent",
            Gender = "female",
            ParentFullName = "Ota-ona",
            ParentLastName = "Familiya",
            ParentFirstName = "Ism",
            ParentMiddleName = "Otasi",
            ParentPhone = "+998901112233",
            ClassName = "5-A",
            EnrollmentDate = "2023-09-01",
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return student.Id;
    }
}
