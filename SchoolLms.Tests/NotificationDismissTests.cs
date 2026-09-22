using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Bildirishnomalar: o'qilgani 1 kundan keyin yo'qoladi, qo'lda o'chirish (bittalab va
/// ko'plab) — va bu faqat SHU foydalanuvchiga ta'sir qiladi (mijoz, 2026-09-22).
///
/// <para>Manba sifatida hal qilinmagan taklif (<c>feedbacks.status = 'new'</c>) olinadi —
/// u barcha adminlarga ko'rinadi. Umumiy baza: har test o'z taklifini yaratadi va
/// faqat o'sha id'ni kuzatadi.</para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class NotificationDismissTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/notifications";

    private async Task<string> SeedFeedbackAsync(DateTime? createdAt = null)
    {
        var f = new Feedback
        {
            Type = "suggestion",
            Text = "Bildirishnoma testi " + Guid.NewGuid().ToString("N")[..6],
            SenderName = "Test ota-ona",
            CreatedAt = createdAt ?? AppClock.Now,
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Feedbacks.Add(f);
            await db.SaveChangesAsync();
        });
        return "feedback:" + f.Id;
    }

    private static async Task<List<NotificationDto>> ListAsync(HttpClient c)
    {
        var res = await c.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<NotificationListDto>())!.Items;
    }

    private static Task<HttpResponseMessage> DismissAsync(HttpClient c, params string[] ids) =>
        c.PostAsJsonAsync($"{Url}/dismiss", new { ids });

    [Fact]
    public async Task Bittasini_ochirish_faqat_shu_foydalanuvchidan_yashiradi()
    {
        var id = await SeedFeedbackAsync();
        using var me = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var colleague = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.Contains(await ListAsync(me), n => n.Id == id);
        Assert.Equal(HttpStatusCode.NoContent, (await DismissAsync(me, id)).StatusCode);

        Assert.DoesNotContain(await ListAsync(me), n => n.Id == id);
        // Hamkasb uchun hech narsa o'zgarmagan, voqeaning o'zi ham joyida.
        Assert.Contains(await ListAsync(colleague), n => n.Id == id);
    }

    [Fact]
    public async Task Belgilanganlarning_hammasi_birdan_ochiriladi()
    {
        var a = await SeedFeedbackAsync();
        var b = await SeedFeedbackAsync();
        using var me = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.Equal(HttpStatusCode.NoContent, (await DismissAsync(me, a, b, "mavjud-emas:1")).StatusCode);

        var items = await ListAsync(me);
        Assert.DoesNotContain(items, n => n.Id == a || n.Id == b);
    }

    [Fact]
    public async Task Oqilgani_bir_kundan_keyin_yoqoladi_oqilmagani_qoladi()
    {
        var id = await SeedFeedbackAsync();
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        using var me = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Admin, user.Id, user.FullName, user.Email));

        // O'qildi — hali ro'yxatda, lekin "yangi" emas.
        Assert.Equal(HttpStatusCode.NoContent, (await me.PostAsync($"{Url}/read", null)).StatusCode);
        var read = Assert.Single(await ListAsync(me), n => n.Id == id);
        Assert.False(read.IsNew);

        // O'qilganiga 25 soat bo'ldi — ro'yxatdan yo'qoladi.
        await fixture.Api.WithDbAsync(db => db.NotificationStates
            .Where(s => s.UserId == user.Id && s.NotificationId == id)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.ReadAt, AppClock.Now.AddHours(-25))));
        Assert.DoesNotContain(await ListAsync(me), n => n.Id == id);

        // O'qilmagan yangi voqea esa ko'rinadi va "yangi".
        var fresh = await SeedFeedbackAsync();
        Assert.True(Assert.Single(await ListAsync(me), n => n.Id == fresh).IsNew);
    }

    /// <summary>
    /// Jadvaldan OLDIN o'qilganlar (faqat umumiy <c>notifications_read_at</c> bor edi)
    /// keyingi "o'qildi" bosilganda yangi bir kunga cho'zilib ketmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Eski_umumiy_belgi_bilan_oqilgani_qayta_tirilmaydi()
    {
        var id = await SeedFeedbackAsync(AppClock.Now.AddDays(-3));
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        await fixture.Api.WithDbAsync(async db =>
        {
            db.UserSettings.Add(new UserSettings { UserId = user.Id, NotificationsReadAt = AppClock.Now.AddDays(-2) });
            await db.SaveChangesAsync();
        });
        using var me = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Admin, user.Id, user.FullName, user.Email));

        Assert.DoesNotContain(await ListAsync(me), n => n.Id == id);
        await me.PostAsync($"{Url}/read", null);
        Assert.DoesNotContain(await ListAsync(me), n => n.Id == id);
    }

    [Fact]
    public async Task Bosh_royxat_400()
    {
        using var me = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await DismissAsync(me)).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Boshqa_rollar_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await DismissAsync(client, "feedback:x")).StatusCode);
    }

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await DismissAsync(client, "feedback:x")).StatusCode);
    }

    /// <summary>
    /// Bitta bildirishnoma ochilganda faqat o'sha o'qiladi — qolganlari "yangi" bo'lib qoladi
    /// va 1 kunlik o'chish muddati ular uchun boshlanmaydi (code review, 2026-09-23).
    /// </summary>
    [Fact]
    public async Task Bittasini_oqish_qolganlarini_yangi_qoldiradi()
    {
        var a = await SeedFeedbackAsync();
        var b = await SeedFeedbackAsync();
        using var me = await fixture.Api.ClientAsAsync(Roles.Admin);

        var res = await me.PostAsJsonAsync($"{Url}/read", new { ids = new[] { a } });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var items = await ListAsync(me);
        Assert.False(Assert.Single(items, n => n.Id == a).IsNew);
        Assert.True(Assert.Single(items, n => n.Id == b).IsNew);
    }

    /// <summary>
    /// Parallel "o'chirish" va "o'qildi" (ikki tab yoki tez bosish) — bir xil kalitga
    /// ikki INSERT. Ilgari biri 500 berardi; endi upsert, ikkalasi ham muvaffaqiyatli.
    /// </summary>
    [Fact]
    public async Task Parallel_sorovlar_500_bermaydi()
    {
        var id = await SeedFeedbackAsync();
        using var me = await fixture.Api.ClientAsAsync(Roles.Admin);

        var tasks = Enumerable.Range(0, 6).Select(i => i % 2 == 0
            ? DismissAsync(me, id)
            : me.PostAsJsonAsync($"{Url}/read", new { ids = new[] { id } })).ToList();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.DoesNotContain(await ListAsync(me), n => n.Id == id);
    }
}
