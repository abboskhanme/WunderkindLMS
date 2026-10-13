using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  The bot token never leaves the server (2026-10-13: it was stolen; Sozlamalar → Telegram showed it
//  in full to everyone with the settings key). The screen gets only "botid:…xyz"; an empty field keeps
//  the stored token; a replacement is audited without the token itself.
//
//  The controller is built here with its OWN TelegramService (no network): the shared test host's
//  service — and the bot loop that polls Telegram once it has a token — is never given one.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class TelegramTokenSettingsTests(ApiFixture fixture)
{
    private const string FakeToken = "111222333:AAFakeTokenForTestsOnly-xyz";

    [Fact]
    public async Task Token_ekranga_chiqmaydi_bosh_maydon_eskisini_saqlaydi_almashtirish_auditda()
    {
        using var scope = fixture.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // The shared test database may have no settings row yet; then this test owns one and removes it.
        var meta = await db.SchoolMeta.FirstOrDefaultAsync();
        var created = meta is null;
        if (meta is null)
        {
            meta = new SchoolMeta();
            db.SchoolMeta.Add(meta);
            await db.SaveChangesAsync();
        }
        var (oldToken, oldUser, oldName) = (meta.TelegramBotToken, meta.TelegramBotUsername, meta.TelegramBotName);
        try
        {
            var controller = new SettingsController(
                db, OfflineTelegram(), scope.ServiceProvider.GetRequiredService<ContractService>(),
                scope.ServiceProvider.GetRequiredService<AuditService>());

            // A new token: stored, never echoed back — only its hint.
            var saved = Value(await controller.SaveTelegram(new SaveTelegramSettingsRequest(FakeToken, "@test_bot", "Test")));
            Assert.Equal("", saved.BotToken);
            Assert.Equal("111222333:…xyz", saved.TokenHint);
            Assert.True(saved.Configured);

            var read = Value(await controller.GetTelegram());
            Assert.Equal("", read.BotToken);
            Assert.Equal("111222333:…xyz", read.TokenHint);
            Assert.Equal("test_bot", read.BotUsername);

            // Saving with the token field empty (the screen never has it) keeps the token.
            Value(await controller.SaveTelegram(new SaveTelegramSettingsRequest("", "other_bot", "Test")));
            await using (var check = PostgresFixture.NewContext(fixture.Database.OwnerConnectionString))
            {
                var row = await check.SchoolMeta.AsNoTracking().SingleAsync(m => m.Id == meta.Id);
                Assert.Equal(FakeToken, row.TelegramBotToken);
                Assert.Equal("other_bot", row.TelegramBotUsername);

                // The replacement is in the audit log — by hint, never the token.
                var audits = await check.AuditLogs.AsNoTracking()
                    .Where(a => a.EntityType == "TelegramBot" && a.Summary.Contains("111222333:…xyz")).ToListAsync();
                Assert.NotEmpty(audits);
                Assert.DoesNotContain(audits, a => a.Summary.Contains(FakeToken)
                                                   || (a.Before ?? "").Contains(FakeToken) || (a.After ?? "").Contains(FakeToken));
            }
        }
        finally
        {
            if (created) db.SchoolMeta.Remove(meta);
            else
            {
                meta.TelegramBotToken = oldToken;
                meta.TelegramBotUsername = oldUser;
                meta.TelegramBotName = oldName;
            }
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Api_javobida_token_yoq()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var res = await admin.GetAsync("/api/admin/settings/telegram");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = (await res.Content.ReadFromJsonAsync<TelegramSettingsDto>())!;
        Assert.Equal("", dto.BotToken);
    }

    private static T Value<T>(ActionResult<T> result) => result.Value!;

    private TelegramService OfflineTelegram() => new(
        fixture.Api.Services.GetRequiredService<IConfiguration>(),
        new NoNetwork(),
        fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());

    /// <summary>Any HTTP call fails at once — nothing leaves the machine.</summary>
    private sealed class NoNetwork : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Refuse());

        private sealed class Refuse : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
