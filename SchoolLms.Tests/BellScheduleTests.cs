using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Har xil sinflarda har xil dars vaqtlari (mijoz, 2026-10-11: "istalgancha dars soatlarini tahrirlash
/// mumkin bo'lsin ... kichik sinflarda dars soatlari va vaqtlari boshqacha bo'lishi mumkin").
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class BellScheduleTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/settings/bell-schedules";

    private static object Body(string name, IEnumerable<string> owners, params (string Start, string End)[] periods) => new
    {
        name,
        periods = periods.Select((p, i) => new { period = i + 1, startTime = p.Start, endTime = p.End }),
        ownerIds = owners,
    };

    [Fact]
    public async Task Jadval_tuziladi_sinf_biriktiriladi_va_vaqt_shu_jadvaldan_olinadi()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var small = new SchoolClass { Name = $"1-{tag}", Grade = 1 };
        var big = new SchoolClass { Name = $"9-{tag}", Grade = 9 };
        var teacher = new Teacher { FullName = $"Ustoz {tag}" };
        var subject = new Subject { Name = $"Fan {tag}" };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(small, big);
            db.Teachers.Add(teacher);
            db.Subjects.Add(subject);
            db.ScheduleTemplates.Add(new ScheduleTemplate
            {
                ClassId = small.Id, Name = "Asosiy",
                Lessons = [new ScheduleLesson { Day = 0, Period = 2, SubjectId = subject.Id, TeacherId = teacher.Id }],
            });
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var created = await admin.PostAsJsonAsync(Url,
            Body($"Boshlang'ich {tag}", [small.Id], ("08:00", "08:40"), ("08:50", "09:30")));
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(created.IsSuccessStatusCode, body.ToString());
        var id = body.GetProperty("id").GetGuid();

        try
        {
            // Sozlamalar sahifasi: sinf shu jadvalda, ikkinchisi — asosiyda.
            var overview = await admin.GetFromJsonAsync<BellSchedulesDto>(Url);
            Assert.Equal(id, overview!.Owners.Single(o => o.Id == small.Id).ScheduleId);
            Assert.Null(overview.Owners.Single(o => o.Id == big.Id).ScheduleId);
            var settings = await admin.GetFromJsonAsync<SchoolSettingsDto>("/api/admin/settings");
            Assert.Contains(settings!.BellSchedules!, b => b.Id == id && b.OwnerIds.SequenceEqual([small.Id]));

            await fixture.Api.WithDbAsync(async db =>
            {
                var bells = await BellTimes.LoadAsync(db);
                Assert.Equal(new BellTime("08:50", "09:30"), bells.Time(small.Id, 2));
                Assert.Null(bells.Time(small.Id, 3));
                var main = await db.LessonTimes.AsNoTracking().FirstOrDefaultAsync(t => t.Period == 2);
                Assert.Equal(main?.StartTime, bells.Time(big.Id, 2)?.StartTime);
            });

            // O'qituvchi bandligi ham egasining vaqtini oladi.
            var occupied = await admin.GetFromJsonAsync<Dictionary<string, List<OccupiedSlotDto>>>(
                "/api/admin/schedule/occupied-slots");
            var slot = Assert.Single(occupied![teacher.Id]);
            Assert.Equal(("08:50", "09:30"), (slot.StartTime, slot.EndTime));

            // Ikkinchi sinf qo'shiladi, birinchisi chiqariladi — to'liq almashtirish.
            var updated = await admin.PutAsJsonAsync($"{Url}/{id}",
                Body($"Boshlang'ich {tag}", [big.Id], ("08:00", "08:45")));
            Assert.True(updated.IsSuccessStatusCode, await updated.Content.ReadAsStringAsync());
            var saved = await updated.Content.ReadFromJsonAsync<BellScheduleDto>();
            Assert.Equal([big.Id], saved!.OwnerIds);
            Assert.Single(saved.Periods);

            // Ikkinchi jadval birinchisidagi sinfni o'ziga oladi — bitta ega, bitta jadval.
            var other = await admin.PostAsJsonAsync(Url, Body($"Boshqa {tag}", [big.Id], ("09:00", "09:45")));
            var otherId = (await other.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            overview = await admin.GetFromJsonAsync<BellSchedulesDto>(Url);
            Assert.Equal(otherId, overview!.Owners.Single(o => o.Id == big.Id).ScheduleId);
            Assert.Empty(overview.Schedules.Single(s => s.Id == id).OwnerIds);

            // O'chirilgan jadvalning sinfi asosiy jadvalga qaytadi.
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{otherId}")).StatusCode);
            overview = await admin.GetFromJsonAsync<BellSchedulesDto>(Url);
            Assert.Null(overview!.Owners.Single(o => o.Id == big.Id).ScheduleId);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{Url}/{otherId}")).StatusCode);
        }
        finally
        {
            await admin.DeleteAsync($"{Url}/{id}");
        }
    }

    [Fact]
    public async Task Ochirilgan_sinf_jadvalni_saqlashga_xalaqit_bermaydi()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var gone = new SchoolClass { Name = $"G-{tag}", Grade = 2 };
        var stays = new SchoolClass { Name = $"S-{tag}", Grade = 2 };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(gone, stays);
            await db.SaveChangesAsync();
        });
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var created = await admin.PostAsJsonAsync(Url, Body($"Qoldiq {tag}", [gone.Id, stays.Id], ("08:00", "08:45")));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        try
        {
            // Sinf o'chirilsa biriktirilishi ham ketadi.
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/classes/{gone.Id}")).StatusCode);
            await fixture.Api.WithDbAsync(async db =>
                Assert.False(await db.BellScheduleOwners.AnyAsync(o => o.OwnerId == gone.Id)));

            // Qoldiq id (masalan eski ekran yuborgan) jadvalni saqlashga to'sqinlik qilmaydi.
            await fixture.Api.WithDbAsync(async db =>
            {
                db.BellScheduleOwners.Add(new BellScheduleOwner { OwnerId = "eski-sinf", ScheduleId = id });
                await db.SaveChangesAsync();
            });
            var saved = await admin.PutAsJsonAsync($"{Url}/{id}",
                Body($"Qoldiq {tag}", ["eski-sinf", stays.Id], ("08:00", "08:45")));
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            Assert.Equal([stays.Id], (await saved.Content.ReadFromJsonAsync<BellScheduleDto>())!.OwnerIds);
        }
        finally
        {
            await admin.DeleteAsync($"{Url}/{id}");
        }
    }

    [Fact]
    public async Task Notogri_jadval_rad_etiladi()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        async Task<string> Rejected(object body)
        {
            var r = await admin.PostAsJsonAsync(Url, body);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()!;
        }

        Assert.Contains("nom", await Rejected(Body(" ", [], ("08:00", "08:45"))), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bitta", await Rejected(Body($"Bo'sh {tag}", [])));
        Assert.Contains("tugash", await Rejected(Body($"Teskari {tag}", [], ("09:00", "08:45"))));
        Assert.Contains("oldingi", await Rejected(Body($"Ustma-ust {tag}", [], ("08:00", "08:45"), ("08:40", "09:20"))));
        Assert.Contains("SS:DD", await Rejected(Body($"Yaroqsiz {tag}", [], ("8 da", "09:00"))));
        Assert.Contains("topilmadi", await Rejected(Body($"Egasiz {tag}", ["yo'q-sinf"], ("08:00", "08:45"))));

        var ok = await admin.PostAsJsonAsync(Url, Body($"Nomi {tag}", [], ("08:00", "08:45")));
        var id = (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        try
        {
            Assert.Contains("allaqachon", await Rejected(Body($"NOMI {tag}", [], ("08:00", "08:45"))));
            Assert.Equal(HttpStatusCode.NotFound,
                (await admin.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}", Body($"X {tag}", [], ("08:00", "08:45")))).StatusCode);
        }
        finally
        {
            await admin.DeleteAsync($"{Url}/{id}");
        }
    }

    [Fact]
    public async Task Faqat_sozlamalar_ruxsati_bilan_ozgartiriladi()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        using var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, "settings:view");
        using var outsider = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        using var editor = await fixture.Api.ClientAsAsync(Roles.Staff, "settings");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Url)).StatusCode);
        var body = Body($"Ruxsat {tag}", [], ("08:00", "08:45"));
        foreach (var client in new[] { viewer, outsider, teacher })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Url, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"{Url}/{Guid.NewGuid()}")).StatusCode);
        }

        var created = await editor.PostAsJsonAsync(Url, body);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await editor.DeleteAsync($"{Url}/{id}")).StatusCode);
    }

    [Fact]
    public void Darslar_tartiblanadi_va_qayta_raqamlanadi()
    {
        var (periods, error) = BellScheduleService.NormalizePeriods(
        [
            new LessonTimeDto(7, "9:00", "09:45"),
            new LessonTimeDto(3, "08:00", "08:45"),
        ]);
        Assert.Null(error);
        Assert.Equal([new LessonTimeDto(1, "08:00", "08:45"), new LessonTimeDto(2, "09:00", "09:45")], periods);

        Assert.NotNull(BellScheduleService.NormalizePeriods(
            [.. Enumerable.Range(1, BellScheduleService.MaxPeriods + 1)
                .Select(i => new LessonTimeDto(i, $"{i + 7:00}:00", $"{i + 7:00}:30"))]).Error);
    }
}
