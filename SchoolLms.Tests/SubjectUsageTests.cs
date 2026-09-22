using System.Net;
using System.Net.Http.Json;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// <c>GET /api/admin/subjects/{id}/usage</c> — o'chirish oynasi oldindan so'raydigan ro'yxat
/// (mijoz, 2026-09-23: guruh yoki sinfga biriktirilgan fan o'chirilmasin). O'chirish ham
/// aynan shu ro'yxat bilan rad etadi.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class SubjectUsageTests(ApiFixture fixture)
{
    [Fact]
    public async Task Ishlatilmagan_fan_ochirsa_boladi_ishlatilgani_yoq()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var free = new Subject { Name = $"Bo'sh {tag}" };
        var used = new Subject { Name = $"Band {tag}" };
        var cls = new SchoolClass { Name = $"U-{tag}", Grade = 3 };
        var tpl = new ScheduleTemplate { ClassId = cls.Id, Name = "asosiy" };
        tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = 0, Period = 1, SubjectId = used.Id });
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Subjects.AddRange(free, used);
            db.Classes.Add(cls);
            db.ScheduleTemplates.Add(tpl);
            await db.SaveChangesAsync();
        });
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var a = (await admin.GetFromJsonAsync<SubjectUsageDto>($"/api/admin/subjects/{free.Id}/usage"))!;
        Assert.True(a.CanDelete);
        Assert.Empty(a.UsedIn);

        var b = (await admin.GetFromJsonAsync<SubjectUsageDto>($"/api/admin/subjects/{used.Id}/usage"))!;
        Assert.False(b.CanDelete);
        Assert.Contains(b.UsedIn, u => u.Contains("dars jadvali"));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/subjects/{used.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/subjects/{free.Id}")).StatusCode);
    }

    [Fact]
    public async Task Topilmagan_fan_404_va_tokensiz_401()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/admin/subjects/yoq/usage")).StatusCode);
        using var anon = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/admin/subjects/yoq/usage")).StatusCode);
    }
}
