using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// "🏫 Panelni ochish" — the bot's keyboard button (client, 2026-09-28). A keyboard-launched Mini
/// App has no <c>initData</c>, so the button URL carries a personal key that
/// <c>POST /api/tg/auth/key</c> trades for the usual session.
///
/// <para>
/// <b>The key is a password</b> (it opens an admin's panel too): the tests hold that a replaced or
/// expired key is dead, that nothing but its hash is stored, and that "Chiqish" from such a
/// session unlinks only THAT Telegram. No network: bot replies land in a recording handler.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TelegramPanelKeyTests(ApiFixture fixture)
{
    private const string MiniAppUrl = "https://lms.test/tg/";
    private static long _telegram = 8_100_000_000 + Random.Shared.Next(1, 90_000_000);

    // ------------------------------------------------------------------ kalit → sessiya

    [Fact]
    public async Task Kalit_oddiy_sessiyaga_almashadi_va_faqat_xeshi_saqlanadi()
    {
        var (user, account) = await LinkedAsync(Roles.Teacher);
        var key = await IssueAsync(account.Id);

        using var anonymous = fixture.Api.AnonymousClient();
        var response = await anonymous.PostAsJsonAsync("/api/tg/auth/key", new { key });

        var body = await ReadAsync(response);
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("token").GetString()));
        Assert.Equal(user.Id, body.GetProperty("user").GetProperty("id").GetString());
        await fixture.Api.WithDbAsync(async db =>
        {
            var row = await db.TelegramAccounts.SingleAsync(a => a.Id == account.Id);
            Assert.NotEqual(key, row.PanelKeyHash);
            Assert.Equal(TelegramPanelKeys.Hash(key), row.PanelKeyHash);
        });
    }

    [Fact]
    public async Task Yangi_tugma_eski_kalitni_oldiradi()
    {
        var (_, account) = await LinkedAsync(Roles.Teacher);
        var old = await IssueAsync(account.Id);
        var fresh = await IssueAsync(account.Id);
        using var anonymous = fixture.Api.AnonymousClient();

        var stale = await anonymous.PostAsJsonAsync("/api/tg/auth/key", new { key = old });
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal("panel_key_invalid", (await ReadAnyAsync(stale)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/tg/auth/key", new { key = fresh })).StatusCode);
    }

    [Fact]
    public async Task Muddati_otgan_yoki_soxta_kalit_401()
    {
        var (_, account) = await LinkedAsync(Roles.Teacher);
        var key = await IssueAsync(account.Id);
        await fixture.Api.WithDbAsync(async db =>
        {
            var row = await db.TelegramAccounts.SingleAsync(a => a.Id == account.Id);
            row.PanelKeyIssuedAt = AppClock.Now - TelegramPanelKeys.Lifetime - TimeSpan.FromDays(1);
            await db.SaveChangesAsync();
        });
        using var anonymous = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/tg/auth/key", new { key })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/tg/auth/key", new { key = new string('A', 43) })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/tg/auth/key", new { key = "" })).StatusCode);
    }

    [Fact]
    public async Task Tugmadan_chiqish_faqat_shu_Telegramni_uzadi()
    {
        var (user, father) = await LinkedAsync(Roles.Teacher);
        var mother = await AddAccountAsync(user.Id);
        var key = await IssueAsync(father.Id);
        using var client = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email));

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/tg/link");
        request.Headers.Add("X-Telegram-Panel-Key", key);
        var response = await client.SendAsync(request);

        // 200 with the link ticket in the body (2026-10-07) — this Telegram may link another account.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await fixture.Api.WithDbAsync(async db =>
        {
            Assert.False(await db.TelegramAccounts.AnyAsync(a => a.Id == father.Id));
            Assert.True(await db.TelegramAccounts.AnyAsync(a => a.Id == mother.Id));
        });
        // the key died with its link
        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/tg/auth/key", new { key })).StatusCode);
    }

    /// <summary>
    /// Client, 2026-10-07: "kirilgan accountidan chiqib keyin boshqa akkountga kirmoqchi bo'lsa yana
    /// eskiga kirib qolyapti". The session knows which Telegram it came from, so "Chiqish" works even
    /// with a key the bot has since replaced, hands back a link ticket, and the same Telegram then
    /// signs in to ANOTHER account with login + password — no initData needed.
    /// </summary>
    [Fact]
    public async Task Chiqib_boshqa_akkauntga_kirsa_yangisi_ochiladi_eskisiga_qaytmaydi()
    {
        var (first, account) = await LinkedAsync(Roles.Teacher);
        var other = await AddAccountAsync(first.Id);              // the same login on another phone
        var (second, password) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var key = await IssueAsync(account.Id);

        using var anonymous = fixture.Api.AnonymousClient();
        var login = await ReadAnyAsync(await anonymous.PostAsJsonAsync("/api/tg/auth/key", new { key }));
        var token = login.GetProperty("token").GetString()!;
        await IssueAsync(account.Id);                             // the bot sent a new keyboard meanwhile

        using var session = fixture.Api.ClientWithToken(token);
        using var leave = new HttpRequestMessage(HttpMethod.Delete, "/api/tg/link");
        leave.Headers.Add("X-Telegram-Panel-Key", key);           // stale — refused before the fix
        var left = await session.SendAsync(leave);
        Assert.Equal(HttpStatusCode.OK, left.StatusCode);
        var ticket = left.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("tg_link=", StringComparison.Ordinal));
        await fixture.Api.WithDbAsync(async db =>
        {
            Assert.False(await db.TelegramAccounts.AnyAsync(a => a.Id == account.Id));
            Assert.True(await db.TelegramAccounts.AnyAsync(a => a.Id == other.Id));   // only this Telegram left
        });

        using var relink = new HttpRequestMessage(HttpMethod.Post, "/api/tg/link-login")
        {
            Content = JsonContent.Create(new { login = second.Email, password, initData = "" }),
        };
        relink.Headers.Add("Cookie", ticket.Split(';')[0]);
        var linked = await anonymous.SendAsync(relink);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.Equal(second.Id, (await ReadAnyAsync(linked)).GetProperty("user").GetProperty("id").GetString());
        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(second.Id, (await db.TelegramAccounts.SingleAsync(a => a.TelegramUserId == account.TelegramUserId)).UserId));
    }

    /// <summary>
    /// Telegram Web runs the Mini App in a cross-site iframe — the ticket cookie may not come back.
    /// The ticket is in the unlink body too, and link-login takes it from a header.
    /// </summary>
    [Fact]
    public async Task Chipta_cookiesiz_sarlavha_bilan_ham_ishlaydi()
    {
        var (first, account) = await LinkedAsync(Roles.Teacher);
        var (second, password) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var key = await IssueAsync(account.Id);

        using var anonymous = fixture.Api.AnonymousClient();
        var token = (await ReadAnyAsync(await anonymous.PostAsJsonAsync("/api/tg/auth/key", new { key }))).GetProperty("token").GetString()!;
        using var session = fixture.Api.ClientWithToken(token);
        var left = await session.DeleteAsync("/api/tg/link");
        Assert.Equal(HttpStatusCode.OK, left.StatusCode);
        var ticket = (await ReadAnyAsync(left)).GetProperty("ticket").GetString()!;

        using var relink = new HttpRequestMessage(HttpMethod.Post, "/api/tg/link-login")
        {
            Content = JsonContent.Create(new { login = second.Email, password, initData = "" }),
        };
        relink.Headers.Add("X-Telegram-Link-Ticket", ticket);          // no cookie at all
        var linked = await anonymous.SendAsync(relink);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(second.Id, (await db.TelegramAccounts.SingleAsync(a => a.TelegramUserId == account.TelegramUserId)).UserId));
        _ = first;
    }

    /// <summary>
    /// The linked account was archived: the button no longer dead-ends on "akkaunt arxivlangan" —
    /// the link goes and the same Telegram signs in to another account.
    /// </summary>
    [Fact]
    public async Task Arxivlangan_akkaunt_boglanishi_uziladi_va_boshqasiga_ulanadi()
    {
        var (archived, account) = await LinkedAsync(Roles.Teacher);
        var (other, password) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var key = await IssueAsync(account.Id);
        await fixture.Api.WithDbAsync(async db =>
        {
            (await db.Teachers.SingleAsync(t => t.UserId == archived.Id)).IsArchived = true;
            await db.SaveChangesAsync();
        });

        using var anonymous = fixture.Api.AnonymousClient();
        var opened = await anonymous.PostAsJsonAsync("/api/tg/auth/key", new { key });
        Assert.Equal(HttpStatusCode.Conflict, opened.StatusCode);
        var body = await ReadAnyAsync(opened);
        Assert.Equal("not_linked", body.GetProperty("code").GetString());
        Assert.Contains("arxivlangan", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        await fixture.Api.WithDbAsync(async db => Assert.False(await db.TelegramAccounts.AnyAsync(a => a.Id == account.Id)));

        using var relink = new HttpRequestMessage(HttpMethod.Post, "/api/tg/link-login")
        {
            Content = JsonContent.Create(new { login = other.Email, password, initData = "" }),
        };
        relink.Headers.Add("X-Telegram-Link-Ticket", body.GetProperty("ticket").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.SendAsync(relink)).StatusCode);
    }

    // ------------------------------------------------------------------ bot

    [Fact]
    public async Task Boglangan_chatga_start_pastki_panel_tugmasini_beradi()
    {
        var (_, account) = await LinkedAsync(Roles.Teacher);
        var handler = new RecordingHandler();

        await Bot(handler).HandleUpdateAsync(TextUpdate(account.TelegramUserId, "/start"), default);

        var reply = Assert.Single(handler.Requests);
        var markup = reply.GetProperty("reply_markup");
        Assert.True(markup.GetProperty("is_persistent").GetBoolean());
        var button = markup.GetProperty("keyboard")[0][0];
        Assert.Equal(TelegramPanelKeys.ButtonText, button.GetProperty("text").GetString());
        var url = button.GetProperty("web_app").GetProperty("url").GetString()!;
        Assert.StartsWith(MiniAppUrl + "?k=", url);

        // the key in the button is the account's current key
        var key = Uri.UnescapeDataString(url[(MiniAppUrl.Length + 3)..]);
        await fixture.Api.WithDbAsync(async db =>
        {
            var found = await TelegramPanelKeys.ResolveAsync(db, key);
            Assert.Equal(account.Id, found?.Id);
        });
    }

    [Fact]
    public async Task Boglanmagan_chatga_faqat_panelga_kirish_tugmasi_telefon_yoq()
    {
        var handler = new RecordingHandler();
        var chat = Interlocked.Increment(ref _telegram);

        await Bot(handler).HandleUpdateAsync(TextUpdate(chat, "/start"), default);

        // No phone button (client, 2026-10-08): the old keyboard is closed, the sign-in button follows.
        Assert.Equal(2, handler.Requests.Count);
        Assert.True(handler.Requests[0].GetProperty("reply_markup").GetProperty("remove_keyboard").GetBoolean());
        Assert.DoesNotContain(handler.Requests, r => r.ToString().Contains("request_contact"));
        var inline = handler.Requests[1].GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0];
        // inline web_app DOES carry initData — no key in this URL
        Assert.Equal(MiniAppUrl, inline.GetProperty("web_app").GetProperty("url").GetString());
    }

    [Fact]
    public async Task Birinchi_ishga_tushishda_hamma_boglanganlarga_bir_marta_yuboriladi()
    {
        var (_, account) = await LinkedAsync(Roles.Teacher);
        var handler = new RecordingHandler();
        var bot = Bot(handler);

        Assert.True(await bot.SendMissingPanelKeyboardsAsync() >= 1);
        Assert.Contains(handler.Requests, r => r.GetProperty("chat_id").GetInt64() == account.TelegramUserId);
        await fixture.Api.WithDbAsync(async db =>
            Assert.NotNull((await db.TelegramAccounts.SingleAsync(a => a.Id == account.Id)).PanelKeyIssuedAt));

        Assert.Equal(0, await bot.SendMissingPanelKeyboardsAsync()); // once only
    }

    // ------------------------------------------------------------------ yordamchilar

    private async Task<(AppUser User, TelegramAccount Account)> LinkedAsync(string role)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role);
        return (user, await AddAccountAsync(user.Id));
    }

    private async Task<TelegramAccount> AddAccountAsync(string userId)
    {
        var account = new TelegramAccount
        {
            TelegramUserId = Interlocked.Increment(ref _telegram),
            UserId = userId,
            DisplayName = "Test",
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.TelegramAccounts.Add(account);
            await db.SaveChangesAsync();
        });
        return account;
    }

    private async Task<string> IssueAsync(string accountId)
    {
        var key = "";
        await fixture.Api.WithDbAsync(async db =>
        {
            key = TelegramPanelKeys.Issue(await db.TelegramAccounts.SingleAsync(a => a.Id == accountId));
            await db.SaveChangesAsync();
        });
        return key;
    }

    private static JsonElement TextUpdate(long chat, string text)
    {
        var json = JsonSerializer.Serialize(new
        {
            update_id = 1,
            message = new
            {
                message_id = 1,
                chat = new { id = chat, type = "private" },
                from = new { id = chat, first_name = "Test" },
                text,
            },
        });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }

    private static async Task<JsonElement> ReadAnyAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    /// <summary>Tokeni bor ("sozlangan"), lekin har HTTP so'rov <paramref name="handler"/> da qoladi.</summary>
    private TelegramBotService Bot(RecordingHandler handler)
    {
        var telegram = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(),
            new SingleHandlerHttpClientFactory(handler),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        telegram.Set("test-token", "test_bot");
        return new TelegramBotService(
            fixture.Api.Services, telegram, fixture.Api.Services.GetRequiredService<ILogger<TelegramBotService>>())
        {
            MiniAppUrl = MiniAppUrl,
        };
    }

    /// <summary>Har so'rov tanasini yozib oladi va 200 qaytaradi — tarmoqqa hech narsa chiqmaydi.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<JsonElement> _requests = [];

        public IReadOnlyList<JsonElement> Requests
        {
            get { lock (_requests) return [.. _requests]; }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            lock (_requests) _requests.Add(json.RootElement.Clone());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }
    }

    private sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
